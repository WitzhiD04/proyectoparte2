package plataforma.adaptador.se8.persistencia;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import jakarta.persistence.Version;
import java.time.Instant;
import java.util.UUID;
import plataforma.adaptador.se8.contrato.Sobre;
import plataforma.adaptador.se8.traduccion.AcuseSe8;
import plataforma.adaptador.se8.traduccion.AlertaSe8;
import plataforma.adaptador.se8.traduccion.TraduccionSe8;

/**
 * Estado de entrega de una alerta a SE-8 (QA-08, RF-GD-15). Máquina de estados:
 * <pre>
 *   PENDIENTE --(SE-8 la recibe)--> ENVIADA --(acuse ENTREGADA)--> ENTREGADA
 *       ^                              |  \--(acuse RECHAZADA)----> FALLIDA
 *       +----(sin acuse en 5 s)--------+
 *   PENDIENTE o ENVIADA --(rechazo 4xx o envíos agotados)--> FALLIDA
 * </pre>
 * Solo {@link #registrarAcuse} lleva a ENTREGADA: nunca hay "entregada" sin acuse. La base lo exige también
 * ({@code ck_entregada_con_acuse}).
 */
@Entity
@Table(name = "entregas", schema = "se8")
public class Entrega {

    public enum Estado { PENDIENTE, ENVIADA, ENTREGADA, FALLIDA }

    @Id
    private UUID id;
    @Column(name = "clave_envio", nullable = false, unique = true)
    private String claveEnvio;
    @Column(name = "clave_idempotencia", nullable = false)
    private String claveIdempotencia;
    @Column(name = "tipo_origen", nullable = false)
    private String tipoOrigen;
    @Column(name = "id_evento_origen", nullable = false)
    private UUID idEventoOrigen;
    @Column(name = "id_notificacion")
    private UUID idNotificacion;
    @Column(name = "intento")
    private Integer intento;
    @Column(name = "id_desastre", nullable = false)
    private UUID idDesastre;
    @Column(nullable = false)
    private String canal;
    @Column(nullable = false)
    private String destino;
    @Column(nullable = false)
    private String asunto;
    @Column(nullable = false)
    private String mensaje;
    @Column(nullable = false)
    private String severidad;
    @Enumerated(EnumType.STRING)
    @Column(nullable = false)
    private Estado estado;
    @Column(nullable = false)
    private int envios;
    @Column(name = "creada_en", nullable = false)
    private Instant creadaEn;
    @Column(name = "ultimo_envio_en")
    private Instant ultimoEnvioEn;
    @Column(name = "acusada_en")
    private Instant acusadaEn;
    @Column(name = "secuencia_acuse")
    private Long secuenciaAcuse;
    private String motivo;
    @Version
    private int version;

    protected Entrega() {
    }

    /** Una entrega nueva, PENDIENTE, para la alerta de un evento de la cola de salida. */
    public static Entrega nueva(AlertaSe8 alerta, Sobre origen, Instant ahora) {
        Entrega e = new Entrega();
        e.id = UUID.randomUUID();
        e.claveEnvio = alerta.referenciaExterna();
        e.claveIdempotencia = origen.claveIdempotencia();
        e.tipoOrigen = origen.tipo();
        e.idEventoOrigen = origen.idEvento();
        if ("EV-28".equals(origen.tipo())) {
            e.idNotificacion = UUID.fromString(origen.datos().path("idNotificacion").asText());
            e.intento = origen.datos().path("intento").asInt();
        }
        e.idDesastre = UUID.fromString(alerta.codigoEvento());
        e.canal = TraduccionSe8.canal(alerta.medio());
        e.destino = alerta.destinatario();
        e.asunto = alerta.titulo();
        e.mensaje = alerta.cuerpo();
        e.severidad = TraduccionSe8.nivel(alerta.prioridad());
        e.estado = Estado.PENDIENTE;
        e.creadaEn = ahora;
        return e;
    }

    /** La alerta que se reenvía: la misma referencia, para que SE-8 no la duplique. */
    public AlertaSe8 alerta(String organismo) {
        return new AlertaSe8(claveEnvio, TraduccionSe8.medio(canal), destino, asunto, mensaje,
                TraduccionSe8.prioridad(severidad), idDesastre.toString(), organismo);
    }

    /** SE-8 recibió la alerta (201, 202) o ya la tenía (200). Falta su acuse. */
    public void marcarEnviada(Instant ahora) {
        if (estado != Estado.PENDIENTE && estado != Estado.ENVIADA) {
            throw new IllegalStateException("La entrega " + claveEnvio + " está " + estado + ": no se envía otra vez.");
        }
        estado = Estado.ENVIADA;
        envios++;
        ultimoEnvioEn = ahora;
    }

    /** No llegó el acuse a tiempo (QA-08: se reintenta y queda pendiente). */
    public void marcarSinAcuse() {
        if (estado != Estado.ENVIADA) {
            throw new IllegalStateException("Solo una entrega ENVIADA puede quedar pendiente por falta de acuse; está " + estado + ".");
        }
        estado = Estado.PENDIENTE;
    }

    /**
     * Aplica el acuse de SE-8. Devuelve falso si ya estaba aplicado (acuse repetido o tardío): el acuse es idempotente.
     */
    public boolean registrarAcuse(AcuseSe8 acuse, Instant ahora) {
        if (estado == Estado.ENTREGADA || estado == Estado.FALLIDA) {
            return false;
        }
        if (envios == 0) {
            throw new IllegalStateException("Acuse de " + claveEnvio + " sin ningún envío registrado.");
        }
        if (acuse.entregada()) {
            estado = Estado.ENTREGADA;
            acusadaEn = ahora;
        } else {
            estado = Estado.FALLIDA;
            motivo = acuse.motivo() == null ? "SE-8 rechazó la entrega" : acuse.motivo();
        }
        secuenciaAcuse = acuse.secuencia() > 0 ? acuse.secuencia() : null;
        return true;
    }

    /** Rechazo permanente de SE-8 o envíos agotados sin acuse. */
    public void marcarFallida(String motivo) {
        if (estado == Estado.ENTREGADA || estado == Estado.FALLIDA) {
            throw new IllegalStateException("La entrega " + claveEnvio + " ya terminó (" + estado + ").");
        }
        estado = Estado.FALLIDA;
        this.motivo = motivo;
    }

    public boolean deNotificacion() {
        return "EV-28".equals(tipoOrigen);
    }

    public UUID getId() {
        return id;
    }

    public String getClaveEnvio() {
        return claveEnvio;
    }

    public String getClaveIdempotencia() {
        return claveIdempotencia;
    }

    public String getTipoOrigen() {
        return tipoOrigen;
    }

    public UUID getIdEventoOrigen() {
        return idEventoOrigen;
    }

    public UUID getIdNotificacion() {
        return idNotificacion;
    }

    public Integer getIntento() {
        return intento;
    }

    public UUID getIdDesastre() {
        return idDesastre;
    }

    public String getCanal() {
        return canal;
    }

    public String getDestino() {
        return destino;
    }

    public String getAsunto() {
        return asunto;
    }

    public String getSeveridad() {
        return severidad;
    }

    public Estado getEstado() {
        return estado;
    }

    public int getEnvios() {
        return envios;
    }

    public Instant getCreadaEn() {
        return creadaEn;
    }

    public Instant getUltimoEnvioEn() {
        return ultimoEnvioEn;
    }

    public Instant getAcusadaEn() {
        return acusadaEn;
    }

    public Long getSecuenciaAcuse() {
        return secuenciaAcuse;
    }

    public String getMotivo() {
        return motivo;
    }
}
