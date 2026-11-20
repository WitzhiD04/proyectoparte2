package plataforma.adaptador.se8.jee;

import jakarta.annotation.PostConstruct;
import jakarta.ejb.EJB;
import jakarta.ejb.Singleton;
import jakarta.ejb.Startup;
import jakarta.inject.Inject;
import java.time.Instant;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.logging.Level;
import java.util.logging.Logger;
import plataforma.adaptador.se8.consumo.ContadorCola;
import plataforma.adaptador.se8.resiliencia.Cortacircuitos;

/**
 * Publica EV-23 cuando el cortacircuitos de SE-8 se abre o se cierra (E-15, QA-01 b). La transición se guarda en
 * {@code estado_integracion} y el EV-23 sale por la bandeja de salida, en una transacción propia.
 */
@Singleton
@Startup
public class MonitorCircuitoSe8 {

    private static final Logger LOG = Logger.getLogger(MonitorCircuitoSe8.class.getName());

    @Inject
    Cortacircuitos circuito;
    @Inject
    ContadorCola contador;
    @EJB
    ManejadorSe8 manejador;

    private final AtomicBoolean conciliarPronto = new AtomicBoolean();

    @PostConstruct
    void registrar() {
        circuito.agregarObservador(new Cortacircuitos.Observador() {
            @Override
            public void alAbrirse(Instant instante) {
                anunciar("ABIERTO", instante, instante);
            }

            @Override
            public void alCerrarse(Instant instante, Instant abiertoDesde) {
                anunciar("CERRADO", instante, abiertoDesde);
                conciliarPronto.set(true); // al volver SE-8, se piden enseguida los acuses pendientes
            }
        });
    }

    /** Verdadero una vez después de cada cierre del circuito. */
    public boolean tomarConciliacionPendiente() {
        return conciliarPronto.getAndSet(false);
    }

    private void anunciar(String estado, Instant instante, Instant abiertoDesde) {
        long enCola = contador.contar();
        try {
            boolean nuevo = manejador.registrarTransicion(UUID.randomUUID(), estado, instante, abiertoDesde, enCola);
            if (nuevo) {
                LOG.warning("SE-8: circuito " + estado + " (" + enCola + " mensajes en q.salida.se8); EV-23 en la bandeja.");
            }
        } catch (RuntimeException e) {
            LOG.log(Level.SEVERE, "No se pudo registrar el EV-23 del circuito " + estado + ".", e);
        }
    }
}
