package plataforma.adaptador.se8.cliente;

import plataforma.adaptador.se8.contrato.EntregaRechazadaException;
import plataforma.adaptador.se8.contrato.ResultadoEntrega;
import plataforma.adaptador.se8.resiliencia.EjecutorResiliente;
import plataforma.adaptador.se8.traduccion.AlertaSe8;

/**
 * Cliente de SE-8 con la resiliencia del Cuadro 10. Todas sus operaciones son idempotentes (PUT con referencia, GET), así
 * que se reintentan. Sin base ni bus.
 */
public final class ClienteSe8 {

    private final TransporteSe8 transporte;
    private final EjecutorResiliente ejecutor;

    public ClienteSe8(TransporteSe8 transporte, EjecutorResiliente ejecutor) {
        this.transporte = transporte;
        this.ejecutor = ejecutor;
    }

    public EjecutorResiliente ejecutor() {
        return ejecutor;
    }

    /**
     * Envía la alerta. 201 o 202: SE-8 la recibió y acusará después. 200: ya la tenía (misma referencia). 4xx: rechazo
     * permanente. Sin respuesta o 5xx: {@code SistemaExternoNoDisponibleException} al agotar la resiliencia.
     */
    public ResultadoEntrega enviar(AlertaSe8 alerta) {
        TransporteSe8.Respuesta respuesta = ejecutor.ejecutar("PUT /v1/alertas", true, t -> transporte.enviar(alerta, t));
        int estado = respuesta.estado();
        if (estado == 201 || estado == 202) {
            return ResultadoEntrega.ACEPTADA;
        }
        if (estado == 200 || estado == 204) {
            return ResultadoEntrega.REPETIDA;
        }
        throw new EntregaRechazadaException("SE-8 rechazó la alerta " + alerta.referenciaExterna() + " con " + estado
                + (respuesta.cuerpo() == null || respuesta.cuerpo().isBlank() ? "" : ": " + respuesta.cuerpo()));
    }

    /** Sondeo del cortacircuitos. Con la apertura vencida, esta llamada es la prueba: si responde, lo cierra. */
    public void sondear() {
        ejecutor.ejecutar("GET /v1/salud", true, t -> {
            transporte.salud(t);
            return null;
        });
    }

    public TransporteSe8.PaginaAcuses acusesDesde(long desde, int limite) {
        return ejecutor.ejecutar("GET /v1/acuses", true, t -> transporte.acuses(desde, limite, t));
    }
}
