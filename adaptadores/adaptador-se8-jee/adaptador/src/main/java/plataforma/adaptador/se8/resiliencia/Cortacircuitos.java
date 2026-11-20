package plataforma.adaptador.se8.resiliencia;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.List;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.locks.Condition;
import java.util.concurrent.locks.ReentrantLock;

/**
 * Cortacircuitos de las llamadas a SE-8 (AD-05, AD-37). Implementación propia, segura ante concurrencia, con la misma
 * regla que el de Polly en los adaptadores .NET:
 * <ul>
 * <li>Se abre si en la ventana de muestreo (30 s) hubo al menos {@code minimoLlamadas} (3) llamadas y la proporción de
 *     fallos llegó a {@code proporcionFallos} (100 %).</li>
 * <li>Abierto rechaza las llamadas durante {@code duracionApertura} (15 s). Después deja pasar una sola llamada de prueba
 *     (semiabierto): si responde se cierra y vacía la ventana; si falla se abre otros 15 s.</li>
 * </ul>
 * Por qué no MicroProfile Fault Tolerance (AD-66, compromiso aceptado): su {@code @CircuitBreaker} mide una ventana de N
 * llamadas, no una ventana de tiempo de 30 s, y no avisa cuando el circuito se abre o se cierra. Ese aviso hace falta
 * para publicar EV-23 por la bandeja de salida (E-15, QA-01 b).
 *
 * <p>Avisos: los observadores reciben solo la apertura y el cierre, como {@code MonitorCircuito} de comun. Una prueba que
 * falla vuelve a abrirlo sin aviso: para la plataforma sigue abierto desde la primera vez. Los avisos se entregan fuera
 * del candado.</p>
 */
public final class Cortacircuitos {

    public enum Estado { CERRADO, ABIERTO, SEMIABIERTO }

    /** Recibe la apertura y el cierre observables. */
    public interface Observador {
        void alAbrirse(Instant instante);

        void alCerrarse(Instant instante, Instant abiertoDesde);
    }

    private record Muestra(Instant instante, boolean exito) {
    }

    private final ReentrantLock candado = new ReentrantLock();
    private final Condition cambio = candado.newCondition();
    private final Deque<Muestra> ventana = new ArrayDeque<>();
    private final List<Observador> observadores = new CopyOnWriteArrayList<>();
    private final OpcionesResiliencia opciones;
    private final Clock reloj;

    private Estado estado = Estado.CERRADO;
    private Instant abiertoHasta;
    private Instant abiertoDesde;
    private boolean pruebaEnCurso;

    public Cortacircuitos(OpcionesResiliencia opciones, Clock reloj) {
        this.opciones = opciones;
        this.reloj = reloj;
    }

    public void agregarObservador(Observador observador) {
        observadores.add(observador);
    }

    /**
     * Pide permiso para una llamada. Cerrado: siempre. Abierto: no, hasta que vence la apertura; entonces pasa a semiabierto
     * y deja pasar una sola llamada de prueba.
     */
    public boolean permitir() {
        candado.lock();
        try {
            Instant ahora = reloj.instant();
            if (estado == Estado.ABIERTO && !ahora.isBefore(abiertoHasta)) {
                estado = Estado.SEMIABIERTO;
                pruebaEnCurso = false;
            }
            switch (estado) {
                case CERRADO:
                    return true;
                case SEMIABIERTO:
                    if (pruebaEnCurso) {
                        return false;
                    }
                    pruebaEnCurso = true;
                    return true;
                default:
                    return false;
            }
        } finally {
            candado.unlock();
        }
    }

    public void registrarExito() {
        Instant cerradoEn = null;
        Instant desde = null;
        candado.lock();
        try {
            Instant ahora = reloj.instant();
            if (estado == Estado.ABIERTO) {
                return; // una llamada que empezó antes de abrirse: no cierra el circuito; solo la prueba lo cierra
            }
            if (estado == Estado.SEMIABIERTO) {
                // La llamada de prueba respondió: se cierra.
                desde = abiertoDesde;
                estado = Estado.CERRADO;
                ventana.clear();
                pruebaEnCurso = false;
                abiertoDesde = null;
                abiertoHasta = null;
                cerradoEn = ahora;
                cambio.signalAll();
            } else {
                agregar(new Muestra(ahora, true));
            }
        } finally {
            candado.unlock();
        }
        if (cerradoEn != null) {
            for (Observador o : observadores) {
                o.alCerrarse(cerradoEn, desde);
            }
        }
    }

    public void registrarFallo() {
        Instant abiertoEn = null;
        candado.lock();
        try {
            Instant ahora = reloj.instant();
            if (estado == Estado.SEMIABIERTO) {
                estado = Estado.ABIERTO; // la prueba falló: otros 15 s; para la plataforma sigue abierto desde antes
                abiertoHasta = ahora.plus(opciones.apertura());
                pruebaEnCurso = false;
                return;
            }
            if (estado == Estado.ABIERTO) {
                return;
            }
            agregar(new Muestra(ahora, false));
            long fallos = ventana.stream().filter(m -> !m.exito()).count();
            if (ventana.size() >= opciones.minimoLlamadas()
                    && (double) fallos / ventana.size() >= opciones.proporcionFallos()) {
                estado = Estado.ABIERTO;
                abiertoDesde = ahora;
                abiertoHasta = ahora.plus(opciones.apertura());
                ventana.clear();
                abiertoEn = ahora;
                cambio.signalAll();
            }
        } finally {
            candado.unlock();
        }
        if (abiertoEn != null) {
            for (Observador o : observadores) {
                o.alAbrirse(abiertoEn);
            }
        }
    }

    /** Estado interno (con semiabierto). Para la plataforma, semiabierto sigue siendo ABIERTO. */
    public Estado estado() {
        candado.lock();
        try {
            return estado;
        } finally {
            candado.unlock();
        }
    }

    /** CERRADO o ABIERTO: lo que se publica en EV-23. */
    public boolean abierto() {
        return estado() != Estado.CERRADO;
    }

    public Instant abiertoDesde() {
        candado.lock();
        try {
            return abiertoDesde;
        } finally {
            candado.unlock();
        }
    }

    /** Espera a que el circuito se cierre, o a que pase {@code maximo}. Devuelve verdadero si está cerrado. */
    public boolean esperarCierre(Duration maximo) throws InterruptedException {
        candado.lock();
        try {
            long nanos = maximo.toNanos();
            while (estado != Estado.CERRADO && nanos > 0) {
                nanos = cambio.awaitNanos(nanos);
            }
            return estado == Estado.CERRADO;
        } finally {
            candado.unlock();
        }
    }

    /** Espera a que el circuito se abra, o a que pase {@code maximo}. Devuelve verdadero si está abierto. */
    public boolean esperarApertura(Duration maximo) throws InterruptedException {
        candado.lock();
        try {
            long nanos = maximo.toNanos();
            while (estado == Estado.CERRADO && nanos > 0) {
                nanos = cambio.awaitNanos(nanos);
            }
            return estado != Estado.CERRADO;
        } finally {
            candado.unlock();
        }
    }

    /** Muestras vigentes de la ventana (para pruebas y diagnóstico). */
    public List<Boolean> muestras() {
        candado.lock();
        try {
            purgar(reloj.instant());
            List<Boolean> lista = new ArrayList<>();
            ventana.forEach(m -> lista.add(m.exito()));
            return lista;
        } finally {
            candado.unlock();
        }
    }

    private void agregar(Muestra muestra) {
        purgar(muestra.instante());
        ventana.addLast(muestra);
    }

    private void purgar(Instant ahora) {
        Instant limite = ahora.minus(opciones.ventana());
        while (!ventana.isEmpty() && !ventana.peekFirst().instante().isAfter(limite)) {
            ventana.removeFirst();
        }
    }
}
