package plataforma.adaptador.se8.persistencia;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.time.Instant;

/** Marca de avance de la entrada (AD-14): último acuse de SE-8 conciliado. */
@Entity
@Table(name = "marcas_avance", schema = "se8")
public class MarcaAvance {

    public static final String FLUJO_ACUSES = "se8.acuses";

    @Id
    private String flujo;
    private String marca;
    @Column(name = "actualizada_en", nullable = false)
    private Instant actualizadaEn;

    protected MarcaAvance() {
    }

    public MarcaAvance(String flujo, Instant ahora) {
        this.flujo = flujo;
        this.actualizadaEn = ahora;
    }

    public String getMarca() {
        return marca;
    }

    public void avanzar(String marca, Instant ahora) {
        this.marca = marca;
        this.actualizadaEn = ahora;
    }
}
