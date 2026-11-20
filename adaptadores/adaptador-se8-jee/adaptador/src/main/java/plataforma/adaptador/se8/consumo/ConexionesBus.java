package plataforma.adaptador.se8.consumo;

import com.rabbitmq.jms.admin.RMQConnectionFactory;
import com.rabbitmq.jms.admin.RMQDestination;
import jakarta.jms.JMSException;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import plataforma.adaptador.se8.contrato.CatalogoEventos;

/**
 * Fábricas de Jakarta Messaging sobre RabbitMQ con el cliente JMS de RabbitMQ en modo de interoperabilidad AMQP. Los
 * destinos AMQP apuntan a la cola y al exchange que ya declara {@code definiciones.json}: el cliente no declara nada (el
 * usuario {@code adaptador-se8} no tiene permiso de configuración, igual que los adaptadores .NET).
 */
public final class ConexionesBus {

    private ConexionesBus() {
    }

    /**
     * Fábrica de conexiones con los nodos del clúster (AD-02: 3 nodos; el cliente prueba cada uno). {@code nackOnRollback}:
     * en una sesión transaccional, {@code rollback()} rechaza el mensaje sin reencolarlo, y el exchange de mensajes
     * muertos de la cola lo lleva a {@code dlq.salida.se8}. Cerrar la sesión, en cambio, lo reencola.
     */
    public static RMQConnectionFactory fabrica(List<String> nodos, String usuario, String clave, String virtualHost)
            throws JMSException {
        if (nodos == null || nodos.isEmpty()) {
            throw new IllegalArgumentException("Falta Bus__Nodos__0.");
        }
        String vhost = URLEncoder.encode(virtualHost == null ? "/" : virtualHost, StandardCharsets.UTF_8);
        String credenciales = URLEncoder.encode(usuario, StandardCharsets.UTF_8) + ":"
                + URLEncoder.encode(clave, StandardCharsets.UTF_8);
        List<String> uris = new ArrayList<>();
        for (String nodo : nodos) {
            String hostPuerto = nodo.contains(":") ? nodo : nodo + ":5672";
            uris.add("amqp://" + credenciales + "@" + hostPuerto + "/" + vhost);
        }
        RMQConnectionFactory fabrica = new RMQConnectionFactory();
        fabrica.setUris(uris);
        fabrica.setNackOnRollback(true);
        fabrica.setDeclareReplyToDestination(false);
        fabrica.setAmqpConnectionFactoryPostProcessor(f -> {
            f.setAutomaticRecoveryEnabled(false); // la recuperación la hace el bucle: cierra y vuelve a abrir
            f.setRequestedHeartbeat(30);
            f.setConnectionTimeout(5000);
        });
        return fabrica;
    }

    /** Destino de lectura: la cola existente, sin declararla. */
    public static RMQDestination cola(String nombre) {
        return new RMQDestination(nombre, null, null, nombre);
    }

    /** Destino de escritura: el exchange de la plataforma con la clave de ruteo del evento. */
    public static RMQDestination exchange(String claveRuteo) {
        return new RMQDestination(claveRuteo, CatalogoEventos.EXCHANGE, claveRuteo, null);
    }
}
