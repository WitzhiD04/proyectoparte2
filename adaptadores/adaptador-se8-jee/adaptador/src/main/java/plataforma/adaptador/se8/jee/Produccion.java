package plataforma.adaptador.se8.jee;

import com.rabbitmq.jms.admin.RMQConnectionFactory;
import jakarta.enterprise.context.ApplicationScoped;
import jakarta.enterprise.inject.Disposes;
import jakarta.enterprise.inject.Produces;
import jakarta.inject.Singleton;
import jakarta.jms.JMSException;
import java.time.Clock;
import org.eclipse.microprofile.config.Config;
import org.eclipse.microprofile.config.ConfigProvider;
import plataforma.adaptador.se8.cliente.ClienteSe8;
import plataforma.adaptador.se8.cliente.TransporteJaxRs;
import plataforma.adaptador.se8.configuracion.Configuracion;
import plataforma.adaptador.se8.consumo.ConexionesBus;
import plataforma.adaptador.se8.consumo.ContadorCola;
import plataforma.adaptador.se8.resiliencia.Cortacircuitos;
import plataforma.adaptador.se8.resiliencia.EjecutorResiliente;
import plataforma.adaptador.se8.resiliencia.OpcionesResiliencia;
import plataforma.adaptador.se8.traduccion.OpcionesSustituto;

/**
 * Productores CDI de las piezas sin estado de contenedor. Un solo cortacircuitos por proceso: la entrega desde la cola,
 * el reenvío y la conciliación comparten su estado, como el pipeline con nombre de los adaptadores .NET.
 */
@ApplicationScoped
public class Produccion {

    @Produces
    @Singleton
    Configuracion configuracion() {
        Config config = ConfigProvider.getConfig();
        return new Configuracion(nombre -> config.getOptionalValue(nombre, String.class));
    }

    @Produces
    @Singleton
    Clock reloj() {
        return Clock.systemUTC();
    }

    @Produces
    @Singleton
    OpcionesSustituto sustituto(Configuracion configuracion) {
        return configuracion.sustituto();
    }

    @Produces
    @Singleton
    Cortacircuitos cortacircuitos(Configuracion configuracion, Clock reloj) {
        return new Cortacircuitos(configuracion.resiliencia(), reloj);
    }

    @Produces
    @Singleton
    TransporteJaxRs transporte(Configuracion configuracion) {
        return new TransporteJaxRs(configuracion.urlSe8(), configuracion.resiliencia().porIntento());
    }

    void cerrar(@Disposes TransporteJaxRs transporte) {
        transporte.close();
    }

    @Produces
    @Singleton
    ClienteSe8 cliente(Configuracion configuracion, TransporteJaxRs transporte, Cortacircuitos circuito) {
        OpcionesResiliencia opciones = configuracion.resiliencia();
        return new ClienteSe8(transporte, EjecutorResiliente.sistema(opciones, circuito));
    }

    @Produces
    @Singleton
    RMQConnectionFactory fabricaBus(Configuracion c) throws JMSException {
        return ConexionesBus.fabrica(c.nodosBus(), c.usuarioBus(), c.claveBus(), c.virtualHost());
    }

    @Produces
    @Singleton
    ContadorCola contadorCola(Configuracion c) {
        return new ContadorCola(c.nodosBus(), c.usuarioBus(), c.claveBus(), c.virtualHost(), c.cola());
    }
}
