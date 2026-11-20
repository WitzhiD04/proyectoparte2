package plataforma.adaptador.se8.traduccion;

import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.List;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import plataforma.adaptador.se8.Muestras;
import plataforma.adaptador.se8.contrato.EventoInvalidoException;
import plataforma.adaptador.se8.contrato.Sobre;
import plataforma.adaptador.se8.contrato.ValidadorContrato;

/** Traducción canónico → SE-8 y acuse de SE-8 → EV-29. */
class TraduccionSe8Test {

    private static final OpcionesSustituto SUSTITUTO = OpcionesSustituto.POR_OMISION;
    private static final OpcionesSustituto APAGADO = new OpcionesSustituto(false, "CORREO", "x@y", "x");

    @Test
    void ev28EsUnaAlertaPorDestinatarioEIntento() {
        UUID id = UUID.randomUUID();
        Sobre ev28 = Muestras.ev28(id, 2);
        AlertaSe8 alerta = TraduccionSe8.aSe8(ev28, APAGADO); // EV-28 no depende del modo sustituto
        assertNotNull(alerta);
        assertEquals("notificacion:" + id + ":2", alerta.referenciaExterna());
        assertEquals("SMS", alerta.medio());
        assertEquals("+573001234567", alerta.destinatario());
        assertEquals("ALTA", alerta.prioridad());
        assertEquals("Bomberos Bogotá", alerta.organismo());
    }

    @Test
    void ev03EnModoSustitutoAlertaAlDestinoDeDifusionConElAmbito() {
        UUID idDesastre = UUID.randomUUID();
        Sobre ev03 = Muestras.ev03(idDesastre, "NIVEL_2");
        AlertaSe8 alerta = TraduccionSe8.aSe8(ev03, SUSTITUTO);
        assertNotNull(alerta);
        assertEquals("sustituto:" + ev03.claveIdempotencia(), alerta.referenciaExterna());
        assertEquals("EMAIL", alerta.medio());
        assertEquals(SUSTITUTO.destino(), alerta.destinatario());
        assertEquals("MEDIA", alerta.prioridad());
        assertEquals(idDesastre.toString(), alerta.codigoEvento());
        assertTrue(alerta.titulo().contains("Inundación río Bogotá"));
        assertTrue(alerta.cuerpo().contains("autoridad local, organismos de respuesta"), alerta.cuerpo());
    }

    @Test
    void conElModoSustitutoApagadoEv03YEv04NoVanASe8() {
        assertNull(TraduccionSe8.aSe8(Muestras.ev03(UUID.randomUUID(), "NIVEL_1"), APAGADO));
        assertNull(TraduccionSe8.aSe8(Muestras.ev04(UUID.randomUUID(), "NIVEL_1", "NIVEL_2"), APAGADO));
    }

    @Test
    void unAscensoDeNivel1ANivel3IncorporaDestinatarios() {
        AlertaSe8 alerta = TraduccionSe8.aSe8(Muestras.ev04(UUID.randomUUID(), "NIVEL_1", "NIVEL_3"), SUSTITUTO);
        assertNotNull(alerta);
        assertEquals("ALTA", alerta.prioridad());
        assertTrue(alerta.cuerpo().contains("Se incorporan: organismos de respuesta, autoridad regional y nacional"),
                alerta.cuerpo());
        assertEquals(List.of("asistencia internacional"), TraduccionSe8.incorporados("NIVEL_3", "NIVEL_4"));
        assertEquals(List.of(), TraduccionSe8.incorporados("NIVEL_3", "NIVEL_2"));
    }

    @Test
    void elAcuseDeUnaAlertaDeNotificacionEsUnEv29QueCumpleElContrato() {
        UUID id = UUID.randomUUID();
        AcuseSe8 acuse = new AcuseSe8("notificacion:" + id + ":3", 41, "ENTREGADA", null, "2026-11-19T15:00:01Z");
        Sobre ev29 = TraduccionSe8.aEvento(acuse, Muestras.AHORA);
        assertNotNull(ev29);
        assertEquals("EV-29", ev29.tipo());
        assertEquals("adaptador-se8", ev29.productor());
        assertEquals(id.toString(), ev29.datos().get("idNotificacion").asText());
        assertEquals(3, ev29.datos().get("intento").asInt());
        assertEquals("ENTREGADA", ev29.datos().get("resultado").asText());
        assertEquals("se8:acuse:41", ev29.datos().get("claveOrigen").asText());
        assertEquals("sistema-externo", ev29.procedencia().fuente());
        assertDoesNotThrow(() -> ValidadorContrato.instancia().validar(ev29.conSecuencia(1)));
        // El mismo acuse da siempre la misma clave: Notificación lo aplica una vez.
        assertEquals(ev29.claveIdempotencia(), TraduccionSe8.aEvento(acuse, Muestras.AHORA).claveIdempotencia());
    }

    @Test
    void unRechazoDeSe8EsUnEv29Fallida() {
        Sobre ev29 = TraduccionSe8.aEvento(new AcuseSe8("notificacion:" + UUID.randomUUID() + ":1", 0, "RECHAZADA",
                "número inválido", null), Muestras.AHORA);
        assertEquals("FALLIDA", ev29.datos().get("resultado").asText());
        assertEquals("número inválido", ev29.datos().get("motivo").asText());
    }

    @Test
    void losAcusesDelModoSustitutoNoProducenEv29() {
        assertNull(TraduccionSe8.aEvento(new AcuseSe8("sustituto:desastre:1:creado", 5, "ENTREGADA", null, null),
                Muestras.AHORA));
    }

    @Test
    void equivalenciasDeVocabulario() {
        assertEquals("BAJA", TraduccionSe8.prioridad("NIVEL_1"));
        assertEquals("CRITICA", TraduccionSe8.prioridad("NIVEL_4"));
        assertEquals("NIVEL_4", TraduccionSe8.nivel("CRITICA"));
        assertEquals("EMAIL", TraduccionSe8.medio("CORREO"));
        assertEquals("CORREO", TraduccionSe8.canal("EMAIL"));
        assertThrows(EventoInvalidoException.class, () -> TraduccionSe8.medio("PALOMA"));
        assertThrows(EventoInvalidoException.class, () -> TraduccionSe8.prioridad("NIVEL_5"));
    }
}
