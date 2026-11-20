package plataforma.adaptador.se8.cliente;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;
import jakarta.ws.rs.ProcessingException;
import jakarta.ws.rs.client.Client;
import jakarta.ws.rs.client.ClientBuilder;
import jakarta.ws.rs.client.Entity;
import jakarta.ws.rs.client.WebTarget;
import jakarta.ws.rs.core.MediaType;
import jakarta.ws.rs.core.Response;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;
import plataforma.adaptador.se8.contrato.Sobre;
import plataforma.adaptador.se8.resiliencia.FalloTransitorioException;
import plataforma.adaptador.se8.traduccion.AcuseSe8;
import plataforma.adaptador.se8.traduccion.AlertaSe8;

/**
 * Transporte con el cliente de Jakarta REST (JAX-RS Client). El tiempo de cada intento se aplica con la llamada
 * asíncrona y {@code Future.get(tiempo)}: así vale el menor entre los 3 s del intento y lo que queda de los 20 s de la
 * operación. Los tiempos de conexión y de lectura del cliente quedan como respaldo.
 */
public final class TransporteJaxRs implements TransporteSe8, AutoCloseable {

    private final Client cliente;
    private final WebTarget base;

    public TransporteJaxRs(String url, Duration tiempoIntento) {
        long ms = Math.max(1, tiempoIntento.toMillis());
        this.cliente = ClientBuilder.newBuilder()
                .connectTimeout(ms, TimeUnit.MILLISECONDS)
                .readTimeout(ms, TimeUnit.MILLISECONDS)
                .build();
        this.base = cliente.target(url.endsWith("/") ? url : url + "/");
    }

    @Override
    public Respuesta enviar(AlertaSe8 alerta, Duration tiempoMaximo) throws FalloTransitorioException {
        String cuerpo;
        try {
            cuerpo = Sobre.JSON.writeValueAsString(alerta);
        } catch (JsonProcessingException e) {
            throw new IllegalStateException(e);
        }
        Future<Response> futuro = base.path("v1/alertas/{referencia}")
                .resolveTemplate("referencia", alerta.referenciaExterna())
                .request(MediaType.APPLICATION_JSON)
                .async()
                .put(Entity.entity(cuerpo, MediaType.APPLICATION_JSON));
        try (Response respuesta = esperar(futuro, tiempoMaximo)) {
            int estado = respuesta.getStatus();
            String texto = respuesta.hasEntity() ? respuesta.readEntity(String.class) : "";
            transitorio(estado, texto);
            return new Respuesta(estado, texto);
        }
    }

    @Override
    public void salud(Duration tiempoMaximo) throws FalloTransitorioException {
        Future<Response> futuro = base.path("v1/salud").request(MediaType.APPLICATION_JSON).async().get();
        try (Response respuesta = esperar(futuro, tiempoMaximo)) {
            transitorio(respuesta.getStatus(), "");
            if (respuesta.getStatus() >= 400) {
                throw new FalloTransitorioException("Sondeo de SE-8 con " + respuesta.getStatus());
            }
        }
    }

    @Override
    public PaginaAcuses acuses(long desde, int limite, Duration tiempoMaximo) throws FalloTransitorioException {
        Future<Response> futuro = base.path("v1/acuses").queryParam("desde", desde).queryParam("limite", limite)
                .request(MediaType.APPLICATION_JSON).async().get();
        try (Response respuesta = esperar(futuro, tiempoMaximo)) {
            String texto = respuesta.hasEntity() ? respuesta.readEntity(String.class) : "";
            transitorio(respuesta.getStatus(), texto);
            if (respuesta.getStatus() >= 400) {
                throw new FalloTransitorioException("Consulta de acuses con " + respuesta.getStatus());
            }
            JsonNode nodo = Sobre.JSON.readTree(texto);
            List<AcuseSe8> acuses = new ArrayList<>();
            for (JsonNode a : nodo.path("acuses")) {
                acuses.add(Sobre.JSON.treeToValue(a, AcuseSe8.class));
            }
            return new PaginaAcuses(acuses, nodo.path("hayMas").asBoolean(false));
        } catch (JsonProcessingException e) {
            throw new FalloTransitorioException("Respuesta de acuses ilegible: " + e.getMessage(), e);
        }
    }

    private static Response esperar(Future<Response> futuro, Duration tiempoMaximo) throws FalloTransitorioException {
        try {
            return futuro.get(Math.max(1, tiempoMaximo.toMillis()), TimeUnit.MILLISECONDS);
        } catch (TimeoutException e) {
            futuro.cancel(true);
            throw new FalloTransitorioException("SE-8 no respondió en " + tiempoMaximo.toMillis() + " ms", e);
        } catch (ExecutionException e) {
            Throwable causa = e.getCause();
            if (causa instanceof ProcessingException || causa instanceof java.io.IOException) {
                throw new FalloTransitorioException("Error de red con SE-8: " + causa.getMessage(), causa);
            }
            throw new FalloTransitorioException("Error con SE-8: " + causa, causa);
        } catch (InterruptedException e) {
            futuro.cancel(true);
            Thread.currentThread().interrupt();
            throw new FalloTransitorioException("Llamada a SE-8 interrumpida", e);
        }
    }

    /** 5xx, 408 y 429 son transitorios (Plataforma.Comun.Adaptadores.Indisponibilidad). */
    static void transitorio(int estado, String texto) throws FalloTransitorioException {
        if (estado >= 500 || estado == 408 || estado == 429) {
            throw new FalloTransitorioException("SE-8 respondió " + estado + (texto == null || texto.isBlank() ? "" : ": " + texto));
        }
    }

    @Override
    public void close() {
        cliente.close();
    }
}
