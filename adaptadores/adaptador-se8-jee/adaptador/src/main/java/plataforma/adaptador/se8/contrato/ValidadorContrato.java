package plataforma.adaptador.se8.contrato;

import com.fasterxml.jackson.databind.JsonNode;
import com.networknt.schema.JsonSchema;
import com.networknt.schema.JsonSchemaFactory;
import com.networknt.schema.SchemaLocation;
import com.networknt.schema.SchemaValidatorsConfig;
import com.networknt.schema.SpecVersion;
import com.networknt.schema.ValidationMessage;
import java.io.IOException;
import java.io.InputStream;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import java.util.stream.Collectors;

/**
 * Valida un sobre y sus datos contra los esquemas JSON del contrato (DT-17), como {@code ValidadorContrato} de
 * {@code Plataforma.Comun.Mensajeria}: tipo del catálogo, nombre del tipo, sobre contra {@code sobre.v1.json} y datos
 * contra {@code EV-nn.v{version}.json}. Los formatos (uuid, date-time) se exigen.
 *
 * <p>Los esquemas no se copian a mano: el WAR los toma en la compilación de {@code comun/Plataforma.Comun.Eventos/Esquemas}
 * (una sola fuente). Se leen del classpath del despliegue y se registran por su {@code $id}.</p>
 */
public final class ValidadorContrato {

    public static final String PREFIJO = "https://plataforma.local/esquemas/";
    private static final List<String> NOMBRES = List.of("comun.v1.json", "sobre.v1.json", "EV-03.v1.json", "EV-04.v1.json",
            "EV-23.v1.json", "EV-28.v1.json", "EV-29.v1.json");

    private static final ValidadorContrato INSTANCIA = new ValidadorContrato();

    private final JsonSchemaFactory fabrica;
    private final SchemaValidatorsConfig configuracion;
    private final Map<String, JsonSchema> esquemas = new ConcurrentHashMap<>();

    public static ValidadorContrato instancia() {
        return INSTANCIA;
    }

    private ValidadorContrato() {
        Map<String, String> contenidos = new HashMap<>();
        for (String nombre : NOMBRES) {
            contenidos.put(PREFIJO + nombre, leerRecurso("esquemas/" + nombre));
        }
        fabrica = JsonSchemaFactory.getInstance(SpecVersion.VersionFlag.V202012,
                b -> b.schemaLoaders(l -> l.schemas(contenidos)));
        configuracion = SchemaValidatorsConfig.builder().formatAssertionsEnabled(true).build();
    }

    /** Lanza {@link EventoInvalidoException} si el sobre o sus datos no cumplen el contrato. */
    public void validar(Sobre sobre) {
        validar(sobre.aJson());
    }

    /** Valida el árbol JSON tal como llegó del bus. */
    public void validar(JsonNode arbol) {
        if (arbol == null || !arbol.isObject()) {
            throw new EventoInvalidoException("El mensaje no contiene un sobre.");
        }
        String tipo = arbol.path("tipo").asText("");
        CatalogoEventos.Definicion definicion = CatalogoEventos.porTipo(tipo);
        String nombre = arbol.path("nombre").asText("");
        if (!definicion.nombre().equals(nombre)) {
            throw new EventoInvalidoException("El evento " + tipo + " se llama " + definicion.nombre() + ", no " + nombre + ".");
        }
        exigir("sobre.v1.json", arbol, "sobre");
        String esquemaDatos = tipo + ".v" + arbol.path("version").asInt() + ".json";
        if (!NOMBRES.contains(esquemaDatos)) {
            throw new EventoInvalidoException("No existe el esquema " + esquemaDatos + ".");
        }
        exigir(esquemaDatos, arbol.get("datos"), "datos");
    }

    private void exigir(String nombre, JsonNode nodo, String parte) {
        JsonSchema esquema = esquemas.computeIfAbsent(nombre,
                n -> fabrica.getSchema(SchemaLocation.of(PREFIJO + n), configuracion));
        Set<ValidationMessage> errores = esquema.validate(nodo);
        if (!errores.isEmpty()) {
            String detalle = errores.stream().limit(5).map(ValidationMessage::getMessage).collect(Collectors.joining("; "));
            throw new EventoInvalidoException("El " + parte + " no cumple " + nombre + ": " + detalle);
        }
    }

    private static String leerRecurso(String ruta) {
        try (InputStream entrada = ValidadorContrato.class.getClassLoader().getResourceAsStream(ruta)) {
            if (entrada == null) {
                throw new IllegalStateException("Falta el esquema " + ruta + " en el despliegue.");
            }
            return new String(entrada.readAllBytes(), StandardCharsets.UTF_8);
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }
}
