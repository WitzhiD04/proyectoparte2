package plataforma.adaptador.se8.contrato;

import jakarta.ejb.ApplicationException;

/**
 * El sistema externo no respondió, respondió con un error transitorio (5xx, 408, 429) o el circuito está abierto. No es
 * un fallo del mensaje: no cuenta para el límite de entregas. Revierte la transacción en curso.
 */
@ApplicationException(rollback = true)
public class SistemaExternoNoDisponibleException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    public SistemaExternoNoDisponibleException(String mensaje) {
        super(mensaje);
    }

    public SistemaExternoNoDisponibleException(String mensaje, Throwable causa) {
        super(mensaje, causa);
    }
}
