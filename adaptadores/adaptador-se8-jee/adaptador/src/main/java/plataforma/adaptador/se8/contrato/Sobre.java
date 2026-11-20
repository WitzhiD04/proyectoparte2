package plataforma.adaptador.se8.contrato;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.DeserializationFeature;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.JsonNodeFactory;
import com.fasterxml.jackson.databind.node.ObjectNode;
import java.io.IOException;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.util.Objects;
import java.util.UUID;

/**
 * Sobre canónico de todos los eventos del bus (SAD §5.2, AD-09). Equivalente Java de {@code Plataforma.Comun.Eventos.Sobre}:
 * los mismos once campos, en el mismo orden y con los mismos nombres JSON (camelCase).
 *
 * @param idEvento identificador único del evento
 * @param tipo identificador del catálogo, por ejemplo "EV-03"
 * @param nombre nombre del evento, por ejemplo "desastre.creado"
 * @param version versión del esquema de los datos
 * @param instanteEmision instante en que el productor registró el hecho, en UTC
 * @param productor microservicio o adaptador que emite el evento
 * @param secuenciaProductor consecutivo sin huecos por productor; lo asigna el relevo al publicar
 * @param idCorrelacion identificador del flujo que originó el evento
 * @param claveIdempotencia clave con la que un consumidor descarta un evento repetido (AD-02, AD-14)
 * @param procedencia fuente del dato y su confianza (AD-11)
 * @param datos contenido del evento, según el esquema del tipo y la versión
 */
public record Sobre(
        UUID idEvento,
        String tipo,
        String nombre,
        int version,
        Instant instanteEmision,
        String productor,
        Long secuenciaProductor,
        String idCorrelacion,
        String claveIdempotencia,
        Procedencia procedencia,
        JsonNode datos) {

    /** Mapeador del contrato. Lo comparten la lectura, la escritura y la validación. */
    public static final ObjectMapper JSON = new ObjectMapper()
            .configure(DeserializationFeature.FAIL_ON_UNKNOWN_PROPERTIES, false);

    public Sobre {
        Objects.requireNonNull(idEvento, "idEvento");
        Objects.requireNonNull(tipo, "tipo");
        Objects.requireNonNull(claveIdempotencia, "claveIdempotencia");
        Objects.requireNonNull(procedencia, "procedencia");
        Objects.requireNonNull(datos, "datos");
    }

    /** Arma un sobre para un evento del catálogo. La secuencia del productor la asigna el relevo al publicar. */
    public static Sobre crear(CatalogoEventos.Definicion definicion, String productor, String claveIdempotencia,
            Object datos, Instant instanteEmision, Procedencia procedencia, String idCorrelacion) {
        if (productor == null || productor.isBlank()) {
            throw new IllegalArgumentException("Falta el productor.");
        }
        if (claveIdempotencia == null || claveIdempotencia.isBlank()) {
            throw new IllegalArgumentException("Falta la clave de idempotencia.");
        }
        return new Sobre(UUID.randomUUID(), definicion.tipo(), definicion.nombre(), definicion.version(), instanteEmision,
                productor, null, idCorrelacion, claveIdempotencia, procedencia == null ? Procedencia.PLATAFORMA : procedencia,
                JSON.valueToTree(datos));
    }

    /** El mismo sobre con la secuencia que asignó el relevo. */
    public Sobre conSecuencia(long secuencia) {
        return new Sobre(idEvento, tipo, nombre, version, instanteEmision, productor, secuencia, idCorrelacion,
                claveIdempotencia, procedencia, datos);
    }

    /** El sobre como árbol JSON, con los campos en el orden del contrato. Es lo que se valida y lo que se publica. */
    public ObjectNode aJson() {
        ObjectNode nodo = JsonNodeFactory.instance.objectNode();
        nodo.put("idEvento", idEvento.toString());
        nodo.put("tipo", tipo);
        nodo.put("nombre", nombre);
        nodo.put("version", version);
        nodo.put("instanteEmision", instanteEmision == null ? null : instanteEmision.toString());
        nodo.put("productor", productor);
        if (secuenciaProductor == null) {
            nodo.putNull("secuenciaProductor");
        } else {
            nodo.put("secuenciaProductor", secuenciaProductor);
        }
        nodo.put("idCorrelacion", idCorrelacion);
        nodo.put("claveIdempotencia", claveIdempotencia);
        ObjectNode proc = nodo.putObject("procedencia");
        proc.put("fuente", procedencia.fuente());
        proc.put("sistemaOrigen", procedencia.sistemaOrigen());
        proc.put("confianza", procedencia.confianza());
        nodo.set("datos", datos);
        return nodo;
    }

    public String serializar() {
        try {
            return JSON.writeValueAsString(aJson());
        } catch (JsonProcessingException e) {
            throw new IllegalStateException("No se pudo serializar el sobre.", e);
        }
    }

    /** Lee un sobre del cuerpo de un mensaje. Lanza {@link EventoInvalidoException} si no es un sobre. */
    public static Sobre leer(byte[] cuerpo) {
        JsonNode nodo;
        try {
            nodo = JSON.readTree(cuerpo);
        } catch (IOException e) {
            throw new EventoInvalidoException("El mensaje no es JSON: " + e.getMessage(), e);
        }
        return deJson(nodo);
    }

    public static Sobre leer(String json) {
        try {
            return deJson(JSON.readTree(json));
        } catch (JsonProcessingException e) {
            throw new EventoInvalidoException("El mensaje no es JSON: " + e.getMessage(), e);
        }
    }

    /** Lee el sobre sin validarlo contra el esquema (eso lo hace {@link ValidadorContrato}). */
    public static Sobre deJson(JsonNode nodo) {
        if (nodo == null || !nodo.isObject()) {
            throw new EventoInvalidoException("El mensaje no contiene un sobre.");
        }
        try {
            JsonNode proc = nodo.path("procedencia");
            return new Sobre(
                    UUID.fromString(texto(nodo, "idEvento")),
                    texto(nodo, "tipo"),
                    texto(nodo, "nombre"),
                    nodo.path("version").asInt(),
                    nodo.hasNonNull("instanteEmision") ? OffsetDateTime.parse(nodo.get("instanteEmision").asText()).toInstant() : null,
                    texto(nodo, "productor"),
                    nodo.hasNonNull("secuenciaProductor") ? nodo.get("secuenciaProductor").asLong() : null,
                    nodo.hasNonNull("idCorrelacion") ? nodo.get("idCorrelacion").asText() : null,
                    texto(nodo, "claveIdempotencia"),
                    new Procedencia(proc.path("fuente").asText(null),
                            proc.hasNonNull("sistemaOrigen") ? proc.get("sistemaOrigen").asText() : null,
                            proc.path("confianza").asText(null)),
                    nodo.has("datos") ? nodo.get("datos") : JsonNodeFactory.instance.nullNode());
        } catch (RuntimeException e) {
            throw new EventoInvalidoException("El sobre no tiene la forma del contrato: " + e.getMessage(), e);
        }
    }

    /** El árbol JSON original del mensaje, para validarlo tal como llegó. */
    public static JsonNode arbol(byte[] cuerpo) {
        try {
            return JSON.readTree(cuerpo);
        } catch (IOException e) {
            throw new EventoInvalidoException("El mensaje no es JSON: " + e.getMessage(), e);
        }
    }

    private static String texto(JsonNode nodo, String campo) {
        JsonNode valor = nodo.get(campo);
        if (valor == null || valor.isNull()) {
            throw new IllegalArgumentException("falta " + campo);
        }
        return valor.asText();
    }
}
