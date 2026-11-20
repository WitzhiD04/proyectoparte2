package plataforma.adaptador.se8.nucleo;

import java.time.Clock;
import java.util.Optional;
import plataforma.adaptador.se8.contrato.ContratoAdaptador;
import plataforma.adaptador.se8.contrato.Sobre;
import plataforma.adaptador.se8.persistencia.AlmacenSe8;
import plataforma.adaptador.se8.persistencia.Entrega;
import plataforma.adaptador.se8.traduccion.AcuseSe8;

/**
 * Aplica un acuse de SE-8 a su entrega, dentro de la transacción de quien lo llama. Es idempotente: un acuse repetido o
 * tardío no cambia nada. Solo aquí una entrega pasa a ENTREGADA (QA-08). Si la alerta vino de Notificación (EV-28),
 * EV-29 sale por la bandeja en la misma transacción (AD-45).
 */
public final class AplicadorAcuses {

    public enum Resultado { APLICADO, REPETIDO, DESCONOCIDO }

    private final AlmacenSe8 almacen;
    private final ContratoAdaptador<AcuseSe8> adaptador;
    private final Clock reloj;

    public AplicadorAcuses(AlmacenSe8 almacen, ContratoAdaptador<AcuseSe8> adaptador, Clock reloj) {
        this.almacen = almacen;
        this.adaptador = adaptador;
        this.reloj = reloj;
    }

    public Resultado aplicar(AcuseSe8 acuse) {
        Optional<Entrega> encontrada = almacen.buscarEntrega(acuse.referenciaExterna());
        if (encontrada.isEmpty()) {
            // La transacción que registró el envío aún no confirma, o la alerta no es de esta plataforma. SE-8 reintenta
            // el aviso y la conciliación por marca de avance lo recoge después.
            return Resultado.DESCONOCIDO;
        }
        Entrega entrega = encontrada.get();
        if (!entrega.registrarAcuse(acuse, reloj.instant())) {
            return Resultado.REPETIDO;
        }
        if (entrega.deNotificacion()) {
            Sobre ev29 = adaptador.traducir(acuse);
            if (ev29 != null) {
                almacen.agregarABandeja(ev29);
            }
        }
        return Resultado.APLICADO;
    }
}
