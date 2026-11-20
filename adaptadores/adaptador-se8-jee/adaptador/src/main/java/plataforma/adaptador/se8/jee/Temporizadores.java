package plataforma.adaptador.se8.jee;

import com.rabbitmq.jms.admin.RMQConnectionFactory;
import jakarta.annotation.PostConstruct;
import jakarta.annotation.PreDestroy;
import jakarta.annotation.Resource;
import jakarta.ejb.ConcurrencyManagement;
import jakarta.ejb.ConcurrencyManagementType;
import jakarta.ejb.EJB;
import jakarta.ejb.Schedule;
import jakarta.ejb.Singleton;
import jakarta.ejb.Startup;
import jakarta.ejb.Timeout;
import jakarta.ejb.Timer;
import jakarta.ejb.TimerConfig;
import jakarta.ejb.TimerService;
import jakarta.inject.Inject;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.logging.Level;
import java.util.logging.Logger;
import plataforma.adaptador.se8.configuracion.Configuracion;
import plataforma.adaptador.se8.consumo.PublicadorJms;
import plataforma.adaptador.se8.contrato.SistemaExternoNoDisponibleException;
import plataforma.adaptador.se8.nucleo.ReenvioSinAcuse;
import plataforma.adaptador.se8.nucleo.RelevoBandeja;

/**
 * Tareas periódicas con el EJB Timer Service:
 * <ul>
 * <li>Relevo de la bandeja de salida ({@code @Schedule} cada segundo, AD-45): numera y publica EV-23 y EV-29.</li>
 * <li>Reenvío por falta de acuse ({@code @Schedule} cada segundo; reenvía lo que lleva 5 s sin acuse, QA-08).</li>
 * <li>Conciliación de acuses desde la marca de avance (temporizador de intervalo, {@code Entrada__IntervaloSegundos}),
 *     y enseguida después de cada cierre del circuito.</li>
 * </ul>
 * Los temporizadores no son persistentes: si el proceso cae, vuelven a crearse al desplegar. Cada tarea se salta una
 * vuelta si la anterior sigue en curso.
 */
@Singleton
@Startup
@ConcurrencyManagement(ConcurrencyManagementType.BEAN)
public class Temporizadores {

    private static final Logger LOG = Logger.getLogger(Temporizadores.class.getName());

    @Resource
    TimerService servicio;
    @EJB
    ManejadorSe8 manejador;
    @EJB
    MonitorCircuitoSe8 monitor;
    @Inject
    RMQConnectionFactory fabrica;
    @Inject
    Configuracion configuracion;

    private final AtomicBoolean relevoEnCurso = new AtomicBoolean();
    private final AtomicBoolean reenvioEnCurso = new AtomicBoolean();
    private final AtomicBoolean conciliacionEnCurso = new AtomicBoolean();
    private PublicadorJms publicador;

    @PostConstruct
    void iniciar() {
        publicador = new PublicadorJms(fabrica);
        long intervalo = Math.max(1000, configuracion.intervaloEntrada().toMillis());
        servicio.createIntervalTimer(intervalo, intervalo, new TimerConfig("conciliacion", false));
    }

    @PreDestroy
    void detener() {
        if (publicador != null) {
            publicador.close();
        }
    }

    @Schedule(second = "*", minute = "*", hour = "*", persistent = false, info = "relevo")
    void relevo() {
        if (!relevoEnCurso.compareAndSet(false, true)) {
            return;
        }
        try {
            while (manejador.numerarBandeja() == configuracion.tamanoLote()) {
                // puede haber más: seguir numerando
            }
            RelevoBandeja.Resultado resultado = manejador.publicarBandeja(publicador);
            if (resultado.rechazado()) {
                LOG.warning("El bus no aceptó un evento (" + resultado.error() + "); queda en la bandeja y se reintenta.");
            }
            if (monitor.tomarConciliacionPendiente()) {
                conciliar();
            }
        } catch (RuntimeException e) {
            LOG.log(Level.WARNING, "El relevo no pudo avanzar; los eventos siguen en la bandeja.", e);
        } finally {
            relevoEnCurso.set(false);
        }
    }

    @Schedule(second = "*", minute = "*", hour = "*", persistent = false, info = "reenvio")
    void reenvio() {
        if (!reenvioEnCurso.compareAndSet(false, true)) {
            return;
        }
        try {
            ReenvioSinAcuse.Resumen r = manejador.revisarReenvios(20);
            if (r.reenviadas() + r.fallidas() > 0) {
                LOG.info("Reenvío por falta de acuse: " + r.reenviadas() + " reenviadas, " + r.pendientes()
                        + " pendientes, " + r.fallidas() + " fallidas.");
            }
        } catch (RuntimeException e) {
            LOG.log(Level.WARNING, "La revisión de reenvíos no pudo avanzar.", e);
        } finally {
            reenvioEnCurso.set(false);
        }
    }

    @Timeout
    void alVencer(Timer temporizador) {
        if ("conciliacion".equals(temporizador.getInfo())) {
            conciliar();
        }
    }

    private void conciliar() {
        if (!conciliacionEnCurso.compareAndSet(false, true)) {
            return;
        }
        try {
            int paginas = 0;
            while (manejador.conciliarPagina() && ++paginas < 20) {
                // SE-8 tiene más acuses: otra página
            }
        } catch (SistemaExternoNoDisponibleException e) {
            LOG.fine("Conciliación de acuses: SE-8 no responde; se intenta en la próxima vuelta.");
        } catch (RuntimeException e) {
            LOG.log(Level.WARNING, "La conciliación de acuses no pudo avanzar.", e);
        } finally {
            conciliacionEnCurso.set(false);
        }
    }
}
