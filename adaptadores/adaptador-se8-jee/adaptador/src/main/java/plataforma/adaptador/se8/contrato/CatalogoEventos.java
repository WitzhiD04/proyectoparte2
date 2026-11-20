package plataforma.adaptador.se8.contrato;

import java.util.List;
import java.util.Map;
import java.util.function.Function;
import java.util.stream.Collectors;

/**
 * Los eventos del catálogo de {@code Plataforma.Comun.Eventos.CatalogoEventos} que toca el adaptador de SE-8. No agrega
 * eventos: los tipos, nombres, versiones y productores son los mismos (E-15, E-37).
 */
public final class CatalogoEventos {

    public static final String EXCHANGE = "plataforma.eventos";
    public static final String ADAPTADOR_SE8 = "adaptador-se8";

    /** Un evento del catálogo. La clave de ruteo sigue la regla de comun: "ev" + número + "." + nombre. */
    public record Definicion(String tipo, String nombre, int version, String productor) {
        public String claveRuteo() {
            return "ev" + tipo.substring(3) + "." + nombre;
        }
    }

    public static final Definicion DESASTRE_CREADO = new Definicion("EV-03", "desastre.creado", 1, "gestion-desastres");
    public static final Definicion SEVERIDAD_CAMBIADA =
            new Definicion("EV-04", "desastre.severidad-cambiada", 1, "gestion-desastres");
    public static final Definicion ESTADO_INTEGRACION_CAMBIADO =
            new Definicion("EV-23", "integracion.estado-cambiado", 1, "adaptador-*");
    public static final Definicion ENVIO_NOTIFICACION_SOLICITADO =
            new Definicion("EV-28", "notificacion.envio-solicitado", 1, "notificacion");
    public static final Definicion ACUSE_NOTIFICACION =
            new Definicion("EV-29", "notificacion.acuse-recibido", 1, ADAPTADOR_SE8);

    public static final List<Definicion> TODOS = List.of(DESASTRE_CREADO, SEVERIDAD_CAMBIADA, ESTADO_INTEGRACION_CAMBIADO,
            ENVIO_NOTIFICACION_SOLICITADO, ACUSE_NOTIFICACION);

    private static final Map<String, Definicion> POR_TIPO =
            TODOS.stream().collect(Collectors.toUnmodifiableMap(Definicion::tipo, Function.identity()));

    private CatalogoEventos() {
    }

    /** La definición del tipo, o {@link EventoInvalidoException} si el adaptador no lo conoce. */
    public static Definicion porTipo(String tipo) {
        Definicion definicion = POR_TIPO.get(tipo);
        if (definicion == null) {
            throw new EventoInvalidoException("El evento " + tipo + " no está en el contrato del adaptador de SE-8.");
        }
        return definicion;
    }
}
