package plataforma.adaptador.se8.jee;

import jakarta.ejb.EJB;
import jakarta.ejb.Stateless;
import jakarta.ejb.TransactionAttribute;
import jakarta.ejb.TransactionAttributeType;
import jakarta.inject.Inject;
import jakarta.persistence.EntityManager;
import jakarta.persistence.PersistenceContext;
import java.time.Clock;
import java.time.Instant;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.UUID;
import plataforma.adaptador.se8.cliente.ClienteSe8;
import plataforma.adaptador.se8.configuracion.Configuracion;
import plataforma.adaptador.se8.contrato.Sobre;
import plataforma.adaptador.se8.nucleo.AplicadorAcuses;
import plataforma.adaptador.se8.nucleo.ConciliadorAcuses;
import plataforma.adaptador.se8.nucleo.EstadoIntegracionSe8;
import plataforma.adaptador.se8.nucleo.ProcesadorSe8;
import plataforma.adaptador.se8.nucleo.ReenvioSinAcuse;
import plataforma.adaptador.se8.nucleo.RelevoBandeja;
import plataforma.adaptador.se8.persistencia.AlmacenJpa;
import plataforma.adaptador.se8.persistencia.Entrega;
import plataforma.adaptador.se8.traduccion.AcuseSe8;
import plataforma.adaptador.se8.traduccion.OpcionesSustituto;

/**
 * Unidades de trabajo del adaptador. Cada método de negocio abre su propia transacción JTA ({@code REQUIRES_NEW}) y la
 * confirma al volver: quien llama sabe que, si el método volvió sin excepción, el cambio ya está confirmado. Por eso el
 * consumidor confirma el mensaje JMS después de {@link #procesar} (ack solo después del commit JPA).
 */
@Stateless
public class ManejadorSe8 {

    @PersistenceContext(unitName = "adaptador-se8")
    EntityManager em;
    @EJB
    AdaptadorSe8 adaptador;
    @Inject
    ClienteSe8 cliente;
    @Inject
    OpcionesSustituto sustituto;
    @Inject
    Configuracion configuracion;
    @Inject
    Clock reloj;

    /** Aplica un evento de q.salida.se8 una sola vez. */
    @TransactionAttribute(TransactionAttributeType.REQUIRES_NEW)
    public ProcesadorSe8.Resultado procesar(Sobre sobre) {
        return new ProcesadorSe8(new AlmacenJpa(em), adaptador, sustituto, reloj).procesar(sobre);
    }

    /** Suma un fallo de aplicación al mensaje y devuelve el total (transacción aparte de la del mensaje revertido). */
    @TransactionAttribute(TransactionAttributeType.REQUIRES_NEW)
    public int registrarFallo(String claveIdempotencia, String error) {
        return new AlmacenJpa(em).registrarFallo(claveIdempotencia, error, reloj.instant());
    }

    @TransactionAttribute(TransactionAttributeType.REQUIRES_NEW)
    public AplicadorAcuses.Resultado aplicarAcuse(AcuseSe8 acuse) {
        return aplicador().aplicar(acuse);
    }

    /** Concilia una página de acuses desde la marca de avance. Devuelve verdadero si hay más. */
    @TransactionAttribute(TransactionAttributeType.REQUIRES_NEW)
    public boolean conciliarPagina() {
        AlmacenJpa almacen = new AlmacenJpa(em);
        return new ConciliadorAcuses(almacen, adaptador, aplicador(), reloj).conciliarPagina();
    }

    @TransactionAttribute(TransactionAttributeType.REQUIRES_NEW)
    public ReenvioSinAcuse.Resumen revisarReenvios(int limite) {
        return new ReenvioSinAcuse(new AlmacenJpa(em), cliente, configuracion.sinAcuse(), configuracion.maximoEnvios(),
                sustituto.organismo(), reloj).revisar(limite);
    }

    /** Apertura o cierre del cortacircuitos: estado_integracion y EV-23 en la misma transacción. */
    @TransactionAttribute(TransactionAttributeType.REQUIRES_NEW)
    public boolean registrarTransicion(UUID id, String nuevo, Instant instante, Instant abiertoDesde, long mensajesEnCola) {
        return new EstadoIntegracionSe8(new AlmacenJpa(em)).registrar(id, nuevo, instante, abiertoDesde, mensajesEnCola);
    }

    @TransactionAttribute(TransactionAttributeType.REQUIRES_NEW)
    public int numerarBandeja() {
        return relevo().numerar();
    }

    @TransactionAttribute(TransactionAttributeType.REQUIRES_NEW)
    public RelevoBandeja.Resultado publicarBandeja(RelevoBandeja.PublicadorBus publicador) {
        return relevo().publicar(publicador);
    }

    // ---- Consultas (red interna)

    @TransactionAttribute(TransactionAttributeType.SUPPORTS)
    public Optional<Entrega> entrega(String claveEnvio) {
        return new AlmacenJpa(em).buscarEntrega(claveEnvio);
    }

    @TransactionAttribute(TransactionAttributeType.SUPPORTS)
    public List<Entrega> entregas(String estado, String desastre, int limite) {
        StringBuilder jpql = new StringBuilder("SELECT e FROM Entrega e WHERE 1 = 1");
        if (estado != null) {
            jpql.append(" AND e.estado = :estado");
        }
        if (desastre != null) {
            jpql.append(" AND e.idDesastre = :desastre");
        }
        jpql.append(" ORDER BY e.creadaEn DESC");
        var consulta = em.createQuery(jpql.toString(), Entrega.class).setMaxResults(limite);
        if (estado != null) {
            consulta.setParameter("estado", Entrega.Estado.valueOf(estado));
        }
        if (desastre != null) {
            consulta.setParameter("desastre", UUID.fromString(desastre));
        }
        return consulta.getResultList();
    }

    @TransactionAttribute(TransactionAttributeType.SUPPORTS)
    public Map<String, Object> resumen() {
        Map<String, Object> resumen = new LinkedHashMap<>();
        Map<String, Long> porEstado = new LinkedHashMap<>();
        for (Entrega.Estado e : Entrega.Estado.values()) {
            porEstado.put(e.name(), 0L);
        }
        for (Object[] fila : em.createQuery("SELECT e.estado, count(e) FROM Entrega e GROUP BY e.estado", Object[].class)
                .getResultList()) {
            porEstado.put(((Entrega.Estado) fila[0]).name(), (Long) fila[1]);
        }
        resumen.put("entregasPorEstado", porEstado);
        resumen.put("enviosTotales", em.createQuery("SELECT coalesce(sum(e.envios), 0) FROM Entrega e", Long.class)
                .getSingleResult());
        resumen.put("mensajesProcesados", em.createQuery("SELECT count(m) FROM MensajeProcesado m", Long.class)
                .getSingleResult());
        resumen.put("bandejaPendiente", relevo().pendientes());
        resumen.put("ev23Publicados", ((Number) em.createNativeQuery(
                "SELECT count(*) FROM se8.bandeja_salida WHERE tipo = 'EV-23' AND publicado_en IS NOT NULL")
                .getSingleResult()).longValue());
        return resumen;
    }

    /** Sondeo de la base para /salud/listo. */
    @TransactionAttribute(TransactionAttributeType.SUPPORTS)
    public void verificarBase() {
        em.createNativeQuery("SELECT 1").getSingleResult();
    }

    private AplicadorAcuses aplicador() {
        return new AplicadorAcuses(new AlmacenJpa(em), adaptador, reloj);
    }

    private RelevoBandeja relevo() {
        return new RelevoBandeja(em, configuracion.productor(), configuracion.tamanoLote(), reloj);
    }
}
