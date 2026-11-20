package plataforma.adaptador.se8.jee;

import com.rabbitmq.jms.admin.RMQConnectionFactory;
import jakarta.annotation.PostConstruct;
import jakarta.annotation.PreDestroy;
import jakarta.annotation.Resource;
import jakarta.ejb.DependsOn;
import jakarta.ejb.EJB;
import jakarta.ejb.Singleton;
import jakarta.ejb.Startup;
import jakarta.enterprise.concurrent.ManagedExecutorService;
import jakarta.inject.Inject;
import java.time.Duration;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import java.util.logging.Logger;
import plataforma.adaptador.se8.cliente.ClienteSe8;
import plataforma.adaptador.se8.configuracion.Configuracion;
import plataforma.adaptador.se8.consumo.BucleConsumo;
import plataforma.adaptador.se8.consumo.ConexionesBus;
import plataforma.adaptador.se8.contrato.SistemaExternoNoDisponibleException;
import plataforma.adaptador.se8.resiliencia.Cortacircuitos;

/**
 * Arranca el consumo de {@code q.salida.se8} al desplegar. Es un EJB {@code @Singleton @Startup} que corre el bucle de
 * consumo en un hilo del {@code ManagedExecutorService} (Jakarta Concurrency).
 *
 * <p>Por qué no un MDB (AD-66, compromiso aceptado): un MDB necesita un resource adapter JCA para el proveedor de
 * mensajería, y RabbitMQ no tiene uno mantenido. El cliente JMS de RabbitMQ sí implementa Jakarta Messaging 3.1, así que
 * el consumo usa la API estándar ({@code Connection}, {@code Session} transaccional, {@code MessageConsumer}); lo único
 * propio es quién arranca el hilo.</p>
 */
@Singleton
@Startup
@DependsOn("MonitorCircuitoSe8")
public class ConsumidorSe8 {

    private static final Logger LOG = Logger.getLogger(ConsumidorSe8.class.getName());

    @Resource
    ManagedExecutorService ejecutor;
    @EJB
    ManejadorSe8 manejador;
    @Inject
    RMQConnectionFactory fabrica;
    @Inject
    Configuracion configuracion;
    @Inject
    ClienteSe8 cliente;
    @Inject
    Cortacircuitos circuito;

    private BucleConsumo bucle;
    private Future<?> tarea;

    @PostConstruct
    void iniciar() {
        BucleConsumo.Compuerta compuerta = new BucleConsumo.Compuerta() {
            @Override
            public boolean abierta() {
                return circuito.abierto();
            }

            @Override
            public void sondear() {
                try {
                    cliente.sondear();
                } catch (SistemaExternoNoDisponibleException e) {
                    // sigue sin responder
                }
            }
        };
        BucleConsumo.Opciones opciones = new BucleConsumo.Opciones(configuracion.cola(), configuracion.limiteEntregas(),
                configuracion.esperaReintento(), configuracion.intervaloSondeo(), Duration.ofSeconds(1));
        bucle = new BucleConsumo(fabrica, ConexionesBus.cola(configuracion.cola()), sobre -> manejador.procesar(sobre),
                manejador::registrarFallo, compuerta, opciones);
        tarea = ejecutor.submit(bucle);
        LOG.info("Adaptador de SE-8: consumo de " + configuracion.cola() + " en marcha.");
    }

    @PreDestroy
    void detener() {
        if (bucle != null) {
            bucle.detener();
        }
        if (tarea != null) {
            try {
                tarea.get(10, TimeUnit.SECONDS);
            } catch (Exception e) {
                tarea.cancel(true);
            }
        }
    }

    public BucleConsumo bucle() {
        return bucle;
    }
}
