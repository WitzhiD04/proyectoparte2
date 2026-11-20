package plataforma.adaptador.se8.nucleo;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.List;
import plataforma.adaptador.se8.cliente.ClienteSe8;
import plataforma.adaptador.se8.contrato.EntregaRechazadaException;
import plataforma.adaptador.se8.contrato.SistemaExternoNoDisponibleException;
import plataforma.adaptador.se8.persistencia.AlmacenSe8;
import plataforma.adaptador.se8.persistencia.Entrega;

/**
 * Reenvío por falta de acuse (QA-08: "si un canal no acusa en 5 s se reintenta y queda pendiente"). Es un mecanismo
 * distinto de la resiliencia de cada llamada (Cuadro 11): aquí SE-8 sí recibió la alerta, pero no la acusó.
 * <ul>
 * <li>Solo para el modo sustituto (EV-03, EV-04). Con EV-28, el reintento lo decide Notificación, dueña del estado
 *     (AD-13): publica un EV-28 nuevo con el intento siguiente.</li>
 * <li>Una entrega ENVIADA sin acuse después de {@code segundosSinAcuse} pasa a PENDIENTE y se reenvía con la misma
 *     referencia (SE-8 no la duplica). Si SE-8 no responde, queda PENDIENTE para la próxima revisión.</li>
 * <li>Al llegar a {@code maximoEnvios} sin acuse, pasa a FALLIDA.</li>
 * </ul>
 * Usa el cliente directamente (con la misma resiliencia y el mismo cortacircuitos), no el EJB del contrato, para que un
 * SE-8 caído no revierta el paso a PENDIENTE.
 */
public final class ReenvioSinAcuse {

    public record Resumen(int reenviadas, int pendientes, int fallidas) {
    }

    private final AlmacenSe8 almacen;
    private final ClienteSe8 cliente;
    private final Duration sinAcuse;
    private final int maximoEnvios;
    private final String organismo;
    private final Clock reloj;

    public ReenvioSinAcuse(AlmacenSe8 almacen, ClienteSe8 cliente, Duration sinAcuse, int maximoEnvios, String organismo,
            Clock reloj) {
        this.almacen = almacen;
        this.cliente = cliente;
        this.sinAcuse = sinAcuse;
        this.maximoEnvios = maximoEnvios;
        this.organismo = organismo;
        this.reloj = reloj;
    }

    public Resumen revisar(int limite) {
        Instant ahora = reloj.instant();
        List<Entrega> candidatas = almacen.entregasSinAcuse(ahora.minus(sinAcuse), limite);
        int reenviadas = 0;
        int pendientes = 0;
        int fallidas = 0;
        for (Entrega e : candidatas) {
            if (e.getEnvios() >= maximoEnvios) {
                e.marcarFallida("Sin acuse de SE-8 tras " + e.getEnvios() + " envíos");
                fallidas++;
                continue;
            }
            if (e.getEstado() == Entrega.Estado.ENVIADA) {
                e.marcarSinAcuse();
            }
            if (cliente.ejecutor().circuito().abierto()) {
                pendientes++; // la compuerta espera a que SE-8 vuelva
                continue;
            }
            try {
                cliente.enviar(e.alerta(organismo));
                e.marcarEnviada(reloj.instant());
                reenviadas++;
            } catch (SistemaExternoNoDisponibleException noResponde) {
                pendientes++;
            } catch (EntregaRechazadaException rechazo) {
                e.marcarFallida(rechazo.getMessage());
                fallidas++;
            }
        }
        return new Resumen(reenviadas, pendientes, fallidas);
    }
}
