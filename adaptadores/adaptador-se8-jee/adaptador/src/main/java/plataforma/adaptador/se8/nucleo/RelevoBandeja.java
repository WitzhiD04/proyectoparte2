package plataforma.adaptador.se8.nucleo;

import jakarta.persistence.EntityManager;
import java.sql.Timestamp;
import java.time.Clock;
import java.util.List;
import java.util.UUID;
import plataforma.adaptador.se8.contrato.Sobre;
import plataforma.adaptador.se8.persistencia.FilaBandeja;

/**
 * Relevo de la bandeja de salida hacia el bus (AD-45, DA-04), con las dos fases de {@code RelevoBandeja} de comun:
 * <ol>
 * <li>Numerar: bajo el bloqueo del contador del productor asigna una secuencia consecutiva y sin huecos a los eventos ya
 *     confirmados (mitigación de ATAM R-02), aunque haya varias réplicas.</li>
 * <li>Publicar: envía los eventos numerados en orden y los marca como publicados solo cuando el bus los acepta. Se
 *     detiene en el primer rechazo para conservar el orden: nunca se marca un evento que el bus no aceptó (QA-04).</li>
 * </ol>
 * Cada fase corre en su propia transacción (la abre quien llama).
 */
public final class RelevoBandeja {

    /** Publica un sobre en el exchange de la plataforma. Termina cuando el bus lo aceptó; si no, lanza. */
    @FunctionalInterface
    public interface PublicadorBus {
        void publicar(String claveRuteo, Sobre sobre) throws Exception;
    }

    private final EntityManager em;
    private final String productor;
    private final int lote;
    private final Clock reloj;

    public RelevoBandeja(EntityManager em, String productor, int lote, Clock reloj) {
        this.em = em;
        this.productor = productor;
        this.lote = lote;
        this.reloj = reloj;
    }

    /** Fase 1. Devuelve cuántos eventos numeró. */
    @SuppressWarnings("unchecked")
    public int numerar() {
        em.createNativeQuery("INSERT INTO se8.contador_productor (productor, ultimo) VALUES (?1, 0) "
                + "ON CONFLICT (productor) DO NOTHING").setParameter(1, productor).executeUpdate();
        // El bloqueo del contador serializa la numeración entre réplicas.
        long ultimo = ((Number) em.createNativeQuery(
                        "SELECT ultimo FROM se8.contador_productor WHERE productor = ?1 FOR UPDATE")
                .setParameter(1, productor).getSingleResult()).longValue();
        List<Object> pendientes = em.createNativeQuery("""
                SELECT id FROM se8.bandeja_salida WHERE secuencia_productor IS NULL
                ORDER BY instante, orden, secuencia LIMIT ?1 FOR UPDATE SKIP LOCKED""")
                .setParameter(1, lote).getResultList();
        if (pendientes.isEmpty()) {
            return 0;
        }
        long secuencia = ultimo;
        for (Object id : pendientes) {
            secuencia++;
            em.createNativeQuery("UPDATE se8.bandeja_salida SET secuencia_productor = ?1 WHERE id = ?2")
                    .setParameter(1, secuencia)
                    .setParameter(2, id instanceof UUID u ? u : UUID.fromString(id.toString()))
                    .executeUpdate();
        }
        em.createNativeQuery("UPDATE se8.contador_productor SET ultimo = ?1 WHERE productor = ?2")
                .setParameter(1, secuencia).setParameter(2, productor).executeUpdate();
        return pendientes.size();
    }

    /** Fase 2. Devuelve cuántos publicó y si el bus rechazó alguno. */
    @SuppressWarnings("unchecked")
    public Resultado publicar(PublicadorBus publicador) {
        List<FilaBandeja> filas = em.createNativeQuery("""
                SELECT * FROM se8.bandeja_salida WHERE secuencia_productor IS NOT NULL AND publicado_en IS NULL
                ORDER BY secuencia_productor LIMIT ?1 FOR UPDATE SKIP LOCKED""", FilaBandeja.class)
                .setParameter(1, lote).getResultList();
        int publicados = 0;
        for (FilaBandeja fila : filas) {
            Sobre sobre = Sobre.leer(fila.getSobre()).conSecuencia(fila.getSecuenciaProductor());
            try {
                publicador.publicar(fila.getClaveRuteo(), sobre);
            } catch (Exception e) {
                return new Resultado(publicados, true, e.getMessage());
            }
            fila.marcarPublicado(reloj.instant());
            publicados++;
        }
        return new Resultado(publicados, false, null);
    }

    /** Eventos que esperan su número o su publicación (diagnóstico). */
    public long pendientes() {
        return ((Number) em.createNativeQuery("SELECT count(*) FROM se8.bandeja_salida WHERE publicado_en IS NULL")
                .getSingleResult()).longValue();
    }

    static Timestamp ts(java.time.Instant instante) {
        return Timestamp.from(instante);
    }

    public record Resultado(int publicados, boolean rechazado, String error) {
    }
}
