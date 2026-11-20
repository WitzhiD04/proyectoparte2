package plataforma.adaptador.se8.traduccion;

/**
 * Una alerta en el formato propio de SE-8 (Comunicación y notificación). Es "el otro sistema": tiene su vocabulario
 * (medio, prioridad, referencia externa) y la plataforma no lo conoce fuera de este adaptador (AD-03).
 *
 * @param referenciaExterna clave de idempotencia hacia SE-8: la misma referencia no produce un segundo envío
 * @param medio EMAIL o SMS
 * @param destinatario dirección o número
 * @param titulo asunto
 * @param cuerpo texto de la alerta
 * @param prioridad BAJA, MEDIA, ALTA o CRITICA (de NIVEL_1 a NIVEL_4)
 * @param codigoEvento identificador del desastre
 * @param organismo organismo destinatario
 */
public record AlertaSe8(String referenciaExterna, String medio, String destinatario, String titulo, String cuerpo,
        String prioridad, String codigoEvento, String organismo) {
}
