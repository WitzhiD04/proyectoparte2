package plataforma.adaptador.se8.persistencia;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.time.Instant;
import java.util.UUID;
import org.hibernate.annotations.JdbcTypeCode;
import org.hibernate.type.SqlTypes;
import plataforma.adaptador.se8.contrato.CatalogoEventos;
import plataforma.adaptador.se8.contrato.Sobre;

/**
 * Evento por publicar o ya publicado (bandeja de salida, AD-45). Mismas columnas que {@code FilaBandeja} de
 * Plataforma.Comun.Mensajeria. La secuencia del productor la asigna el relevo, no quien escribe.
 */
@Entity
@Table(name = "bandeja_salida", schema = "se8")
public class FilaBandeja {

    @Id
    private UUID id;
    @Column(name = "secuencia", insertable = false, updatable = false)
    private Long secuencia;
    @Column(name = "secuencia_productor")
    private Long secuenciaProductor;
    @Column(nullable = false)
    private String tipo;
    @Column(name = "clave_ruteo", nullable = false)
    private String claveRuteo;
    @Column(name = "clave_idempotencia", nullable = false, unique = true)
    private String claveIdempotencia;
    @Column(name = "id_correlacion")
    private String idCorrelacion;
    @JdbcTypeCode(SqlTypes.JSON)
    @Column(nullable = false, columnDefinition = "jsonb")
    private String sobre;
    @Column(nullable = false)
    private Instant instante;
    @Column(nullable = false)
    private int orden;
    @Column(name = "publicado_en")
    private Instant publicadoEn;

    protected FilaBandeja() {
    }

    /** Fila para un sobre ya validado, sin secuencia del productor. */
    public static FilaBandeja de(Sobre sobre, int orden) {
        if (sobre.secuenciaProductor() != null) {
            throw new IllegalStateException("La secuencia del productor la asigna el relevo, no quien escribe el evento.");
        }
        FilaBandeja f = new FilaBandeja();
        f.id = sobre.idEvento();
        f.tipo = sobre.tipo();
        f.claveRuteo = CatalogoEventos.porTipo(sobre.tipo()).claveRuteo();
        f.claveIdempotencia = sobre.claveIdempotencia();
        f.idCorrelacion = sobre.idCorrelacion();
        f.sobre = sobre.serializar();
        f.instante = sobre.instanteEmision();
        f.orden = orden;
        return f;
    }

    public void marcarPublicado(Instant ahora) {
        publicadoEn = ahora;
    }

    public UUID getId() {
        return id;
    }

    public Long getSecuenciaProductor() {
        return secuenciaProductor;
    }

    public String getTipo() {
        return tipo;
    }

    public String getClaveRuteo() {
        return claveRuteo;
    }

    public String getClaveIdempotencia() {
        return claveIdempotencia;
    }

    public String getSobre() {
        return sobre;
    }

    public Instant getPublicadoEn() {
        return publicadoEn;
    }
}
