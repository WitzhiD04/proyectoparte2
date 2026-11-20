package plataforma.adaptador.se8.contrato;

import jakarta.ejb.ApplicationException;

/** Un evento que no cumple el contrato. Es permanente: reintentarlo no lo arregla, y va a la cola de mensajes muertos.  Revierte la transacción en curso. */
@ApplicationException(rollback = true)
public class EventoInvalidoException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    public EventoInvalidoException(String mensaje) {
        super(mensaje);
    }

    public EventoInvalidoException(String mensaje, Throwable causa) {
        super(mensaje, causa);
    }
}
