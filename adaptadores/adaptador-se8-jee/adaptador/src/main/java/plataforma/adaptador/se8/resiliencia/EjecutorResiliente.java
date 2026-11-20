package plataforma.adaptador.se8.resiliencia;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.concurrent.ThreadLocalRandom;
import plataforma.adaptador.se8.contrato.SistemaExternoNoDisponibleException;

/**
 * Resiliencia de cada operación hacia SE-8, en el mismo orden que el pipeline de Polly de los adaptadores .NET
 * (Plataforma.Comun.Adaptadores.RegistroResiliencia). De afuera hacia adentro:
 * <ol>
 * <li>tiempo total de la operación (20 s);</li>
 * <li>reintento exponencial con variación aleatoria (2 reintentos desde 0,5 s), <b>solo para operaciones idempotentes</b>
 *     (Cuadro 5): una operación no idempotente se intenta una sola vez;</li>
 * <li>cortacircuitos: cada intento cuenta para él; con el circuito abierto no se intenta;</li>
 * <li>tiempo de espera de cada intento (3 s).</li>
 * </ol>
 * Cualquier fallo que agote la operación sale como {@link SistemaExternoNoDisponibleException}.
 */
public final class EjecutorResiliente {

    /** Un intento de la operación, con su tiempo máximo. */
    @FunctionalInterface
    public interface Intento<T> {
        T ejecutar(Duration tiempoMaximo) throws FalloTransitorioException;
    }

    /** Espera entre reintentos. Reemplazable en las pruebas. */
    @FunctionalInterface
    public interface Dormidor {
        void dormir(Duration espera) throws InterruptedException;
    }

    private final OpcionesResiliencia opciones;
    private final Cortacircuitos circuito;
    private final Clock reloj;
    private final Dormidor dormidor;
    private final boolean variacion;

    public EjecutorResiliente(OpcionesResiliencia opciones, Cortacircuitos circuito, Clock reloj, Dormidor dormidor,
            boolean variacion) {
        this.opciones = opciones;
        this.circuito = circuito;
        this.reloj = reloj;
        this.dormidor = dormidor;
        this.variacion = variacion;
    }

    public static EjecutorResiliente sistema(OpcionesResiliencia opciones, Cortacircuitos circuito) {
        return new EjecutorResiliente(opciones, circuito, Clock.systemUTC(), d -> Thread.sleep(d.toMillis()), true);
    }

    public Cortacircuitos circuito() {
        return circuito;
    }

    public <T> T ejecutar(String operacion, boolean idempotente, Intento<T> intento) {
        Instant limite = reloj.instant().plus(opciones.total());
        int maximo = idempotente ? 1 + Math.max(0, opciones.reintentos()) : 1;
        FalloTransitorioException ultimo = null;
        for (int n = 0; n < maximo; n++) {
            if (!circuito.permitir()) {
                throw new SistemaExternoNoDisponibleException("SE-8: circuito abierto; " + operacion + " no se intenta.");
            }
            Duration restante = Duration.between(reloj.instant(), limite);
            if (restante.isNegative() || restante.isZero()) {
                circuito.registrarFallo();
                break;
            }
            Duration tiempo = restante.compareTo(opciones.porIntento()) < 0 ? restante : opciones.porIntento();
            try {
                T resultado = intento.ejecutar(tiempo);
                circuito.registrarExito();
                return resultado;
            } catch (FalloTransitorioException e) {
                circuito.registrarFallo();
                ultimo = e;
            }
            if (n + 1 >= maximo) {
                break;
            }
            Duration espera = espera(n);
            if (reloj.instant().plus(espera).isAfter(limite)) {
                break; // la espera pasaría el tiempo total
            }
            try {
                dormidor.dormir(espera);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                throw new SistemaExternoNoDisponibleException("SE-8: " + operacion + " interrumpida.", e);
            }
        }
        throw new SistemaExternoNoDisponibleException("SE-8 no responde (" + operacion + "): "
                + (ultimo == null ? "tiempo total agotado" : ultimo.getMessage()), ultimo);
    }

    /** Espera antes del reintento n+1: base · 2^n, con ±20 % de variación para no sincronizar réplicas. */
    Duration espera(int n) {
        long base = opciones.esperaBase().toMillis() << n;
        if (!variacion) {
            return Duration.ofMillis(base);
        }
        double factor = 0.8 + ThreadLocalRandom.current().nextDouble() * 0.4;
        return Duration.ofMillis(Math.round(base * factor));
    }
}
