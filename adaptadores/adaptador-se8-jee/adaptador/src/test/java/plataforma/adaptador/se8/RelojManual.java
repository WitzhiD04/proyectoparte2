package plataforma.adaptador.se8;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneId;
import java.time.ZoneOffset;

/** Reloj que avanza solo cuando la prueba lo pide. */
public final class RelojManual extends Clock {

    private volatile Instant ahora;

    public RelojManual(Instant inicio) {
        this.ahora = inicio;
    }

    public void avanzar(Duration duracion) {
        ahora = ahora.plus(duracion);
    }

    @Override
    public Instant instant() {
        return ahora;
    }

    @Override
    public ZoneId getZone() {
        return ZoneOffset.UTC;
    }

    @Override
    public Clock withZone(ZoneId zone) {
        return this;
    }
}
