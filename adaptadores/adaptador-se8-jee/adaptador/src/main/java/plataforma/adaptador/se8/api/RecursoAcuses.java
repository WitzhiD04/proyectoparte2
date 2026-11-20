package plataforma.adaptador.se8.api;

import com.fasterxml.jackson.core.JsonProcessingException;
import jakarta.ejb.EJB;
import jakarta.ws.rs.Consumes;
import jakarta.ws.rs.POST;
import jakarta.ws.rs.Path;
import jakarta.ws.rs.Produces;
import jakarta.ws.rs.core.MediaType;
import jakarta.ws.rs.core.Response;
import java.util.Map;
import plataforma.adaptador.se8.contrato.Sobre;
import plataforma.adaptador.se8.jee.ManejadorSe8;
import plataforma.adaptador.se8.nucleo.AplicadorAcuses;
import plataforma.adaptador.se8.traduccion.AcuseSe8;

/**
 * Aviso de acuse de SE-8: {@code POST /v1/acuses}. Ruta solo de la red interna del compose (y del clúster): el API
 * Gateway no la enruta. Es idempotente: el mismo acuse dos veces no cambia nada.
 * <ul>
 * <li>200: acuse aplicado o repetido.</li>
 * <li>404: la alerta no se conoce todavía (su transacción no ha confirmado): SE-8 reintenta el aviso y la conciliación
 *     por marca de avance lo recoge.</li>
 * <li>400: el cuerpo no es un acuse.</li>
 * </ul>
 */
@Path("v1/acuses")
public class RecursoAcuses {

    @EJB
    ManejadorSe8 manejador;

    @POST
    @Consumes(MediaType.APPLICATION_JSON)
    @Produces(MediaType.APPLICATION_JSON)
    public Response recibir(String cuerpo) {
        AcuseSe8 acuse;
        try {
            acuse = Sobre.JSON.readValue(cuerpo, AcuseSe8.class);
        } catch (JsonProcessingException e) {
            return Response.status(400).entity(Map.of("error", "El cuerpo no es un acuse de SE-8.")).build();
        }
        if (acuse.referenciaExterna() == null || acuse.resultado() == null) {
            return Response.status(400).entity(Map.of("error", "Faltan referenciaExterna o resultado.")).build();
        }
        AplicadorAcuses.Resultado resultado = manejador.aplicarAcuse(acuse);
        int estado = resultado == AplicadorAcuses.Resultado.DESCONOCIDO ? 404 : 200;
        return Response.status(estado).entity(Map.of("resultado", resultado.name())).build();
    }
}
