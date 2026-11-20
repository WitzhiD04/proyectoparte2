package plataforma.adaptador.se8;

import java.time.Instant;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.UUID;
import plataforma.adaptador.se8.contrato.CatalogoEventos;
import plataforma.adaptador.se8.contrato.Procedencia;
import plataforma.adaptador.se8.contrato.Sobre;

/** Eventos del contrato para las pruebas, con datos que cumplen sus esquemas. */
public final class Muestras {

    public static final Instant AHORA = Instant.parse("2026-11-19T15:00:00Z");

    private Muestras() {
    }

    public static Sobre ev03(UUID idDesastre, String nivel) {
        Map<String, Object> d = new LinkedHashMap<>();
        d.put("idDesastre", idDesastre.toString());
        d.put("tipo", "INUNDACION");
        d.put("severidad", nivel);
        d.put("zonaVersion", 1);
        d.put("estado", "REGISTRADO");
        d.put("organismoRegistra", "UNGRD");
        d.put("responsable", "ungrd.operador");
        d.put("origen", "MANUAL");
        d.put("nombre", "Inundación río Bogotá");
        d.put("descripcion", "Desbordamiento en el sector norte");
        d.put("instanteInicio", "2026-11-19T14:00:00Z");
        d.put("ubicacion", Map.of("lat", 4.71, "lon", -74.07));
        d.put("versionDesastre", 1);
        return publicado(Sobre.crear(CatalogoEventos.DESASTRE_CREADO, "gestion-desastres", "desastre:" + idDesastre + ":creado",
                d, AHORA, Procedencia.PLATAFORMA, "corr-1"), 7);
    }

    public static Sobre ev04(UUID idDesastre, String anterior, String nueva) {
        Map<String, Object> d = new LinkedHashMap<>();
        d.put("idDesastre", idDesastre.toString());
        d.put("severidadAnterior", anterior);
        d.put("severidadNueva", nueva);
        d.put("version", 2);
        d.put("autor", "ungrd.operador");
        d.put("motivo", "Aumento de damnificados");
        return publicado(Sobre.crear(CatalogoEventos.SEVERIDAD_CAMBIADA, "gestion-desastres",
                "desastre:" + idDesastre + ":severidad:2", d, AHORA, Procedencia.PLATAFORMA, null), 8);
    }

    public static Sobre ev28(UUID idNotificacion, int intento) {
        Map<String, Object> d = new LinkedHashMap<>();
        d.put("idNotificacion", idNotificacion.toString());
        d.put("intento", intento);
        d.put("idEventoOrigen", UUID.randomUUID().toString());
        d.put("idDesastre", UUID.randomUUID().toString());
        d.put("organismo", "Bomberos Bogotá");
        d.put("canal", "SMS");
        d.put("destino", "+573001234567");
        d.put("asunto", "Alerta NIVEL_3");
        d.put("mensaje", "Se activa el plan de respuesta");
        d.put("severidad", "NIVEL_3");
        return publicado(Sobre.crear(CatalogoEventos.ENVIO_NOTIFICACION_SOLICITADO, "notificacion",
                "notificacion:" + idNotificacion + ":" + intento + ":envio", d, AHORA, Procedencia.PLATAFORMA, null), 9);
    }

    /** Como llega del bus: con la secuencia que asignó el relevo del productor. */
    private static Sobre publicado(Sobre sobre, long secuencia) {
        return sobre.conSecuencia(secuencia);
    }
}
