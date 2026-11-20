package plataforma.adaptador.se8.resiliencia;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import plataforma.adaptador.se8.Muestras;
import plataforma.adaptador.se8.RelojManual;

/** Regla del Cuadro 11: abre con ≥ 3 llamadas en 30 s y 100 % de fallo; 15 s abierto antes de la llamada de prueba. */
class CortacircuitosTest {

    private RelojManual reloj;
    private Cortacircuitos circuito;
    private final List<String> avisos = new ArrayList<>();

    @BeforeEach
    void preparar() {
        reloj = new RelojManual(Muestras.AHORA);
        circuito = new Cortacircuitos(OpcionesResiliencia.CUADRO_10, reloj);
        circuito.agregarObservador(new Cortacircuitos.Observador() {
            @Override
            public void alAbrirse(Instant instante) {
                avisos.add("ABIERTO@" + instante);
            }

            @Override
            public void alCerrarse(Instant instante, Instant abiertoDesde) {
                avisos.add("CERRADO@" + instante + " desde " + abiertoDesde);
            }
        });
    }

    private void fallar(int veces) {
        for (int i = 0; i < veces; i++) {
            assertTrue(circuito.permitir());
            circuito.registrarFallo();
        }
    }

    @Test
    void dosFallosNoLoAbren() {
        fallar(2);
        assertEquals(Cortacircuitos.Estado.CERRADO, circuito.estado());
        assertTrue(avisos.isEmpty());
    }

    @Test
    void tresDeTresFallosEnLaVentanaLoAbrenYAvisanUnaVez() {
        fallar(3);
        assertEquals(Cortacircuitos.Estado.ABIERTO, circuito.estado());
        assertFalse(circuito.permitir(), "abierto no deja pasar llamadas");
        assertEquals(List.of("ABIERTO@" + Muestras.AHORA), avisos);
    }

    @Test
    void unExitoEnLaVentanaImpideAbrirlo() {
        fallar(2);
        circuito.permitir();
        circuito.registrarExito();
        fallar(1); // 3 de 4: proporción 75 % < 100 %
        assertEquals(Cortacircuitos.Estado.CERRADO, circuito.estado());
    }

    @Test
    void losFallosFueraDeLaVentanaDe30sNoCuentan() {
        fallar(2);
        reloj.avanzar(Duration.ofSeconds(31));
        fallar(1);
        assertEquals(Cortacircuitos.Estado.CERRADO, circuito.estado());
        assertEquals(1, circuito.muestras().size());
    }

    @Test
    void despuesDe15sDejaPasarUnaSolaLlamadaDePrueba() {
        fallar(3);
        reloj.avanzar(Duration.ofSeconds(14));
        assertFalse(circuito.permitir());
        reloj.avanzar(Duration.ofSeconds(1));
        assertTrue(circuito.permitir(), "vencida la apertura pasa la prueba");
        assertEquals(Cortacircuitos.Estado.SEMIABIERTO, circuito.estado());
        assertFalse(circuito.permitir(), "solo una llamada de prueba a la vez");
        assertTrue(circuito.abierto(), "para la plataforma semiabierto sigue siendo ABIERTO");
    }

    @Test
    void unaPruebaFallidaLoReabreSinAvisoNuevo() {
        fallar(3);
        reloj.avanzar(Duration.ofSeconds(15));
        assertTrue(circuito.permitir());
        circuito.registrarFallo();
        assertEquals(Cortacircuitos.Estado.ABIERTO, circuito.estado());
        assertEquals(Muestras.AHORA, circuito.abiertoDesde(), "conserva el instante de la primera apertura");
        reloj.avanzar(Duration.ofSeconds(10));
        assertFalse(circuito.permitir(), "otros 15 s desde la prueba fallida");
        assertEquals(1, avisos.size());
    }

    @Test
    void unaPruebaExitosaLoCierraYAvisaConElInstanteDeApertura() {
        fallar(3);
        reloj.avanzar(Duration.ofSeconds(15));
        assertTrue(circuito.permitir());
        circuito.registrarExito();
        assertEquals(Cortacircuitos.Estado.CERRADO, circuito.estado());
        assertEquals(List.of("ABIERTO@" + Muestras.AHORA,
                "CERRADO@" + Muestras.AHORA.plusSeconds(15) + " desde " + Muestras.AHORA), avisos);
        assertTrue(circuito.muestras().isEmpty(), "al cerrarse empieza una ventana nueva");
    }

    @Test
    void conMuchosHilosFallandoALaVezAvisaUnaSolaApertura() throws Exception {
        AtomicInteger aperturas = new AtomicInteger();
        Cortacircuitos concurrente = new Cortacircuitos(OpcionesResiliencia.CUADRO_10, reloj);
        concurrente.agregarObservador(new Cortacircuitos.Observador() {
            @Override
            public void alAbrirse(Instant instante) {
                aperturas.incrementAndGet();
            }

            @Override
            public void alCerrarse(Instant instante, Instant abiertoDesde) {
            }
        });
        ExecutorService hilos = Executors.newFixedThreadPool(16);
        CountDownLatch salida = new CountDownLatch(1);
        for (int i = 0; i < 200; i++) {
            hilos.submit(() -> {
                salida.await();
                if (concurrente.permitir()) {
                    concurrente.registrarFallo();
                }
                return null;
            });
        }
        salida.countDown();
        hilos.shutdown();
        assertTrue(hilos.awaitTermination(10, TimeUnit.SECONDS));
        assertEquals(1, aperturas.get());
        assertEquals(Cortacircuitos.Estado.ABIERTO, concurrente.estado());
    }

    @Test
    void esperarCierreVuelveCuandoSeCierra() throws Exception {
        fallar(3);
        reloj.avanzar(Duration.ofSeconds(15));
        Thread prueba = new Thread(() -> {
            circuito.permitir();
            circuito.registrarExito();
        });
        prueba.start();
        assertTrue(circuito.esperarCierre(Duration.ofSeconds(5)));
        prueba.join();
    }
}
