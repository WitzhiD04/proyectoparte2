package plataforma.adaptador.se8.persistencia;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.time.Instant;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import plataforma.adaptador.se8.Muestras;
import plataforma.adaptador.se8.contrato.Sobre;
import plataforma.adaptador.se8.traduccion.AcuseSe8;
import plataforma.adaptador.se8.traduccion.OpcionesSustituto;
import plataforma.adaptador.se8.traduccion.TraduccionSe8;

/** Máquina de estados de la entrega: pendiente, enviada, entregada (solo con acuse) y fallida (QA-08). */
class EntregaTest {

    private static final Instant T0 = Muestras.AHORA;

    private static Entrega nueva(Sobre sobre) {
        return Entrega.nueva(TraduccionSe8.aSe8(sobre, OpcionesSustituto.POR_OMISION), sobre, T0);
    }

    private static AcuseSe8 acuse(Entrega e, String resultado) {
        return new AcuseSe8(e.getClaveEnvio(), 10, resultado, "ENTREGADA".equals(resultado) ? null : "buzón lleno", null);
    }

    @Test
    void empiezaPendienteYPasaAEnviadaCuandoSe8LaRecibe() {
        Entrega e = nueva(Muestras.ev03(UUID.randomUUID(), "NIVEL_1"));
        assertEquals(Entrega.Estado.PENDIENTE, e.getEstado());
        assertEquals(0, e.getEnvios());
        e.marcarEnviada(T0.plusSeconds(1));
        assertEquals(Entrega.Estado.ENVIADA, e.getEstado());
        assertEquals(1, e.getEnvios());
        assertNull(e.getAcusadaEn(), "enviada no es entregada: falta el acuse");
    }

    @Test
    void soloElAcuseLaLlevaAEntregada() {
        Entrega e = nueva(Muestras.ev03(UUID.randomUUID(), "NIVEL_1"));
        e.marcarEnviada(T0);
        assertTrue(e.registrarAcuse(acuse(e, "ENTREGADA"), T0.plusSeconds(2)));
        assertEquals(Entrega.Estado.ENTREGADA, e.getEstado());
        assertNotNull(e.getAcusadaEn());
        assertEquals(10L, e.getSecuenciaAcuse());
        // Ningún otro camino la lleva a entregada: reenviar o marcar fallida ya no se permiten.
        assertThrows(IllegalStateException.class, () -> e.marcarEnviada(T0));
        assertThrows(IllegalStateException.class, () -> e.marcarFallida("x"));
    }

    @Test
    void unAcuseRepetidoOTardioNoCambiaNada() {
        Entrega e = nueva(Muestras.ev03(UUID.randomUUID(), "NIVEL_1"));
        e.marcarEnviada(T0);
        assertTrue(e.registrarAcuse(acuse(e, "ENTREGADA"), T0.plusSeconds(1)));
        Instant acusada = e.getAcusadaEn();
        assertFalse(e.registrarAcuse(acuse(e, "ENTREGADA"), T0.plusSeconds(9)));
        assertFalse(e.registrarAcuse(acuse(e, "RECHAZADA"), T0.plusSeconds(9)));
        assertEquals(Entrega.Estado.ENTREGADA, e.getEstado());
        assertEquals(acusada, e.getAcusadaEn());
    }

    @Test
    void sinAcuseVuelveAPendienteYSeReenviaConLaMismaReferencia() {
        Entrega e = nueva(Muestras.ev04(UUID.randomUUID(), "NIVEL_1", "NIVEL_2"));
        String referencia = e.getClaveEnvio();
        e.marcarEnviada(T0);
        e.marcarSinAcuse();
        assertEquals(Entrega.Estado.PENDIENTE, e.getEstado());
        e.marcarEnviada(T0.plusSeconds(6));
        assertEquals(2, e.getEnvios());
        assertEquals(referencia, e.alerta("org").referenciaExterna());
        // Un acuse que llega mientras espera el reenvío también vale.
        e.marcarSinAcuse();
        assertTrue(e.registrarAcuse(acuse(e, "ENTREGADA"), T0.plusSeconds(7)));
        assertEquals(Entrega.Estado.ENTREGADA, e.getEstado());
    }

    @Test
    void unRechazoDeSe8LaDejaFallida() {
        Entrega e = nueva(Muestras.ev28(UUID.randomUUID(), 1));
        assertTrue(e.deNotificacion());
        e.marcarEnviada(T0);
        assertTrue(e.registrarAcuse(acuse(e, "RECHAZADA"), T0.plusSeconds(1)));
        assertEquals(Entrega.Estado.FALLIDA, e.getEstado());
        assertEquals("buzón lleno", e.getMotivo());
        assertNull(e.getAcusadaEn());
    }

    @Test
    void transicionesNoPermitidas() {
        Entrega e = nueva(Muestras.ev03(UUID.randomUUID(), "NIVEL_1"));
        assertThrows(IllegalStateException.class, e::marcarSinAcuse, "sin enviar no hay acuse que esperar");
        assertThrows(IllegalStateException.class, () -> e.registrarAcuse(acuse(e, "ENTREGADA"), T0),
                "no hay acuse de algo que no se envió");
        e.marcarFallida("rechazo 422");
        assertThrows(IllegalStateException.class, () -> e.marcarEnviada(T0));
    }

    @Test
    void guardaElOrigenConElVocabularioDelContrato() {
        UUID idNotificacion = UUID.randomUUID();
        Entrega e = nueva(Muestras.ev28(idNotificacion, 4));
        assertEquals("EV-28", e.getTipoOrigen());
        assertEquals(idNotificacion, e.getIdNotificacion());
        assertEquals(4, e.getIntento());
        assertEquals("SMS", e.getCanal());
        assertEquals("NIVEL_3", e.getSeveridad());
    }
}
