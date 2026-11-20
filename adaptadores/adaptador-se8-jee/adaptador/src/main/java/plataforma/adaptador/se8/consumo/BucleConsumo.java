package plataforma.adaptador.se8.consumo;

import com.fasterxml.jackson.databind.JsonNode;
import jakarta.jms.BytesMessage;
import jakarta.jms.Connection;
import jakarta.jms.ConnectionFactory;
import jakarta.jms.Destination;
import jakarta.jms.JMSException;
import jakarta.jms.Message;
import jakarta.jms.MessageConsumer;
import jakarta.jms.Session;
import jakarta.jms.TextMessage;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.concurrent.atomic.AtomicLong;
import java.util.logging.Level;
import java.util.logging.Logger;
import plataforma.adaptador.se8.contrato.EventoInvalidoException;
import plataforma.adaptador.se8.contrato.SistemaExternoNoDisponibleException;
import plataforma.adaptador.se8.contrato.Sobre;
import plataforma.adaptador.se8.contrato.ValidadorContrato;

/**
 * Consumidor de {@code q.salida.se8} con Jakarta Messaging. Hace lo mismo que {@code ConsumidorIdempotente} y
 * {@code CompuertaConsumo} de comun:
 * <ol>
 * <li>Toma un mensaje a la vez ({@code receive}: los demás quedan listos en la cola, donde KEDA los cuenta).</li>
 * <li>Valida el sobre contra {@code sobre.v1.json} y el {@code EV-nn.v1.json} del tipo. Si no cumple el contrato,
 *     {@code rollback()}: con {@code nackOnRollback} el mensaje se rechaza sin reencolar y va a {@code dlq.salida.se8}.</li>
 * <li>Lo aplica con el procesador, que abre y confirma su propia transacción JPA. <b>Solo después</b> confirma la sesión
 *     JMS ({@code commit()} = ack). Si la transacción JPA no confirma, el mensaje no se confirma.</li>
 * <li>Un error transitorio suma un fallo en la base ({@code intentos_consumo}); al quinto, {@code rollback()} lo aparta a
 *     la DLQ. Antes de eso, cerrar la sesión lo reencola. Si la base no responde, cuenta {@code JMSXDeliveryCount}.</li>
 * <li>Si SE-8 no responde no cuenta como fallo del mensaje. Con el circuito cerrado se reintenta en un momento; cuando
 *     el circuito se abre, la sesión se cierra (el mensaje vuelve a la cola una vez por caída) y el consumo se detiene.
 *     Mientras está abierto se sondea SE-8; al cerrarse, el consumo sigue.</li>
 * </ol>
 * Es Java simple: el EJB {@code ConsumidorSe8} lo corre en un hilo del {@code ManagedExecutorService}, y la prueba de
 * integración lo corre contra RabbitMQ y PostgreSQL en contenedores.
 */
public final class BucleConsumo implements Runnable {

    /** Aplica el evento en su propia transacción y la confirma antes de volver. */
    @FunctionalInterface
    public interface Procesador {
        void procesar(Sobre sobre) throws Exception;
    }

    /** Suma un fallo de aplicación del mensaje (en su propia transacción) y devuelve el total. */
    @FunctionalInterface
    public interface ContadorFallos {
        int registrarFallo(String claveIdempotencia, String error) throws Exception;
    }

    /** Estado del cortacircuitos de SE-8, visto por el consumidor. */
    public interface Compuerta {
        boolean abierta();

        /** Llamada de prueba a SE-8. Con la apertura vencida, si responde, cierra el circuito. */
        void sondear();
    }

    /** Parámetros del bucle. */
    public record Opciones(String cola, int limiteEntregas, Duration esperaReintento, Duration intervaloSondeo,
            Duration esperaRecepcion) {
    }

    private static final Logger LOG = Logger.getLogger(BucleConsumo.class.getName());

    private final ConnectionFactory fabrica;
    private final Destination destino;
    private final Procesador procesador;
    private final ContadorFallos contador;
    private final Compuerta compuerta;
    private final Opciones opciones;
    private final ValidadorContrato validador = ValidadorContrato.instancia();

    private volatile boolean activo = true;
    private volatile boolean consumiendo;
    private Connection conexion;
    private Session sesion;
    private MessageConsumer consumidor;

    private final AtomicLong confirmados = new AtomicLong();
    private final AtomicLong apartados = new AtomicLong();
    private final AtomicLong reencolados = new AtomicLong();

    public BucleConsumo(ConnectionFactory fabrica, Destination destino, Procesador procesador, ContadorFallos contador,
            Compuerta compuerta, Opciones opciones) {
        this.fabrica = fabrica;
        this.destino = destino;
        this.procesador = procesador;
        this.contador = contador;
        this.compuerta = compuerta;
        this.opciones = opciones;
    }

    @Override
    public void run() {
        LOG.info(() -> "Consumidor de " + opciones.cola() + " iniciado.");
        while (activo) {
            try {
                ciclo();
            } catch (JMSException | RuntimeException e) {
                if (activo) {
                    LOG.log(Level.WARNING, "El consumidor de " + opciones.cola() + " perdió la conexión con el bus; se reconecta.", e);
                }
                cerrarTodo();
                dormir(Duration.ofSeconds(3));
            }
        }
        cerrarTodo();
        LOG.info(() -> "Consumidor de " + opciones.cola() + " detenido.");
    }

    public void detener() {
        activo = false;
    }

    public boolean activo() {
        return activo;
    }

    /** El consumidor tiene una sesión abierta sobre la cola (circuito cerrado). */
    public boolean consumiendo() {
        return consumiendo;
    }

    public long confirmados() {
        return confirmados.get();
    }

    public long apartados() {
        return apartados.get();
    }

    public long reencolados() {
        return reencolados.get();
    }

    private void ciclo() throws JMSException {
        if (compuerta.abierta()) {
            if (sesion != null) {
                cerrarSesion();
                LOG.warning("SE-8 no responde: se pausa el consumo; los mensajes esperan en " + opciones.cola() + ".");
            }
            compuerta.sondear();
            if (compuerta.abierta()) {
                dormir(opciones.intervaloSondeo());
            } else {
                LOG.info("SE-8 responde: se consume " + opciones.cola() + " otra vez.");
            }
            return;
        }
        abrirSesion();
        Message mensaje = consumidor.receive(opciones.esperaRecepcion().toMillis());
        if (mensaje != null) {
            atender(mensaje);
        }
    }

    private void atender(Message mensaje) throws JMSException {
        Sobre sobre;
        try {
            JsonNode arbol = Sobre.arbol(cuerpo(mensaje));
            validador.validar(arbol);
            sobre = Sobre.deJson(arbol);
        } catch (EventoInvalidoException e) {
            LOG.log(Level.SEVERE, "Mensaje " + mensaje.getJMSMessageID() + " de " + opciones.cola()
                    + " no cumple el contrato; se aparta a la cola de mensajes muertos: " + e.getMessage());
            apartar();
            return;
        }

        while (activo) {
            try {
                procesador.procesar(sobre); // confirma su transacción JPA antes de volver
                sesion.commit();            // ack solo después del commit JPA
                confirmados.incrementAndGet();
                return;
            } catch (SistemaExternoNoDisponibleException e) {
                if (compuerta.abierta()) {
                    // El mensaje vuelve a la cola sin contar como fallo; la compuerta detiene el consumo.
                    cerrarSesion();
                    reencolados.incrementAndGet();
                    return;
                }
                LOG.warning(() -> "SE-8 no responde (" + e.getMessage() + "); " + sobre.tipo() + " "
                        + sobre.claveIdempotencia() + " sigue pendiente.");
                dormir(opciones.esperaReintento());
            } catch (EventoInvalidoException e) {
                LOG.log(Level.SEVERE, "Evento " + sobre.tipo() + " " + sobre.idEvento()
                        + " rechazado por el adaptador; se aparta a la cola de mensajes muertos.", e);
                apartar();
                return;
            } catch (JMSException e) {
                throw e; // el commit JMS falló después del commit JPA: el mensaje vuelve y se descarta por idempotente
            } catch (Exception e) {
                int intento = contarFallo(sobre, e, mensaje);
                if (intento >= opciones.limiteEntregas()) {
                    LOG.log(Level.SEVERE, "El evento " + sobre.tipo() + " " + sobre.idEvento() + " falló " + intento
                            + " veces; se aparta a la cola de mensajes muertos.", e);
                    apartar();
                    return;
                }
                LOG.log(Level.WARNING, "No se pudo aplicar " + sobre.tipo() + " " + sobre.idEvento() + " (intento "
                        + intento + " de " + opciones.limiteEntregas() + "); se reencola.", e);
                cerrarSesion(); // cerrar la sesión transaccional reencola el mensaje (no hace nack)
                reencolados.incrementAndGet();
                dormir(Duration.ofSeconds(1));
                return;
            }
        }
        cerrarSesion();
    }

    private int contarFallo(Sobre sobre, Exception error, Message mensaje) throws JMSException {
        try {
            return contador.registrarFallo(sobre.claveIdempotencia(), String.valueOf(error));
        } catch (Exception sinBase) {
            // Sin base no hay contador: se usa el conteo del broker (x-acquired-count o x-delivery-count).
            LOG.log(Level.WARNING, "No se pudo registrar el fallo en la base; se usa JMSXDeliveryCount.", sinBase);
            return mensaje.propertyExists("JMSXDeliveryCount") ? mensaje.getIntProperty("JMSXDeliveryCount") : 1;
        }
    }

    /** Rechaza el mensaje sin reencolar: el exchange de mensajes muertos lo lleva a dlq.salida.se8. */
    private void apartar() throws JMSException {
        sesion.rollback();
        apartados.incrementAndGet();
    }

    private void abrirSesion() throws JMSException {
        if (conexion == null) {
            conexion = fabrica.createConnection();
            conexion.start();
        }
        if (sesion == null) {
            sesion = conexion.createSession(true, Session.SESSION_TRANSACTED);
            consumidor = sesion.createConsumer(destino);
            consumiendo = true;
        }
    }

    private void cerrarSesion() {
        consumiendo = false;
        try {
            if (sesion != null) {
                sesion.close(); // sin nack: lo no confirmado vuelve a la cola
            }
        } catch (JMSException e) {
            LOG.log(Level.FINE, "La sesión ya estaba cerrada.", e);
        }
        sesion = null;
        consumidor = null;
    }

    private void cerrarTodo() {
        cerrarSesion();
        try {
            if (conexion != null) {
                conexion.close();
            }
        } catch (JMSException e) {
            LOG.log(Level.FINE, "La conexión ya estaba cerrada.", e);
        }
        conexion = null;
    }

    private static byte[] cuerpo(Message mensaje) throws JMSException {
        if (mensaje instanceof BytesMessage bytes) {
            byte[] cuerpo = new byte[(int) bytes.getBodyLength()];
            bytes.readBytes(cuerpo);
            return cuerpo;
        }
        if (mensaje instanceof TextMessage texto) {
            return texto.getText() == null ? new byte[0] : texto.getText().getBytes(StandardCharsets.UTF_8);
        }
        return new byte[0];
    }

    private void dormir(Duration espera) {
        try {
            Thread.sleep(Math.max(1, espera.toMillis()));
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            activo = false;
        }
    }
}
