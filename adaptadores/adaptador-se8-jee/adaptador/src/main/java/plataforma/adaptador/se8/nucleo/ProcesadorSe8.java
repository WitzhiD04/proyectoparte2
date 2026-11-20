package plataforma.adaptador.se8.nucleo;

import java.time.Clock;
import java.time.Instant;
import java.util.Optional;
import plataforma.adaptador.se8.contrato.ContratoAdaptador;
import plataforma.adaptador.se8.contrato.EntregaRechazadaException;
import plataforma.adaptador.se8.contrato.Sobre;
import plataforma.adaptador.se8.persistencia.AlmacenSe8;
import plataforma.adaptador.se8.persistencia.Entrega;
import plataforma.adaptador.se8.traduccion.AcuseSe8;
import plataforma.adaptador.se8.traduccion.AlertaSe8;
import plataforma.adaptador.se8.traduccion.OpcionesSustituto;
import plataforma.adaptador.se8.traduccion.TraduccionSe8;

/**
 * Aplica un evento de {@code q.salida.se8} una sola vez, dentro de la transacción de quien lo llama (como
 * {@code ConsumidorIdempotente.AplicarAsync}):
 * <ol>
 * <li>Si la clave de idempotencia ya está en {@code mensajes_procesados}, no hace nada.</li>
 * <li>Traduce el evento a la alerta de SE-8 y registra la entrega PENDIENTE.</li>
 * <li>Entrega la alerta con el contrato del adaptador. Si SE-8 la recibe, la entrega pasa a ENVIADA (falta el acuse).
 *     Si SE-8 la rechaza (4xx), pasa a FALLIDA y, si vino de Notificación, sale EV-29 FALLIDA por la bandeja.</li>
 * <li>Registra el evento como procesado. La llave primaria impide un segundo registro aunque dos réplicas compitan.</li>
 * </ol>
 * Si SE-8 no responde, la excepción sale y la transacción se revierte: no queda nada a medias y el mensaje sigue en la
 * cola.
 */
public final class ProcesadorSe8 {

    public enum Resultado { APLICADO, YA_PROCESADO, OMITIDO, RECHAZADO }

    private final AlmacenSe8 almacen;
    private final ContratoAdaptador<AcuseSe8> adaptador;
    private final OpcionesSustituto sustituto;
    private final Clock reloj;

    public ProcesadorSe8(AlmacenSe8 almacen, ContratoAdaptador<AcuseSe8> adaptador, OpcionesSustituto sustituto, Clock reloj) {
        this.almacen = almacen;
        this.adaptador = adaptador;
        this.sustituto = sustituto;
        this.reloj = reloj;
    }

    public Resultado procesar(Sobre sobre) {
        if (almacen.yaProcesado(sobre.claveIdempotencia())) {
            return Resultado.YA_PROCESADO;
        }
        Instant ahora = reloj.instant();
        AlertaSe8 alerta = TraduccionSe8.aSe8(sobre, sustituto);
        if (alerta == null) {
            almacen.registrarProcesado(sobre, ahora);
            return Resultado.OMITIDO;
        }

        Optional<Entrega> existente = almacen.buscarEntrega(alerta.referenciaExterna());
        Entrega entrega;
        if (existente.isPresent()) {
            entrega = existente.get();
            if (entrega.getEstado() != Entrega.Estado.PENDIENTE) {
                // Otra copia del mismo envío (misma referencia hacia SE-8) que ya salió: no se envía dos veces.
                almacen.registrarProcesado(sobre, ahora);
                return Resultado.YA_PROCESADO;
            }
        } else {
            entrega = Entrega.nueva(alerta, sobre, ahora);
            almacen.guardar(entrega);
        }

        Resultado resultado;
        try {
            adaptador.entregar(sobre);
            entrega.marcarEnviada(reloj.instant());
            resultado = Resultado.APLICADO;
        } catch (EntregaRechazadaException e) {
            entrega.marcarFallida(e.getMessage());
            if (entrega.deNotificacion()) {
                AcuseSe8 rechazo = new AcuseSe8(alerta.referenciaExterna(), 0, "RECHAZADA", e.getMessage(), ahora.toString());
                almacen.agregarABandeja(adaptador.traducir(rechazo));
            }
            resultado = Resultado.RECHAZADO;
        }
        almacen.registrarProcesado(sobre, ahora);
        return resultado;
    }
}
