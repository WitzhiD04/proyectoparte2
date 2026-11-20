package plataforma.adaptador.se8.configuracion;

import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import java.util.function.Function;
import plataforma.adaptador.se8.contrato.CatalogoEventos;
import plataforma.adaptador.se8.resiliencia.OpcionesResiliencia;
import plataforma.adaptador.se8.traduccion.OpcionesSustituto;

/**
 * Configuración del adaptador. Los nombres son los de los adaptadores .NET (variables de entorno con doble guion bajo):
 * {@code Bus__Nodos__0}, {@code Bus__Clave}, {@code Se8__Url}, {@code Resiliencia__TiempoEsperaIntentoSegundos}... En el
 * servidor se leen con MicroProfile Config, que busca primero la variable de entorno con el nombre exacto.
 */
public final class Configuracion {

    public static final String COLA_SALIDA = "q.salida.se8";

    private final Function<String, Optional<String>> fuente;

    public Configuracion(Function<String, Optional<String>> fuente) {
        this.fuente = fuente;
    }

    public String texto(String nombre, String porOmision) {
        return fuente.apply(nombre).filter(v -> !v.isBlank()).orElse(porOmision);
    }

    public int entero(String nombre, int porOmision) {
        return fuente.apply(nombre).filter(v -> !v.isBlank()).map(String::trim).map(Integer::parseInt).orElse(porOmision);
    }

    public double decimal(String nombre, double porOmision) {
        return fuente.apply(nombre).filter(v -> !v.isBlank()).map(v -> Double.parseDouble(v.trim().replace(',', '.')))
                .orElse(porOmision);
    }

    public boolean logico(String nombre, boolean porOmision) {
        return fuente.apply(nombre).filter(v -> !v.isBlank()).map(v -> Boolean.parseBoolean(v.trim())).orElse(porOmision);
    }

    // ---- Bus (sección "Bus" de los servicios .NET)

    public List<String> nodosBus() {
        List<String> nodos = new ArrayList<>();
        for (int i = 0; i < 16; i++) {
            Optional<String> nodo = fuente.apply("Bus__Nodos__" + i).filter(v -> !v.isBlank());
            if (nodo.isEmpty()) {
                break;
            }
            nodos.add(nodo.get().trim());
        }
        return nodos;
    }

    public String usuarioBus() {
        return texto("Bus__Usuario", CatalogoEventos.ADAPTADOR_SE8);
    }

    public String claveBus() {
        return texto("Bus__Clave", "adaptador_se8_local");
    }

    public String virtualHost() {
        return texto("Bus__VirtualHost", "/");
    }

    public String productor() {
        return texto("Bus__Productor", CatalogoEventos.ADAPTADOR_SE8);
    }

    public String cola() {
        return texto("Bus__Cola", COLA_SALIDA);
    }

    /** Intentos antes de apartar un mensaje a la DLQ. Igual que el delivery-limit de la política (5). */
    public int limiteEntregas() {
        return entero("Bus__LimiteEntregas", 5);
    }

    public int tamanoLote() {
        return entero("Bus__TamanoLote", 50);
    }

    // ---- SE-8

    public String urlSe8() {
        return texto("Se8__Url", "http://sim-se8:8080/");
    }

    public OpcionesResiliencia resiliencia() {
        OpcionesResiliencia c = OpcionesResiliencia.CUADRO_10;
        return new OpcionesResiliencia(
                decimal("Resiliencia__TiempoEsperaIntentoSegundos", c.tiempoEsperaIntentoSegundos()),
                decimal("Resiliencia__TiempoEsperaTotalSegundos", c.tiempoEsperaTotalSegundos()),
                entero("Resiliencia__Reintentos", c.reintentos()),
                decimal("Resiliencia__EsperaBaseReintentoSegundos", c.esperaBaseReintentoSegundos()),
                entero("Resiliencia__MinimoLlamadas", c.minimoLlamadas()),
                decimal("Resiliencia__ProporcionFallos", c.proporcionFallos()),
                decimal("Resiliencia__VentanaSegundos", c.ventanaSegundos()),
                decimal("Resiliencia__DuracionAperturaSegundos", c.duracionAperturaSegundos()));
    }

    /** Modo sustituto de Notificación (L-11). Encendido por omisión. */
    public OpcionesSustituto sustituto() {
        OpcionesSustituto d = OpcionesSustituto.POR_OMISION;
        return new OpcionesSustituto(logico("Sustituto__Activo", d.activo()), texto("Sustituto__Canal", d.canal()),
                texto("Sustituto__Destino", d.destino()), texto("Sustituto__Organismo", d.organismo()));
    }

    // ---- Salida (sección "Salida" de los adaptadores .NET)

    public Duration esperaReintento() {
        return segundos(decimal("Salida__EsperaReintentoSegundos", 2));
    }

    public Duration intervaloSondeo() {
        return segundos(decimal("Salida__IntervaloSondeoSegundos", 5));
    }

    // ---- Reenvío por falta de acuse (QA-08)

    public Duration sinAcuse() {
        return segundos(decimal("Reenvio__SegundosSinAcuse", 5));
    }

    public int maximoEnvios() {
        return entero("Reenvio__MaximoEnvios", 5);
    }

    // ---- Entrada: conciliación de acuses desde la marca de avance

    public Duration intervaloEntrada() {
        return segundos(decimal("Entrada__IntervaloSegundos", 30));
    }

    public int tamanoPagina() {
        return entero("Entrada__TamanoPagina", 100);
    }

    private static Duration segundos(double s) {
        return Duration.ofMillis(Math.round(s * 1000));
    }
}
