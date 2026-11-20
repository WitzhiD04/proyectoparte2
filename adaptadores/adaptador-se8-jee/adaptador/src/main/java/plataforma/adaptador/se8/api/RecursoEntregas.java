package plataforma.adaptador.se8.api;

import jakarta.ejb.EJB;
import jakarta.inject.Inject;
import jakarta.ws.rs.DefaultValue;
import jakarta.ws.rs.GET;
import jakarta.ws.rs.Path;
import jakarta.ws.rs.PathParam;
import jakarta.ws.rs.Produces;
import jakarta.ws.rs.QueryParam;
import jakarta.ws.rs.core.MediaType;
import jakarta.ws.rs.core.Response;
import java.time.Instant;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import plataforma.adaptador.se8.consumo.BucleConsumo;
import plataforma.adaptador.se8.jee.ConsumidorSe8;
import plataforma.adaptador.se8.jee.ManejadorSe8;
import plataforma.adaptador.se8.persistencia.Entrega;
import plataforma.adaptador.se8.resiliencia.Cortacircuitos;

/**
 * Consulta interna del estado de entrega (QA-08: "100 % consultable por destinatario") y de la integración. Mientras
 * Notificación no exista, es la única vista del estado de las alertas del modo sustituto (L-11).
 */
@Path("v1")
@Produces(MediaType.APPLICATION_JSON)
public class RecursoEntregas {

    @EJB
    ManejadorSe8 manejador;
    @EJB
    ConsumidorSe8 consumidor;
    @Inject
    Cortacircuitos circuito;

    @GET
    @Path("entregas")
    public Response entregas(@QueryParam("estado") String estado, @QueryParam("desastre") String desastre,
            @QueryParam("limite") @DefaultValue("100") int limite) {
        try {
            List<Map<String, Object>> filas = manejador.entregas(estado, desastre, Math.min(Math.max(limite, 1), 1000))
                    .stream().map(RecursoEntregas::vista).toList();
            return Response.ok(Map.of("datoAl", Instant.now().toString(), "entregas", filas)).build();
        } catch (IllegalArgumentException e) {
            return Response.status(400).entity(Map.of("error", "Filtro no válido: " + e.getMessage())).build();
        }
    }

    @GET
    @Path("entregas/{clave}")
    public Response entrega(@PathParam("clave") String clave) {
        return manejador.entrega(clave)
                .map(e -> Response.ok(vista(e)).build())
                .orElseGet(() -> Response.status(404).entity(Map.of("error", "No existe la entrega " + clave)).build());
    }

    @GET
    @Path("integracion")
    public Map<String, Object> integracion() {
        Map<String, Object> cuerpo = new LinkedHashMap<>();
        cuerpo.put("datoAl", Instant.now().toString());
        cuerpo.put("sistemaExterno", "SE-8");
        Cortacircuitos.Estado estado = circuito.estado();
        cuerpo.put("circuito", estado.name());
        cuerpo.put("abiertoDesde", circuito.abiertoDesde() == null ? null : circuito.abiertoDesde().toString());
        BucleConsumo bucle = consumidor.bucle();
        if (bucle != null) {
            cuerpo.put("consumiendo", bucle.consumiendo());
            cuerpo.put("mensajesConfirmados", bucle.confirmados());
            cuerpo.put("mensajesApartadosDlq", bucle.apartados());
            cuerpo.put("mensajesReencolados", bucle.reencolados());
        }
        cuerpo.putAll(manejador.resumen());
        return cuerpo;
    }

    static Map<String, Object> vista(Entrega e) {
        Map<String, Object> v = new LinkedHashMap<>();
        v.put("claveEnvio", e.getClaveEnvio());
        v.put("estado", e.getEstado().name());
        v.put("tipoOrigen", e.getTipoOrigen());
        v.put("claveIdempotencia", e.getClaveIdempotencia());
        v.put("idNotificacion", e.getIdNotificacion() == null ? null : e.getIdNotificacion().toString());
        v.put("intento", e.getIntento());
        v.put("idDesastre", e.getIdDesastre().toString());
        v.put("canal", e.getCanal());
        v.put("destino", e.getDestino());
        v.put("asunto", e.getAsunto());
        v.put("severidad", e.getSeveridad());
        v.put("envios", e.getEnvios());
        v.put("creadaEn", texto(e.getCreadaEn()));
        v.put("ultimoEnvioEn", texto(e.getUltimoEnvioEn()));
        v.put("acusadaEn", texto(e.getAcusadaEn()));
        v.put("motivo", e.getMotivo());
        return v;
    }

    private static String texto(Instant instante) {
        return instante == null ? null : instante.toString();
    }
}
