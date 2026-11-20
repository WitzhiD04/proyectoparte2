package plataforma.adaptador.se8.persistencia;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.time.Instant;
import java.util.UUID;

/**
 * Evento ya aplicado (consumo idempotente, AD-02). La clave es la llave primaria: un segundo registro de la misma clave
 * viola la restricción y revierte la transacción, aunque dos réplicas lo procesen a la vez.
 */
@Entity
@Table(name = "mensajes_procesados", schema = "se8")
public class MensajeProcesado {

    @Id
    @Column(name = "clave_idempotencia")
    private String claveIdempotencia;
    @Column(name = "id_evento", nullable = false)
    private UUID idEvento;
    @Column(nullable = false)
    private String tipo;
    @Column(name = "procesado_en", nullable = false)
    private Instant procesadoEn;

    protected MensajeProcesado() {
    }

    public MensajeProcesado(String claveIdempotencia, UUID idEvento, String tipo, Instant procesadoEn) {
        this.claveIdempotencia = claveIdempotencia;
        this.idEvento = idEvento;
        this.tipo = tipo;
        this.procesadoEn = procesadoEn;
    }

    public String getClaveIdempotencia() {
        return claveIdempotencia;
    }
}
