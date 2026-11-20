package plataforma.adaptador.se8.persistencia;

import jakarta.persistence.EntityManager;
import jakarta.persistence.LockModeType;
import java.sql.Timestamp;
import java.time.Instant;
import java.util.List;
import java.util.Optional;
import plataforma.adaptador.se8.contrato.Sobre;
import plataforma.adaptador.se8.contrato.ValidadorContrato;

/** {@link AlmacenSe8} con Jakarta Persistence sobre el esquema {@code se8}. */
public final class AlmacenJpa implements AlmacenSe8 {

    private final EntityManager em;
    private final ValidadorContrato validador = ValidadorContrato.instancia();

    public AlmacenJpa(EntityManager em) {
        this.em = em;
    }

    @Override
    public boolean yaProcesado(String claveIdempotencia) {
        return em.find(MensajeProcesado.class, claveIdempotencia) != null;
    }

    @Override
    public void registrarProcesado(Sobre sobre, Instant ahora) {
        em.persist(new MensajeProcesado(sobre.claveIdempotencia(), sobre.idEvento(), sobre.tipo(), ahora));
        em.flush(); // la violación de la llave primaria sale aquí, dentro de la transacción del mensaje
    }

    @Override
    public Optional<Entrega> buscarEntrega(String claveEnvio) {
        return em.createQuery("SELECT e FROM Entrega e WHERE e.claveEnvio = :clave", Entrega.class)
                .setParameter("clave", claveEnvio)
                .getResultStream()
                .findFirst();
    }

    @Override
    public void guardar(Entrega entrega) {
        em.persist(entrega);
    }

    @Override
    public void agregarABandeja(Sobre sobre) {
        validador.validar(sobre); // un evento inválido no sale nunca
        em.persist(FilaBandeja.de(sobre, 0));
    }

    @Override
    public int registrarFallo(String claveIdempotencia, String error, Instant ahora) {
        IntentoConsumo intento = em.find(IntentoConsumo.class, claveIdempotencia, LockModeType.PESSIMISTIC_WRITE);
        if (intento == null) {
            intento = new IntentoConsumo(claveIdempotencia);
            int fallos = intento.registrar(error, ahora);
            em.persist(intento);
            return fallos;
        }
        return intento.registrar(error, ahora);
    }

    @Override
    @SuppressWarnings("unchecked")
    public List<Entrega> entregasSinAcuse(Instant antesDe, int limite) {
        return em.createNativeQuery("""
                SELECT * FROM se8.entregas
                WHERE tipo_origen IN ('EV-03', 'EV-04') AND estado IN ('ENVIADA', 'PENDIENTE') AND ultimo_envio_en < ?1
                ORDER BY ultimo_envio_en LIMIT ?2 FOR UPDATE SKIP LOCKED""", Entrega.class)
                .setParameter(1, Timestamp.from(antesDe))
                .setParameter(2, limite)
                .getResultList();
    }

    @Override
    public MarcaAvance marca(String flujo, Instant ahora) {
        em.createNativeQuery("INSERT INTO se8.marcas_avance (flujo, marca, actualizada_en) VALUES (?1, NULL, ?2) "
                        + "ON CONFLICT (flujo) DO NOTHING")
                .setParameter(1, flujo)
                .setParameter(2, Timestamp.from(ahora))
                .executeUpdate();
        return em.find(MarcaAvance.class, flujo, LockModeType.PESSIMISTIC_WRITE);
    }

    @Override
    public EstadoIntegracionFila estadoIntegracion(String sistema) {
        em.createNativeQuery("INSERT INTO se8.estado_integracion (sistema, estado, abierto_desde, actualizado_en) "
                        + "VALUES (?1, 'CERRADO', NULL, now()) ON CONFLICT (sistema) DO NOTHING")
                .setParameter(1, sistema)
                .executeUpdate();
        return em.find(EstadoIntegracionFila.class, sistema, LockModeType.PESSIMISTIC_WRITE);
    }
}
