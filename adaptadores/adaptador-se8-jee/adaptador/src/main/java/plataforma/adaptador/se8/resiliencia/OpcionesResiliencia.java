package plataforma.adaptador.se8.resiliencia;

import java.time.Duration;

/**
 * Valores de la resiliencia hacia SE-8 (Cuadro 10 del SAD, AD-05, AD-37). Mismos nombres y valores por omisión que
 * {@code OpcionesResiliencia} de los adaptadores .NET (variables {@code Resiliencia__*}).
 */
public record OpcionesResiliencia(
        double tiempoEsperaIntentoSegundos,
        double tiempoEsperaTotalSegundos,
        int reintentos,
        double esperaBaseReintentoSegundos,
        int minimoLlamadas,
        double proporcionFallos,
        double ventanaSegundos,
        double duracionAperturaSegundos) {

    /** 3 s por intento, 20 s por operación, 2 reintentos desde 0,5 s; abre con 3 de 3 en 30 s; 15 s abierto. */
    public static final OpcionesResiliencia CUADRO_10 = new OpcionesResiliencia(3, 20, 2, 0.5, 3, 1.0, 30, 15);

    public Duration porIntento() {
        return segundos(tiempoEsperaIntentoSegundos);
    }

    public Duration total() {
        return segundos(tiempoEsperaTotalSegundos);
    }

    public Duration esperaBase() {
        return segundos(esperaBaseReintentoSegundos);
    }

    public Duration ventana() {
        return segundos(ventanaSegundos);
    }

    public Duration apertura() {
        return segundos(duracionAperturaSegundos);
    }

    private static Duration segundos(double s) {
        return Duration.ofMillis(Math.round(s * 1000));
    }
}
