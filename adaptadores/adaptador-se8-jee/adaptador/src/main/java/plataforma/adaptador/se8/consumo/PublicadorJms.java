package plataforma.adaptador.se8.consumo;

import jakarta.jms.BytesMessage;
import jakarta.jms.Connection;
import jakarta.jms.ConnectionFactory;
import jakarta.jms.DeliveryMode;
import jakarta.jms.JMSException;
import jakarta.jms.MessageProducer;
import jakarta.jms.Session;
import java.nio.charset.StandardCharsets;
import plataforma.adaptador.se8.contrato.Sobre;
import plataforma.adaptador.se8.nucleo.RelevoBandeja;

/**
 * Publica sobres en {@code plataforma.eventos} con Jakarta Messaging. La sesión es transaccional: {@code commit()} termina
 * cuando el bus aceptó el mensaje (tx.commit de AMQP), así que el relevo marca la fila solo después. Un mensaje sin ruta
 * no se pierde: el exchange de la plataforma tiene {@code plataforma.sin-ruta} como exchange alterno.
 *
 * <p>El cuerpo es el sobre JSON en UTF-8, sin nada más, como lo escriben los productores .NET. No es seguro para hilos:
 * lo usa solo el relevo, que es un singleton.</p>
 */
public final class PublicadorJms implements RelevoBandeja.PublicadorBus, AutoCloseable {

    private final ConnectionFactory fabrica;
    private Connection conexion;
    private Session sesion;

    public PublicadorJms(ConnectionFactory fabrica) {
        this.fabrica = fabrica;
    }

    @Override
    public void publicar(String claveRuteo, Sobre sobre) throws JMSException {
        try {
            Session s = sesion();
            MessageProducer productor = s.createProducer(ConexionesBus.exchange(claveRuteo));
            try {
                productor.setDeliveryMode(DeliveryMode.PERSISTENT);
                BytesMessage mensaje = s.createBytesMessage();
                mensaje.writeBytes(sobre.serializar().getBytes(StandardCharsets.UTF_8));
                mensaje.setJMSType(sobre.tipo());
                mensaje.setJMSCorrelationID(sobre.idCorrelacion());
                productor.send(mensaje);
                s.commit();
            } finally {
                productor.close();
            }
        } catch (JMSException | RuntimeException e) {
            close();
            throw e;
        }
    }

    private Session sesion() throws JMSException {
        if (sesion == null) {
            conexion = fabrica.createConnection();
            sesion = conexion.createSession(true, Session.SESSION_TRANSACTED);
        }
        return sesion;
    }

    @Override
    public void close() {
        try {
            if (conexion != null) {
                conexion.close();
            }
        } catch (JMSException ignorada) {
            // ya estaba cerrada
        }
        conexion = null;
        sesion = null;
    }
}
