package plataforma.adaptador.se8.contrato;

import java.util.List;

/**
 * Equivalente Java de {@code IContratoAdaptador<TExterno>} de {@code Plataforma.Comun.Adaptadores} (DA-11, AD-09). El
 * resto de la plataforma solo ve el evento canónico.
 *
 * <p>Salida: entregar un evento canónico al sistema externo, de forma idempotente. Entrada: recuperar los cambios del
 * sistema externo desde una marca de avance (AD-14) y traducir cada uno al evento canónico. Para SE-8 los cambios de
 * entrada son los acuses de entrega.</p>
 *
 * @param <E> un cambio en el formato del sistema externo
 */
public interface ContratoAdaptador<E> {

    /** Sistema externo, como lo nombra el SAD ("SE-8"). */
    String sistemaExterno();

    /**
     * Entrega el evento, traducido al formato del sistema externo. Debe ser idempotente: un mensaje se puede entregar más
     * de una vez. Lanza {@link SistemaExternoNoDisponibleException} si el sistema no responde y
     * {@link EntregaRechazadaException} si rechaza el contenido.
     */
    ResultadoEntrega entregar(Sobre eventoCanonico);

    /**
     * Cambios posteriores a {@code marcaAvance} (nula: desde el principio). La consulta es idempotente: la misma marca
     * devuelve los mismos cambios.
     */
    Lote<E> recuperarDesde(String marcaAvance);

    /** El evento canónico de un cambio externo, o nulo si el cambio no le interesa a la plataforma. */
    Sobre traducir(E externo);

    /** Una página de cambios del sistema externo y la marca que la cierra. */
    record Lote<E>(List<E> cambios, String marcaSiguiente, boolean hayMas) {
    }
}
