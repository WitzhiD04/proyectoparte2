package plataforma.adaptador.se8.resiliencia;

/**
 * Un intento de llamada a SE-8 falló por una causa transitoria: tiempo agotado, error de red o respuesta 5xx, 408 o 429.
 * Cuenta como fallo para el cortacircuitos y se puede reintentar si la operación es idempotente.
 */
public class FalloTransitorioException extends Exception {

    private static final long serialVersionUID = 1L;

    public FalloTransitorioException(String mensaje) {
        super(mensaje);
    }

    public FalloTransitorioException(String mensaje, Throwable causa) {
        super(mensaje, causa);
    }
}
