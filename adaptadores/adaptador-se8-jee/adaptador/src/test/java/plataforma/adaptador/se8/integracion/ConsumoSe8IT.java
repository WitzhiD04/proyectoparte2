package plataforma.adaptador.se8.integracion;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.fasterxml.jackson.databind.JsonNode;
import java.nio.charset.StandardCharsets;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.TestInstance;
import org.testcontainers.DockerClientFactory;
import plataforma.adaptador.se8.Muestras;
import plataforma.adaptador.se8.cliente.ClienteSe8;
import plataforma.adaptador.se8.cliente.TransporteSe8;
import plataforma.adaptador.se8.configuracion.Configuracion;
import plataforma.adaptador.se8.consumo.BucleConsumo;
import plataforma.adaptador.se8.consumo.ConexionesBus;
import plataforma.adaptador.se8.consumo.PublicadorJms;
import plataforma.adaptador.se8.contrato.SistemaExternoNoDisponibleException;
import plataforma.adaptador.se8.contrato.Sobre;
import plataforma.adaptador.se8.contrato.ValidadorContrato;
import plataforma.adaptador.se8.jee.AdaptadorSe8;
import plataforma.adaptador.se8.nucleo.AplicadorAcuses;
import plataforma.adaptador.se8.nucleo.EstadoIntegracionSe8;
import plataforma.adaptador.se8.nucleo.ProcesadorSe8;
import plataforma.adaptador.se8.nucleo.RelevoBandeja;
import plataforma.adaptador.se8.persistencia.AlmacenJpa;
import plataforma.adaptador.se8.persistencia.Entrega;
import plataforma.adaptador.se8.resiliencia.Cortacircuitos;
import plataforma.adaptador.se8.resiliencia.EjecutorResiliente;
import plataforma.adaptador.se8.resiliencia.FalloTransitorioException;
import plataforma.adaptador.se8.resiliencia.OpcionesResiliencia;
import plataforma.adaptador.se8.traduccion.AcuseSe8;
import plataforma.adaptador.se8.traduccion.AlertaSe8;
import plataforma.adaptador.se8.traduccion.OpcionesSustituto;

/**
 * Consumo de q.salida.se8 de punta a punta, fuera del servidor pero con las piezas reales: el bucle Jakarta Messaging con
 * el cliente JMS de RabbitMQ, la cola quorum con su política (delivery-limit 5, DLX), el usuario adaptador-se8 de mínimo
 * privilegio, Jakarta Persistence (Hibernate) con el rol adaptador_se8_app sobre la base que crea el migrador, y el relevo
 * de la bandeja. Solo SE-8 es un doble en memoria.
 */
@TestInstance(TestInstance.Lifecycle.PER_CLASS)
class ConsumoSe8IT {

    private static final String COLA = "q.salida.se8";
    private static final String DLQ = "dlq.salida.se8";
    /** El Cuadro 10, con la apertura acortada a 2 s para que la prueba no espere 15 s. */
    private static final OpcionesResiliencia RESILIENCIA = new OpcionesResiliencia(3, 20, 2, 0.1, 3, 1.0, 30, 2);

    private final Entorno entorno = new Entorno();
    private final Se8EnMemoria se8 = new Se8EnMemoria();
    private final Clock reloj = Clock.systemUTC();

    private Cortacircuitos circuito;
    private ClienteSe8 cliente;
    private AdaptadorSe8 adaptador;
    private BucleConsumo bucle;
    private Thread hilo;

    /** Inyección de fallos en la unidad de trabajo del mensaje. */
    private final AtomicReference<RuntimeException> falloAntesDelCommit = new AtomicReference<>();
    private final AtomicInteger fallosPendientes = new AtomicInteger();
    private final AtomicBoolean contadorSinBase = new AtomicBoolean();
    private final AtomicReference<Runnable> durantePrcesamiento = new AtomicReference<>();
    private final List<Integer> fallosVistos = Collections.synchronizedList(new ArrayList<>());

    @BeforeAll
    void iniciarEntorno() throws Exception {
        org.junit.jupiter.api.Assumptions.assumeTrue(DockerClientFactory.instance().isDockerAvailable(),
                "Sin Docker: se omiten las pruebas de integración.");
        entorno.iniciar();
    }

    @AfterAll
    void cerrarEntorno() {
        entorno.close();
    }

    @BeforeEach
    void iniciarConsumo() throws Exception {
        entorno.vaciar(COLA);
        entorno.vaciar(DLQ);
        entorno.vaciar("q.consulta");
        entorno.limpiar();
        se8.reiniciar();
        falloAntesDelCommit.set(null);
        fallosPendientes.set(0);
        contadorSinBase.set(false);
        durantePrcesamiento.set(null);
        fallosVistos.clear();

        circuito = new Cortacircuitos(RESILIENCIA, reloj);
        circuito.agregarObservador(new Cortacircuitos.Observador() {
            @Override
            public void alAbrirse(Instant instante) {
                anunciar("ABIERTO", instante, instante);
            }

            @Override
            public void alCerrarse(Instant instante, Instant abiertoDesde) {
                anunciar("CERRADO", instante, abiertoDesde);
            }
        });
        cliente = new ClienteSe8(se8, EjecutorResiliente.sistema(RESILIENCIA, circuito));
        Configuracion configuracion = new Configuracion(n -> Optional.empty());
        adaptador = new AdaptadorSe8(cliente, OpcionesSustituto.POR_OMISION, configuracion, reloj);

        BucleConsumo.Compuerta compuerta = new BucleConsumo.Compuerta() {
            @Override
            public boolean abierta() {
                return circuito.abierto();
            }

            @Override
            public void sondear() {
                try {
                    cliente.sondear();
                } catch (SistemaExternoNoDisponibleException e) {
                    // sigue caído
                }
            }
        };
        BucleConsumo.Procesador procesador = sobre -> entorno.enTransaccion(em -> {
            ProcesadorSe8.Resultado r = new ProcesadorSe8(new AlmacenJpa(em), adaptador, OpcionesSustituto.POR_OMISION, reloj)
                    .procesar(sobre);
            Runnable durante = durantePrcesamiento.get();
            if (durante != null) {
                durante.run();
            }
            if (fallosPendientes.getAndUpdate(n -> Math.max(0, n - 1)) > 0) {
                throw new IllegalStateException("fallo transitorio simulado antes del commit JPA");
            }
            RuntimeException fallo = falloAntesDelCommit.getAndSet(null);
            if (fallo != null) {
                throw fallo;
            }
            return r;
        });
        BucleConsumo.ContadorFallos contador = (clave, error) -> {
            if (contadorSinBase.get()) {
                throw new IllegalStateException("base caída simulada");
            }
            int n = entorno.enTransaccion(em -> new AlmacenJpa(em).registrarFallo(clave, error, reloj.instant()));
            fallosVistos.add(n);
            return n;
        };
        bucle = new BucleConsumo(
                ConexionesBus.fabrica(List.of(entorno.nodoBus()), "adaptador-se8", "adaptador_se8_local", "/"),
                ConexionesBus.cola(COLA), procesador, contador, compuerta,
                new BucleConsumo.Opciones(COLA, 5, Duration.ofMillis(200), Duration.ofMillis(300), Duration.ofMillis(200)));
        hilo = new Thread(bucle, "consumo-se8-prueba");
        hilo.start();
    }

    @AfterEach
    void detenerConsumo() throws Exception {
        bucle.detener();
        hilo.join(10_000);
    }

    private void anunciar(String estado, Instant instante, Instant abiertoDesde) {
        long enCola;
        try {
            enCola = entorno.mensajes(COLA);
        } catch (Exception e) {
            enCola = 0;
        }
        long n = enCola;
        entorno.enTransaccion(em -> new EstadoIntegracionSe8(new AlmacenJpa(em))
                .registrar(UUID.randomUUID(), estado, instante, abiertoDesde, n));
    }

    // ------------------------------------------------------------------ pruebas

    @Test
    void unaAlertaQuedaEnviadaYSoloElAcuseLaEntrega() throws Exception {
        Sobre ev03 = Muestras.ev03(UUID.randomUUID(), "NIVEL_2");
        entorno.publicar(ev03); // por la clave de ruteo ev03.desastre.creado: prueba también el enlace sustituto

        String referencia = "sustituto:" + ev03.claveIdempotencia();
        Entorno.esperar("la alerta llega a SE-8", Duration.ofSeconds(20), () -> se8.recibidas.containsKey(referencia));
        Entorno.esperar("la cola queda en 0", Duration.ofSeconds(10), () -> mensajes(COLA) == 0);
        Entrega antes = entrega(referencia);
        assertEquals(Entrega.Estado.ENVIADA, antes.getEstado(), "sin acuse no es entregada (QA-08)");

        AplicadorAcuses.Resultado r = entorno.enTransaccion(em -> new AplicadorAcuses(new AlmacenJpa(em), adaptador, reloj)
                .aplicar(new AcuseSe8(referencia, 1, "ENTREGADA", null, Instant.now().toString())));
        assertEquals(AplicadorAcuses.Resultado.APLICADO, r);
        assertEquals(Entrega.Estado.ENTREGADA, entrega(referencia).getEstado());
        assertEquals(0, entorno.mensajes(DLQ));
    }

    @Test
    void elAckLlegaSoloDespuesDelCommitJpa() throws Exception {
        Sobre ev04 = Muestras.ev04(UUID.randomUUID(), "NIVEL_1", "NIVEL_3");
        AtomicReference<JsonNode> colaDuranteLaTransaccion = new AtomicReference<>();
        // Primera entrega: se mira la cola dentro de la transacción y luego el commit JPA falla.
        durantePrcesamiento.set(() -> {
            if (colaDuranteLaTransaccion.get() == null) {
                try {
                    Thread.sleep(6000); // la API de administración actualiza sus estadísticas cada 5 s
                    colaDuranteLaTransaccion.set(entorno.cola(COLA));
                } catch (Exception e) {
                    throw new IllegalStateException(e);
                }
            }
        });
        falloAntesDelCommit.set(new IllegalStateException("el commit JPA falló (simulado)"));
        entorno.publicar(ev04);

        String referencia = "sustituto:" + ev04.claveIdempotencia();
        Entorno.esperar("se procesa después del reintento", Duration.ofSeconds(40),
                () -> existeEntrega(referencia) && mensajes(COLA) == 0);

        JsonNode durante = colaDuranteLaTransaccion.get();
        assertNotNull(durante);
        assertEquals(1, durante.path("messages_unacknowledged").asInt(),
                "mientras la transacción JPA no confirma, el mensaje está tomado pero sin ack: " + durante);
        assertEquals(List.of(1), fallosVistos, "el commit fallido contó un fallo y el mensaje volvió a la cola");
        assertEquals(1, entorno.contar("SELECT count(m) FROM MensajeProcesado m"));
        assertEquals(1, entorno.contar("SELECT count(e) FROM Entrega e"));
        assertEquals(1, se8.recibidas.size(), "SE-8 recibió dos veces la misma referencia y la registró una vez");
        assertEquals(2, se8.recepciones.get());
        assertEquals(0, entorno.mensajes(DLQ));
    }

    @Test
    void cincoEntregasFallidasLlevanElMensajeALaDlq() throws Exception {
        JsonNode cola = entorno.cola(COLA);
        assertEquals("quorum", cola.path("type").asText());
        assertEquals(5, cola.path("effective_policy_definition").path("delivery-limit").asInt(), "política de la topología");
        assertEquals("plataforma.dlx", cola.path("arguments").path("x-dead-letter-exchange").asText());

        fallosPendientes.set(100); // nunca confirma
        Sobre ev03 = Muestras.ev03(UUID.randomUUID(), "NIVEL_1");
        entorno.publicar(ev03);

        Entorno.esperar("el mensaje llega a la DLQ", Duration.ofSeconds(40), () -> mensajes(DLQ) == 1);
        assertEquals(List.of(1, 2, 3, 4, 5), fallosVistos);
        assertEquals(0, entorno.mensajes(COLA));
        assertEquals(0, entorno.contar("SELECT count(m) FROM MensajeProcesado m"));
        int fallos = entorno.enTransaccion(em -> em.createQuery(
                "SELECT i.fallos FROM IntentoConsumo i WHERE i.claveIdempotencia = :c", Integer.class)
                .setParameter("c", ev03.claveIdempotencia()).getSingleResult());
        assertEquals(5, fallos, "intentos_consumo");
        byte[] muerto = entorno.tomar(DLQ);
        assertEquals(ev03.idEvento(), Sobre.leer(muerto).idEvento(), "el mismo mensaje, intacto");
    }

    @Test
    void sinBaseCuentaLasEntregasDelBrokerYTambienApartaAlQuinto() throws Exception {
        contadorSinBase.set(true);
        fallosPendientes.set(100);
        entorno.publicar(Muestras.ev03(UUID.randomUUID(), "NIVEL_1"));
        Entorno.esperar("el mensaje llega a la DLQ por JMSXDeliveryCount", Duration.ofSeconds(40), () -> mensajes(DLQ) == 1);
        assertEquals(0, entorno.mensajes(COLA));
    }

    @Test
    void unMensajeQueNoCumpleElContratoVaDirectoALaDlq() throws Exception {
        entorno.publicarCrudo("ev03.desastre.creado", "{\"tipo\":\"EV-03\",\"nombre\":\"desastre.creado\"}"
                .getBytes(StandardCharsets.UTF_8));
        Entorno.esperar("apartado", Duration.ofSeconds(20), () -> mensajes(DLQ) == 1);
        assertTrue(fallosVistos.isEmpty(), "un error permanente no se reintenta");
        assertEquals(0, se8.recepciones.get());
    }

    @Test
    void unMensajeDuplicadoNoGeneraUnSegundoEnvio() throws Exception {
        Sobre ev03 = Muestras.ev03(UUID.randomUUID(), "NIVEL_3");
        entorno.publicar(ev03);
        entorno.publicar(ev03);
        Entorno.esperar("los dos se consumen", Duration.ofSeconds(20), () -> mensajes(COLA) == 0 && bucle.confirmados() == 2);
        assertEquals(1, se8.recepciones.get(), "el segundo se descartó por su clave de idempotencia");
        assertEquals(1, entorno.contar("SELECT count(e) FROM Entrega e"));
    }

    @Test
    void conSe8CaidoElCircuitoSeAbrePublicaEv23YNoSePierdeNada() throws Exception {
        se8.caido = true;
        List<Sobre> eventos = new ArrayList<>();
        for (int i = 0; i < 5; i++) {
            Sobre s = Muestras.ev03(UUID.randomUUID(), "NIVEL_2");
            eventos.add(s);
            entorno.publicar(s);
        }
        Entorno.esperar("el circuito se abre", Duration.ofSeconds(30), () -> circuito.abierto());
        Entorno.esperar("el consumo se pausa", Duration.ofSeconds(10), () -> !bucle.consumiendo());
        Thread.sleep(1500);
        assertEquals(5, entorno.mensajes(COLA), "los 5 esperan en la cola");
        assertEquals(0, entorno.mensajes(DLQ), "una caída no manda nada a la DLQ");

        // EV-23 sale por la bandeja con el relevo y llega a q.consulta con la secuencia del productor.
        relevar();
        byte[] cuerpo = tomarEv23();
        Sobre ev23 = Sobre.leer(cuerpo);
        ValidadorContrato.instancia().validar(Sobre.arbol(cuerpo));
        assertEquals("EV-23", ev23.tipo());
        assertEquals("adaptador-se8", ev23.productor());
        assertEquals("ABIERTO", ev23.datos().get("estadoNuevo").asText());
        assertEquals("SE-8", ev23.datos().get("sistemaExterno").asText());
        assertTrue(ev23.datos().get("mensajesEnCola").asInt() >= 4, ev23.datos().toString());
        assertNotNull(ev23.secuenciaProductor());

        // Vuelve SE-8: la cola queda en 0 y cada alerta llega una sola vez.
        se8.caido = false;
        Entorno.esperar("la cola se drena", Duration.ofSeconds(30), () -> mensajes(COLA) == 0 && bucle.confirmados() >= 5);
        assertEquals(5, se8.recibidas.size());
        for (Sobre s : eventos) {
            assertEquals(1, se8.recibidas.get("sustituto:" + s.claveIdempotencia()).get(), "una sola vez");
        }
        assertEquals(5, entorno.contar("SELECT count(e) FROM Entrega e WHERE e.estado = plataforma.adaptador.se8.persistencia.Entrega.Estado.ENVIADA"));
        relevar();
        Sobre cierre = Sobre.leer(tomarEv23());
        assertEquals("CERRADO", cierre.datos().get("estadoNuevo").asText());
        assertEquals(ev23.secuenciaProductor() + 1, cierre.secuenciaProductor(), "secuencia sin huecos (AD-45)");
        assertEquals(0, entorno.mensajes(DLQ));
    }

    // ------------------------------------------------------------------ apoyo

    private void relevar() throws Exception {
        try (PublicadorJms publicador = new PublicadorJms(
                ConexionesBus.fabrica(List.of(entorno.nodoBus()), "adaptador-se8", "adaptador_se8_local", "/"))) {
            entorno.enTransaccion(em -> new RelevoBandeja(em, "adaptador-se8", 50, reloj).numerar());
            RelevoBandeja.Resultado r = entorno.enTransaccion(em -> new RelevoBandeja(em, "adaptador-se8", 50, reloj)
                    .publicar(publicador));
            assertTrue(!r.rechazado(), "el bus aceptó la publicación: " + r.error());
        }
        Thread.sleep(300);
    }

    /** q.consulta recibe también EV-03 (enlace de Consulta): se toma el primer EV-23. */
    private byte[] tomarEv23() throws Exception {
        for (int i = 0; i < 50; i++) {
            byte[] cuerpo = entorno.tomar("q.consulta");
            assertNotNull(cuerpo, "EV-23 publicado");
            if ("EV-23".equals(Sobre.leer(cuerpo).tipo())) {
                return cuerpo;
            }
        }
        throw new AssertionError("No llegó EV-23 a q.consulta");
    }

    private long mensajes(String cola) {
        try {
            return entorno.mensajes(cola);
        } catch (Exception e) {
            return -1;
        }
    }

    private boolean existeEntrega(String referencia) {
        return entorno.enTransaccion(em -> new AlmacenJpa(em).buscarEntrega(referencia).isPresent());
    }

    private Entrega entrega(String referencia) {
        return entorno.enTransaccion(em -> new AlmacenJpa(em).buscarEntrega(referencia).orElseThrow());
    }

    /** SE-8 en memoria: idempotente por referencia; cuenta recepciones; puede estar caído. */
    static final class Se8EnMemoria implements TransporteSe8 {
        final Map<String, AtomicInteger> recibidas = new ConcurrentHashMap<>();
        final AtomicInteger recepciones = new AtomicInteger();
        volatile boolean caido;

        void reiniciar() {
            recibidas.clear();
            recepciones.set(0);
            caido = false;
        }

        @Override
        public Respuesta enviar(AlertaSe8 alerta, Duration t) throws FalloTransitorioException {
            if (caido) {
                throw new FalloTransitorioException("503 simulado");
            }
            recepciones.incrementAndGet();
            boolean nueva = recibidas.putIfAbsent(alerta.referenciaExterna(), new AtomicInteger(1)) == null;
            return new Respuesta(nueva ? 202 : 200, "{}");
        }

        @Override
        public void salud(Duration t) throws FalloTransitorioException {
            if (caido) {
                throw new FalloTransitorioException("503 simulado");
            }
        }

        @Override
        public PaginaAcuses acuses(long desde, int limite, Duration t) {
            return new PaginaAcuses(List.of(), false);
        }
    }
}
