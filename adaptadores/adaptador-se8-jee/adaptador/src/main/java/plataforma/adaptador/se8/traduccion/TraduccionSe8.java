package plataforma.adaptador.se8.traduccion;

import com.fasterxml.jackson.databind.JsonNode;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import plataforma.adaptador.se8.contrato.CatalogoEventos;
import plataforma.adaptador.se8.contrato.EventoInvalidoException;
import plataforma.adaptador.se8.contrato.Procedencia;
import plataforma.adaptador.se8.contrato.Sobre;

/**
 * Traducción entre el contrato de la plataforma y el formato de SE-8 (AD-03, AD-09). Sin base, bus ni HTTP.
 * <ul>
 * <li>EV-28 (envío solicitado por Notificación): una alerta por destinatario e intento. Clave hacia SE-8:
 *     {@code notificacion:<idNotificacion>:<intento>}.</li>
 * <li>EV-03 y EV-04, solo en modo sustituto (L-11): una alerta al destino de difusión. El mensaje nombra el ámbito de
 *     coordinación del nivel (SRS Cuadro 18). Clave hacia SE-8: {@code sustituto:<claveIdempotencia del evento>}.</li>
 * <li>Acuse de SE-8 de una alerta de EV-28: EV-29 con ENTREGADA o FALLIDA. Los acuses del modo sustituto no producen
 *     EV-29 (no hay Notificación que lo consuma); su estado queda en la base del adaptador.</li>
 * </ul>
 */
public final class TraduccionSe8 {

    public static final String SISTEMA = "SE-8";
    public static final String PREFIJO_NOTIFICACION = "notificacion:";
    public static final String PREFIJO_SUSTITUTO = "sustituto:";

    /** Ámbito de coordinación por nivel de severidad (SRS §4.3, RN-3, Cuadro 18). Cada nivel incluye los anteriores. */
    private static final Map<String, List<String>> AMBITO = new LinkedHashMap<>();

    static {
        AMBITO.put("NIVEL_1", List.of("autoridad local"));
        AMBITO.put("NIVEL_2", List.of("autoridad local", "organismos de respuesta"));
        AMBITO.put("NIVEL_3", List.of("autoridad local", "organismos de respuesta", "autoridad regional y nacional"));
        AMBITO.put("NIVEL_4", List.of("autoridad local", "organismos de respuesta", "autoridad regional y nacional",
                "asistencia internacional"));
    }

    private TraduccionSe8() {
    }

    /** La alerta para un evento de la cola de salida, o nula si el evento no se le envía a SE-8. */
    public static AlertaSe8 aSe8(Sobre sobre, OpcionesSustituto sustituto) {
        JsonNode d = sobre.datos();
        switch (sobre.tipo()) {
            case "EV-28": {
                String clave = PREFIJO_NOTIFICACION + texto(d, "idNotificacion") + ":" + d.path("intento").asInt();
                return new AlertaSe8(clave, medio(texto(d, "canal")), texto(d, "destino"), texto(d, "asunto"),
                        texto(d, "mensaje"), prioridad(texto(d, "severidad")), texto(d, "idDesastre"), texto(d, "organismo"));
            }
            case "EV-03": {
                if (!sustituto.activo()) {
                    return null;
                }
                String nivel = texto(d, "severidad");
                String titulo = "Desastre registrado: " + texto(d, "nombre") + " (" + texto(d, "tipo") + ", " + nivel + ")";
                JsonNode ubicacion = d.path("ubicacion");
                String cuerpo = texto(d, "descripcion") + ". Registra: " + texto(d, "organismoRegistra")
                        + ". Inicio: " + texto(d, "instanteInicio")
                        + ". Ubicación: " + ubicacion.path("lat").asText() + ", " + ubicacion.path("lon").asText()
                        + ". Ámbito de coordinación: " + ambito(nivel) + ".";
                return new AlertaSe8(PREFIJO_SUSTITUTO + sobre.claveIdempotencia(), medio(sustituto.canal()),
                        sustituto.destino(), titulo, cuerpo, prioridad(nivel), texto(d, "idDesastre"), sustituto.organismo());
            }
            case "EV-04": {
                if (!sustituto.activo()) {
                    return null;
                }
                String anterior = texto(d, "severidadAnterior");
                String nueva = texto(d, "severidadNueva");
                String titulo = "Cambio de nivel del desastre: " + anterior + " a " + nueva;
                StringBuilder cuerpo = new StringBuilder("Motivo: ").append(texto(d, "motivo"))
                        .append(". Autor: ").append(texto(d, "autor"))
                        .append(". Ámbito de coordinación: ").append(ambito(nueva)).append('.');
                List<String> incorporados = incorporados(anterior, nueva);
                if (!incorporados.isEmpty()) {
                    cuerpo.append(" Se incorporan: ").append(String.join(", ", incorporados)).append('.');
                }
                return new AlertaSe8(PREFIJO_SUSTITUTO + sobre.claveIdempotencia(), medio(sustituto.canal()),
                        sustituto.destino(), titulo, cuerpo.toString(), prioridad(nueva), texto(d, "idDesastre"),
                        sustituto.organismo());
            }
            default:
                return null; // la cola solo recibe EV-03, EV-04 y EV-28
        }
    }

    /**
     * EV-29 para el acuse de una alerta de EV-28, o nulo si la alerta no vino de Notificación (modo sustituto). La clave
     * sale de la referencia de la alerta: repetir el acuse da siempre el mismo evento.
     */
    public static Sobre aEvento(AcuseSe8 acuse, Instant instanteEmision) {
        String referencia = acuse.referenciaExterna();
        if (referencia == null || !referencia.startsWith(PREFIJO_NOTIFICACION)) {
            return null;
        }
        String[] partes = referencia.substring(PREFIJO_NOTIFICACION.length()).split(":");
        if (partes.length != 2) {
            throw new EventoInvalidoException("Referencia de SE-8 sin el formato notificacion:<id>:<intento>: " + referencia);
        }
        Map<String, Object> datos = new LinkedHashMap<>();
        datos.put("idNotificacion", partes[0]);
        datos.put("intento", Integer.parseInt(partes[1]));
        datos.put("resultado", acuse.entregada() ? "ENTREGADA" : "FALLIDA");
        datos.put("motivo", acuse.motivo());
        datos.put("instante", instante(acuse.instante(), instanteEmision).toString());
        datos.put("claveOrigen", "se8:acuse:" + acuse.secuencia());
        return Sobre.crear(CatalogoEventos.ACUSE_NOTIFICACION, CatalogoEventos.ADAPTADOR_SE8, "se8:acuse:" + referencia,
                datos, instanteEmision, Procedencia.deSistemaExterno(SISTEMA), null);
    }

    /** Ámbito de coordinación de un nivel, en texto. */
    public static String ambito(String nivel) {
        List<String> ambito = AMBITO.get(nivel);
        if (ambito == null) {
            throw new EventoInvalidoException("Nivel de severidad desconocido: " + nivel);
        }
        return String.join(", ", ambito);
    }

    /** Destinatarios que un ascenso incorpora (SRS §4.3: de NIVEL_1 a NIVEL_3 se suman organismos y autoridades). */
    public static List<String> incorporados(String anterior, String nueva) {
        List<String> antes = AMBITO.getOrDefault(anterior, List.of());
        return AMBITO.getOrDefault(nueva, List.of()).stream().filter(a -> !antes.contains(a)).toList();
    }

    public static String prioridad(String nivel) {
        return switch (nivel) {
            case "NIVEL_1" -> "BAJA";
            case "NIVEL_2" -> "MEDIA";
            case "NIVEL_3" -> "ALTA";
            case "NIVEL_4" -> "CRITICA";
            default -> throw new EventoInvalidoException("Nivel de severidad desconocido: " + nivel);
        };
    }

    public static String medio(String canal) {
        return switch (canal) {
            case "CORREO" -> "EMAIL";
            case "SMS" -> "SMS";
            default -> throw new EventoInvalidoException("Canal sin equivalente en SE-8: " + canal);
        };
    }

    /** Canal de la plataforma para un medio de SE-8 (el estado se guarda con el vocabulario del contrato). */
    public static String canal(String medio) {
        return "EMAIL".equals(medio) ? "CORREO" : "SMS";
    }

    /** Nivel de la plataforma para una prioridad de SE-8. */
    public static String nivel(String prioridad) {
        return switch (prioridad) {
            case "BAJA" -> "NIVEL_1";
            case "MEDIA" -> "NIVEL_2";
            case "ALTA" -> "NIVEL_3";
            default -> "NIVEL_4";
        };
    }

    private static Instant instante(String texto, Instant porOmision) {
        if (texto == null || texto.isBlank()) {
            return porOmision;
        }
        try {
            return OffsetDateTime.parse(texto).toInstant();
        } catch (RuntimeException e) {
            return porOmision;
        }
    }

    private static String texto(JsonNode datos, String campo) {
        JsonNode valor = datos.get(campo);
        if (valor == null || valor.isNull()) {
            throw new EventoInvalidoException("Falta " + campo + " en los datos del evento.");
        }
        return valor.asText();
    }
}
