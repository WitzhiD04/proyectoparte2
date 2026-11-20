package plataforma.adaptador.se8.nucleo;

import java.time.Clock;
import plataforma.adaptador.se8.contrato.ContratoAdaptador;
import plataforma.adaptador.se8.persistencia.AlmacenSe8;
import plataforma.adaptador.se8.persistencia.MarcaAvance;
import plataforma.adaptador.se8.traduccion.AcuseSe8;

/**
 * Entrada del adaptador (AD-14): pide a SE-8 los acuses posteriores a la marca de avance y los aplica. Recoge los avisos
 * que no llegaron por la ruta interna (adaptador caído, aviso antes de confirmar el envío). La página y el avance de la
 * marca se confirman en la misma transacción: un reinicio a mitad repite solo lo no confirmado, y el bloqueo de la fila
 * de la marca impide que dos réplicas concilien a la vez.
 */
public final class ConciliadorAcuses {

    private final AlmacenSe8 almacen;
    private final ContratoAdaptador<AcuseSe8> adaptador;
    private final AplicadorAcuses aplicador;
    private final Clock reloj;

    public ConciliadorAcuses(AlmacenSe8 almacen, ContratoAdaptador<AcuseSe8> adaptador, AplicadorAcuses aplicador, Clock reloj) {
        this.almacen = almacen;
        this.adaptador = adaptador;
        this.aplicador = aplicador;
        this.reloj = reloj;
    }

    /** Concilia una página. Devuelve verdadero si SE-8 tiene más acuses después de ella. */
    public boolean conciliarPagina() {
        MarcaAvance marca = almacen.marca(MarcaAvance.FLUJO_ACUSES, reloj.instant());
        ContratoAdaptador.Lote<AcuseSe8> lote = adaptador.recuperarDesde(marca.getMarca());
        for (AcuseSe8 acuse : lote.cambios()) {
            aplicador.aplicar(acuse);
        }
        if (lote.marcaSiguiente() != null) {
            marca.avanzar(lote.marcaSiguiente(), reloj.instant());
        }
        return lote.hayMas();
    }
}
