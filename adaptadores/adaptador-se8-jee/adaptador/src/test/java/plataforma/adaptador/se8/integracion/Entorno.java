package plataforma.adaptador.se8.integracion;

import com.fasterxml.jackson.databind.JsonNode;
import com.rabbitmq.client.AMQP;
import com.rabbitmq.client.Channel;
import com.rabbitmq.client.Connection;
import com.rabbitmq.client.ConnectionFactory;
import com.rabbitmq.client.GetResponse;
import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.EntityTransaction;
import jakarta.persistence.PersistenceConfiguration;
import jakarta.persistence.PersistenceUnitTransactionType;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.Base64;
import java.util.function.BooleanSupplier;
import java.util.function.Function;
import org.hibernate.jpa.HibernatePersistenceProvider;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.containers.PostgreSQLContainer;
import org.testcontainers.containers.wait.strategy.Wait;
import org.testcontainers.utility.DockerImageName;
import org.testcontainers.utility.MountableFile;
import plataforma.adaptador.se8.contrato.CatalogoEventos;
import plataforma.adaptador.se8.contrato.Sobre;
import plataforma.adaptador.se8.migrador.CadenaConexion;
import plataforma.adaptador.se8.migrador.Migrador;
import plataforma.adaptador.se8.persistencia.Entrega;
import plataforma.adaptador.se8.persistencia.EstadoIntegracionFila;
import plataforma.adaptador.se8.persistencia.FilaBandeja;
import plataforma.adaptador.se8.persistencia.IntentoConsumo;
import plataforma.adaptador.se8.persistencia.MarcaAvance;
import plataforma.adaptador.se8.persistencia.MensajeProcesado;

/**
 * Entorno de las pruebas de integración, compartido por la clase: RabbitMQ 4.4 de un nodo con la topología real
 * ({@code definiciones.json}: cola quorum q.salida.se8, política con delivery-limit 5, DLX y el usuario adaptador-se8 de
 * mínimo privilegio) y PostgreSQL 18 con la base que crea el migrador (Flyway). El adaptador entra con su rol
 * {@code adaptador_se8_app}, no con el dueño.
 */
final class Entorno implements AutoCloseable {

    static final String CLAVE_APP = "adaptador_se8_prueba";

    @SuppressWarnings("resource")
    final GenericContainer<?> bus = new GenericContainer<>(DockerImageName.parse("rabbitmq:4.4.0-management"))
            .withCopyFileToContainer(MountableFile.forClasspathResource("bus/definiciones.json"), "/etc/rabbitmq/definiciones.json")
            .withCopyFileToContainer(MountableFile.forClasspathResource("bus/rabbitmq-un-nodo.conf"), "/etc/rabbitmq/rabbitmq.conf")
            .withExposedPorts(5672, 15672)
            .waitingFor(Wait.forLogMessage(".*Server startup complete.*", 1).withStartupTimeout(Duration.ofMinutes(2)));

    @SuppressWarnings("resource")
    final PostgreSQLContainer<?> db = new PostgreSQLContainer<>(
            DockerImageName.parse("postgis/postgis:18-3.6").asCompatibleSubstituteFor("postgres"))
            .withDatabaseName("postgres").withUsername("postgres").withPassword("postgres_local");

    EntityManagerFactory emf;
    private final HttpClient http = HttpClient.newHttpClient();

    void iniciar() throws Exception {
        bus.start();
        db.start();
        CadenaConexion dueno = new CadenaConexion(db.getHost(), db.getMappedPort(5432), "adaptador_se8", "postgres", "postgres_local");
        Migrador.ejecutar(dueno, CLAVE_APP);
        emf = new PersistenceConfiguration("pruebas")
                .provider(HibernatePersistenceProvider.class.getName())
                .transactionType(PersistenceUnitTransactionType.RESOURCE_LOCAL)
                .managedClass(Entrega.class).managedClass(MensajeProcesado.class).managedClass(IntentoConsumo.class)
                .managedClass(FilaBandeja.class).managedClass(MarcaAvance.class).managedClass(EstadoIntegracionFila.class)
                .property(PersistenceConfiguration.JDBC_URL, dueno.urlJdbc())
                .property(PersistenceConfiguration.JDBC_USER, Migrador.ROL_APP)
                .property(PersistenceConfiguration.JDBC_PASSWORD, CLAVE_APP)
                .property("hibernate.jdbc.time_zone", "UTC")
                .createEntityManagerFactory();
    }

    /** Vacía las tablas del esquema se8 entre pruebas (como dueño: el rol del adaptador no puede borrar). */
    void limpiar() throws Exception {
        try (java.sql.Connection c = java.sql.DriverManager.getConnection(
                "jdbc:postgresql://" + db.getHost() + ":" + db.getMappedPort(5432) + "/adaptador_se8", "postgres", "postgres_local");
                java.sql.Statement s = c.createStatement()) {
            s.execute("TRUNCATE se8.entregas, se8.mensajes_procesados, se8.intentos_consumo, se8.bandeja_salida, "
                    + "se8.contador_productor, se8.marcas_avance, se8.estado_integracion");
        }
    }

    String nodoBus() {
        return bus.getHost() + ":" + bus.getMappedPort(5672);
    }

    /** Una unidad de trabajo con transacción local; confirma al volver, revierte si lanza. */
    <T> T enTransaccion(Function<EntityManager, T> trabajo) {
        EntityManager em = emf.createEntityManager();
        EntityTransaction tx = em.getTransaction();
        try {
            tx.begin();
            T resultado = trabajo.apply(em);
            tx.commit();
            return resultado;
        } catch (RuntimeException e) {
            if (tx.isActive()) {
                tx.rollback();
            }
            throw e;
        } finally {
            em.close();
        }
    }

    long contar(String jpql) {
        return enTransaccion(em -> em.createQuery(jpql, Long.class).getSingleResult());
    }

    /** Publica como un productor .NET: el sobre JSON en el exchange de la plataforma, con su clave de ruteo. */
    void publicar(Sobre sobre) throws Exception {
        publicarCrudo(CatalogoEventos.porTipo(sobre.tipo()).claveRuteo(), sobre.serializar().getBytes(StandardCharsets.UTF_8));
    }

    void publicarCrudo(String claveRuteo, byte[] cuerpo) throws Exception {
        try (Connection c = admin().newConnection(); Channel canal = c.createChannel()) {
            canal.confirmSelect();
            canal.basicPublish(CatalogoEventos.EXCHANGE, claveRuteo, true,
                    new AMQP.BasicProperties.Builder().contentType("application/json").deliveryMode(2).build(), cuerpo);
            canal.waitForConfirmsOrDie(5000);
        }
    }

    /** Toma (y confirma) un mensaje de una cola, o nulo. */
    byte[] tomar(String cola) throws Exception {
        try (Connection c = admin().newConnection(); Channel canal = c.createChannel()) {
            GetResponse r = canal.basicGet(cola, true);
            return r == null ? null : r.getBody();
        }
    }

    void vaciar(String cola) throws Exception {
        try (Connection c = admin().newConnection(); Channel canal = c.createChannel()) {
            canal.queuePurge(cola);
        }
    }

    /** Estado de una cola en la API de administración (mensajes listos, sin confirmar, política efectiva). */
    JsonNode cola(String nombre) throws Exception {
        String url = "http://" + bus.getHost() + ":" + bus.getMappedPort(15672) + "/api/queues/%2F/"
                + URLEncoder.encode(nombre, StandardCharsets.UTF_8);
        HttpRequest pedido = HttpRequest.newBuilder(URI.create(url))
                .header("Authorization", "Basic " + Base64.getEncoder().encodeToString("admin:admin_local".getBytes(StandardCharsets.UTF_8)))
                .GET().build();
        HttpResponse<String> respuesta = http.send(pedido, HttpResponse.BodyHandlers.ofString());
        return Sobre.JSON.readTree(respuesta.body());
    }

    /** Mensajes en la cola (listos + sin confirmar), por declaración pasiva: no espera la estadística de la API. */
    long mensajes(String nombre) throws Exception {
        try (Connection c = admin().newConnection(); Channel canal = c.createChannel()) {
            return canal.queueDeclarePassive(nombre).getMessageCount();
        }
    }

    private ConnectionFactory admin() {
        ConnectionFactory f = new ConnectionFactory();
        f.setHost(bus.getHost());
        f.setPort(bus.getMappedPort(5672));
        f.setUsername("admin");
        f.setPassword("admin_local");
        return f;
    }

    static void esperar(String que, Duration maximo, BooleanSupplier condicion) throws InterruptedException {
        long limite = System.nanoTime() + maximo.toNanos();
        while (System.nanoTime() < limite) {
            if (condicion.getAsBoolean()) {
                return;
            }
            Thread.sleep(100);
        }
        throw new AssertionError("No se cumplió a tiempo: " + que);
    }

    @Override
    public void close() {
        if (emf != null) {
            emf.close();
        }
        db.stop();
        bus.stop();
    }
}
