package plataforma.adaptador.se8.contrato;

import jakarta.ejb.ApplicationException;

/** El sistema externo rechazó el contenido (4xx). Es permanente.  No revierte la transacción: la entrega queda FALLIDA y se confirma. */
@ApplicationException(rollback = false)
public class EntregaRechazadaException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    public EntregaRechazadaException(String mensaje) {
        super(mensaje);
    }
}
