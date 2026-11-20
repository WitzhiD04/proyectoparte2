package plataforma.adaptador.se8.cliente;

import java.time.Duration;
import java.util.List;
import plataforma.adaptador.se8.resiliencia.FalloTransitorioException;
import plataforma.adaptador.se8.traduccion.AcuseSe8;
import plataforma.adaptador.se8.traduccion.AlertaSe8;

/**
 * Las tres operaciones HTTP de la API de SE-8. Todas son idempotentes, así que la resiliencia puede reintentarlas:
 * <ul>
 * <li>{@code PUT /v1/alertas/{referencia}}: registra una alerta; la misma referencia no produce un segundo envío.</li>
 * <li>{@code GET /v1/salud}: sondeo barato del cortacircuitos.</li>
 * <li>{@code GET /v1/acuses?desde=&limite=}: acuses desde una marca (consulta paginada).</li>
 * </ul>
 * Un fallo transitorio (tiempo agotado, red, 5xx, 408, 429) sale como {@link FalloTransitorioException}; cualquier otra
 * respuesta se devuelve con su código.
 */
public interface TransporteSe8 {

    /** Respuesta de SE-8 a una alerta. */
    record Respuesta(int estado, String cuerpo) {
    }

    /** Una página de acuses. */
    record PaginaAcuses(List<AcuseSe8> acuses, boolean hayMas) {
    }

    Respuesta enviar(AlertaSe8 alerta, Duration tiempoMaximo) throws FalloTransitorioException;

    void salud(Duration tiempoMaximo) throws FalloTransitorioException;

    PaginaAcuses acuses(long desde, int limite, Duration tiempoMaximo) throws FalloTransitorioException;
}
