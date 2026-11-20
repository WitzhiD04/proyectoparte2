package plataforma.adaptador.se8.api;

import jakarta.ejb.EJB;
import jakarta.ws.rs.GET;
import jakarta.ws.rs.Path;
import jakarta.ws.rs.Produces;
import jakarta.ws.rs.core.MediaType;
import jakarta.ws.rs.core.Response;
import java.util.LinkedHashMap;
import java.util.Map;
import plataforma.adaptador.se8.consumo.BucleConsumo;
import plataforma.adaptador.se8.jee.ConsumidorSe8;
import plataforma.adaptador.se8.jee.ManejadorSe8;

/**
 * Sondeos del orquestador, con las rutas y la forma de respuesta de {@code Plataforma.Comun.Salud}: {@code /salud/vivo}
 * (el proceso responde) y {@code /salud/listo} (la base responde y el consumidor está en marcha).
 */
@Path("salud")
@Produces(MediaType.APPLICATION_JSON)
public class RecursoSalud {

    @EJB
    ManejadorSe8 manejador;
    @EJB
    ConsumidorSe8 consumidor;

    @GET
    @Path("vivo")
    public Map<String, Object> vivo() {
        Map<String, Object> cuerpo = new LinkedHashMap<>();
        cuerpo.put("servicio", "adaptador-se8");
        cuerpo.put("rol", "salida");
        cuerpo.put("estado", "Healthy");
        return cuerpo;
    }

    @GET
    @Path("listo")
    public Response listo() {
        Map<String, Object> verificaciones = new LinkedHashMap<>();
        boolean sano = true;
        try {
            manejador.verificarBase();
            verificaciones.put("base", Map.of("estado", "Healthy"));
        } catch (RuntimeException e) {
            sano = false;
            verificaciones.put("base", Map.of("estado", "Unhealthy", "detalle", String.valueOf(e.getMessage())));
        }
        BucleConsumo bucle = consumidor.bucle();
        boolean enMarcha = bucle != null && bucle.activo();
        sano &= enMarcha;
        verificaciones.put("consumidor", Map.of("estado", enMarcha ? "Healthy" : "Unhealthy"));
        Map<String, Object> cuerpo = new LinkedHashMap<>();
        cuerpo.put("servicio", "adaptador-se8");
        cuerpo.put("rol", "salida");
        cuerpo.put("estado", sano ? "Healthy" : "Unhealthy");
        cuerpo.put("verificaciones", verificaciones);
        return Response.status(sano ? 200 : 503).entity(cuerpo).build();
    }
}
