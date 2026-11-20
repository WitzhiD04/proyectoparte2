package plataforma.adaptador.se8.persistencia;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.time.Instant;

/** Estado observable de la integración con SE-8 (E-15). Cambia en la misma transacción que el EV-23 que lo anuncia. */
@Entity
@Table(name = "estado_integracion", schema = "se8")
public class EstadoIntegracionFila {

    @Id
    private String sistema;
    @Column(nullable = false)
    private String estado;
    @Column(name = "abierto_desde")
    private Instant abiertoDesde;
    @Column(name = "actualizado_en", nullable = false)
    private Instant actualizadoEn;

    protected EstadoIntegracionFila() {
    }

    public EstadoIntegracionFila(String sistema) {
        this.sistema = sistema;
        this.estado = "CERRADO";
    }

    public String cambiar(String nuevo, Instant abiertoDesde, Instant ahora) {
        String anterior = estado;
        this.estado = nuevo;
        this.abiertoDesde = abiertoDesde;
        this.actualizadoEn = ahora;
        return anterior;
    }

    public String getEstado() {
        return estado;
    }

    public Instant getAbiertoDesde() {
        return abiertoDesde;
    }
}
