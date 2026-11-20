package plataforma.adaptador.se8.persistencia;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.time.Instant;

/** Fallos de aplicación de un mensaje. A los 5 el consumidor lo aparta a dlq.salida.se8 (como ConsumidorIdempotente). */
@Entity
@Table(name = "intentos_consumo", schema = "se8")
public class IntentoConsumo {

    @Id
    @Column(name = "clave_idempotencia")
    private String claveIdempotencia;
    @Column(nullable = false)
    private int fallos;
    @Column(name = "ultimo_error")
    private String ultimoError;
    @Column(name = "actualizado_en", nullable = false)
    private Instant actualizadoEn;

    protected IntentoConsumo() {
    }

    public IntentoConsumo(String claveIdempotencia) {
        this.claveIdempotencia = claveIdempotencia;
    }

    public int registrar(String error, Instant ahora) {
        fallos++;
        ultimoError = error == null ? null : error.substring(0, Math.min(error.length(), 1000));
        actualizadoEn = ahora;
        return fallos;
    }

    public int getFallos() {
        return fallos;
    }
}
