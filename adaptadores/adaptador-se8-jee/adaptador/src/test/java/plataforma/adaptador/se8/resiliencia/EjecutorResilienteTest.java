package plataforma.adaptador.se8.resiliencia;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import plataforma.adaptador.se8.Muestras;
import plataforma.adaptador.se8.RelojManual;
import plataforma.adaptador.se8.contrato.SistemaExternoNoDisponibleException;

/** Tiempos y reintentos del Cuadro 10: 3 s por intento, 20 s por operación, 2 reintentos desde 0,5 s. */
class EjecutorResilienteTest {

    private RelojManual reloj;
    private Cortacircuitos circuito;
    private final List<Duration> esperas = new ArrayList<>();
    private final List<Duration> tiempos = new ArrayList<>();
    private EjecutorResiliente ejecutor;

    @BeforeEach
    void preparar() {
        reloj = new RelojManual(Muestras.AHORA);
        circuito = new Cortacircuitos(OpcionesResiliencia.CUADRO_10, reloj);
        ejecutor = new EjecutorResiliente(OpcionesResiliencia.CUADRO_10, circuito, reloj, d -> {
            esperas.add(d);
            reloj.avanzar(d);
        }, false);
    }

    @Test
    void reintentaDosVecesConEsperaExponencialDesde500ms() {
        AtomicInteger intentos = new AtomicInteger();
        assertThrows(SistemaExternoNoDisponibleException.class, () -> ejecutor.ejecutar("PUT", true, t -> {
            tiempos.add(t);
            intentos.incrementAndGet();
            throw new FalloTransitorioException("503");
        }));
        assertEquals(3, intentos.get(), "1 intento + 2 reintentos");
        assertEquals(List.of(Duration.ofMillis(500), Duration.ofMillis(1000)), esperas);
        assertEquals(List.of(Duration.ofSeconds(3), Duration.ofSeconds(3), Duration.ofSeconds(3)), tiempos);
    }

    @Test
    void unaEntregaQueAgotaSusTresIntentosAbreElCircuito() {
        assertThrows(SistemaExternoNoDisponibleException.class, () -> ejecutor.ejecutar("PUT", true, t -> {
            throw new FalloTransitorioException("tiempo agotado");
        }));
        assertEquals(Cortacircuitos.Estado.ABIERTO, circuito.estado(), "cada intento cuenta: 3 de 3");
    }

    @Test
    void unaOperacionNoIdempotenteNoSeReintenta() {
        AtomicInteger intentos = new AtomicInteger();
        assertThrows(SistemaExternoNoDisponibleException.class, () -> ejecutor.ejecutar("POST", false, t -> {
            intentos.incrementAndGet();
            throw new FalloTransitorioException("503");
        }));
        assertEquals(1, intentos.get());
        assertTrue(esperas.isEmpty());
    }

    @Test
    void conElCircuitoAbiertoNoSeIntenta() {
        for (int i = 0; i < 3; i++) {
            circuito.permitir();
            circuito.registrarFallo();
        }
        AtomicInteger intentos = new AtomicInteger();
        assertThrows(SistemaExternoNoDisponibleException.class, () -> ejecutor.ejecutar("PUT", true, t -> {
            intentos.incrementAndGet();
            return "ok";
        }));
        assertEquals(0, intentos.get());
    }

    @Test
    void elTiempoDeCadaIntentoNoPasaElTotalDe20s() {
        OpcionesResiliencia corto = new OpcionesResiliencia(3, 5, 2, 0.5, 3, 1.0, 30, 15);
        EjecutorResiliente e = new EjecutorResiliente(corto, new Cortacircuitos(corto, reloj), reloj, d -> reloj.avanzar(d), false);
        assertThrows(SistemaExternoNoDisponibleException.class, () -> e.ejecutar("PUT", true, t -> {
            tiempos.add(t);
            reloj.avanzar(t); // el intento consume todo su tiempo
            throw new FalloTransitorioException("tiempo agotado");
        }));
        // 3 s, espera 0,5 s, 1,5 s restantes; la espera de 1 s ya no cabe.
        assertEquals(List.of(Duration.ofSeconds(3), Duration.ofMillis(1500)), tiempos);
    }

    @Test
    void unExitoDevuelveElResultadoYCuentaParaElCircuito() {
        AtomicInteger intentos = new AtomicInteger();
        String r = ejecutor.ejecutar("PUT", true, t -> {
            if (intentos.incrementAndGet() == 1) {
                throw new FalloTransitorioException("503");
            }
            return "ok";
        });
        assertEquals("ok", r);
        assertEquals(List.of(false, true), circuito.muestras());
    }

    @Test
    void laVariacionQuedaEntre80Y120PorCiento() {
        EjecutorResiliente conVariacion = new EjecutorResiliente(OpcionesResiliencia.CUADRO_10, circuito, reloj, d -> { }, true);
        for (int i = 0; i < 100; i++) {
            long ms = conVariacion.espera(1).toMillis();
            assertTrue(ms >= 800 && ms <= 1200, "espera " + ms);
        }
    }
}
