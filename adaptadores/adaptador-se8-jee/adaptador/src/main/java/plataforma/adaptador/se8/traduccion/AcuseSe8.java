package plataforma.adaptador.se8.traduccion;

/**
 * Acuse de SE-8 para una alerta, en su formato. Llega por la ruta interna {@code POST /v1/acuses} o en la consulta
 * {@code GET /v1/acuses?desde=} que concilia los que no llegaron.
 *
 * @param referenciaExterna la referencia de la alerta (clave de envío)
 * @param secuencia consecutivo de acuses de SE-8 (marca de avance)
 * @param resultado ENTREGADA o RECHAZADA
 * @param motivo motivo del rechazo, si lo hay
 * @param instante instante del acuse en SE-8 (ISO-8601)
 */
public record AcuseSe8(String referenciaExterna, long secuencia, String resultado, String motivo, String instante) {

    public boolean entregada() {
        return "ENTREGADA".equals(resultado);
    }
}
