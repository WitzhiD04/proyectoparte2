package plataforma.adaptador.se8.contrato;

/**
 * Resultado de entregar un evento al sistema externo. Equivale a {@code Plataforma.Comun.Adaptadores.ResultadoEntrega}.
 * Para SE-8, "aceptada" no es "entregada": la entrega se confirma solo con el acuse (QA-08).
 */
public enum ResultadoEntrega {
    /** SE-8 recibió la alerta; falta su acuse. */
    ACEPTADA,
    /** SE-8 ya la tenía (misma clave de idempotencia): no hay efecto nuevo. */
    REPETIDA,
    /** El evento no se envía a SE-8 (tipo que no le corresponde o modo sustituto apagado). */
    OMITIDA
}
