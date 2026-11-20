package plataforma.adaptador.se8.persistencia;

import java.time.Instant;
import java.util.List;
import java.util.Optional;
import plataforma.adaptador.se8.contrato.Sobre;

/**
 * Lo que el adaptador guarda en su base {@code adaptador_se8}. Cada método corre dentro de la transacción de quien lo
 * llama: en el servidor, la transacción JTA del EJB; en las pruebas de integración, una transacción local.
 */
public interface AlmacenSe8 {

    boolean yaProcesado(String claveIdempotencia);

    /** Registra el evento como aplicado. La llave primaria impide registrarlo dos veces. */
    void registrarProcesado(Sobre sobre, Instant ahora);

    Optional<Entrega> buscarEntrega(String claveEnvio);

    void guardar(Entrega entrega);

    /** Valida el sobre contra el contrato y lo deja en la bandeja de salida, en la transacción en curso (AD-45). */
    void agregarABandeja(Sobre sobre);

    /** Suma un fallo de aplicación al mensaje y devuelve el total. */
    int registrarFallo(String claveIdempotencia, String error, Instant ahora);

    /**
     * Entregas del modo sustituto que esperan acuse desde antes de {@code antesDe} (ENVIADA) o esperan reenvío
     * (PENDIENTE), bloqueadas para esta transacción y sin las que otra réplica ya tomó.
     */
    List<Entrega> entregasSinAcuse(Instant antesDe, int limite);

    /** La marca de avance del flujo, bloqueada para esta transacción (dos réplicas no concilian a la vez). */
    MarcaAvance marca(String flujo, Instant ahora);

    /** El estado de la integración del sistema, bloqueado para esta transacción. */
    EstadoIntegracionFila estadoIntegracion(String sistema);
}
