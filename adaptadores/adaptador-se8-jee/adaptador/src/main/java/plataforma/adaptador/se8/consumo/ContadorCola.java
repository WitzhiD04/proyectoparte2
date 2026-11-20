package plataforma.adaptador.se8.consumo;

import com.rabbitmq.client.Address;
import com.rabbitmq.client.Channel;
import com.rabbitmq.client.ConnectionFactory;
import java.util.List;
import java.util.logging.Level;
import java.util.logging.Logger;

/**
 * Mensajes listos en {@code q.salida.se8}, para el campo {@code mensajesEnCola} de EV-23 (como {@code ContadorColaBus} de
 * comun). Usa una declaración pasiva con el cliente AMQP que trae el cliente JMS: Jakarta Messaging no da el largo de una
 * cola. Si el bus no responde devuelve 0; EV-23 sale igual.
 */
public final class ContadorCola {

    private static final Logger LOG = Logger.getLogger(ContadorCola.class.getName());

    private final List<String> nodos;
    private final String usuario;
    private final String clave;
    private final String virtualHost;
    private final String cola;

    public ContadorCola(List<String> nodos, String usuario, String clave, String virtualHost, String cola) {
        this.nodos = nodos;
        this.usuario = usuario;
        this.clave = clave;
        this.virtualHost = virtualHost;
        this.cola = cola;
    }

    public long contar() {
        ConnectionFactory fabrica = new ConnectionFactory();
        fabrica.setUsername(usuario);
        fabrica.setPassword(clave);
        fabrica.setVirtualHost(virtualHost);
        fabrica.setConnectionTimeout(3000);
        fabrica.setAutomaticRecoveryEnabled(false);
        List<Address> direcciones = nodos.stream().map(n -> {
            String[] partes = n.split(":");
            return new Address(partes[0], partes.length > 1 ? Integer.parseInt(partes[1]) : 5672);
        }).toList();
        try (com.rabbitmq.client.Connection conexion = fabrica.newConnection(direcciones, "adaptador-se8-contador");
                Channel canal = conexion.createChannel()) {
            return canal.queueDeclarePassive(cola).getMessageCount();
        } catch (Exception e) {
            LOG.log(Level.WARNING, "No se pudo contar " + cola + "; EV-23 informa 0.", e);
            return 0;
        }
    }
}
