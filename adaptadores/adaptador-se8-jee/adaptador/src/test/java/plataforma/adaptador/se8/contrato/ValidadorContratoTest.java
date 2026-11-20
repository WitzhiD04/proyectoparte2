package plataforma.adaptador.se8.contrato;

import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.fasterxml.jackson.databind.node.ObjectNode;
import java.nio.charset.StandardCharsets;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import plataforma.adaptador.se8.Muestras;

/** Validación del sobre contra sobre.v1.json y de los datos contra EV-nn.v1.json (los esquemas de comun). */
class ValidadorContratoTest {

    private final ValidadorContrato validador = ValidadorContrato.instancia();

    @Test
    void aceptaLosEventosDeLaColaDeSalida() {
        assertDoesNotThrow(() -> validador.validar(Muestras.ev03(UUID.randomUUID(), "NIVEL_2")));
        assertDoesNotThrow(() -> validador.validar(Muestras.ev04(UUID.randomUUID(), "NIVEL_1", "NIVEL_3")));
        assertDoesNotThrow(() -> validador.validar(Muestras.ev28(UUID.randomUUID(), 1)));
    }

    @Test
    void aceptaUnSobreEscritoPorUnProductorNet() {
        // System.Text.Json escribe el desfase como +00:00 y los nombres en camelCase.
        String json = Muestras.ev03(UUID.randomUUID(), "NIVEL_1").serializar()
                .replace("\"instanteEmision\":\"2026-11-19T15:00:00Z\"", "\"instanteEmision\":\"2026-11-19T15:00:00.1234567+00:00\"");
        Sobre sobre = Sobre.leer(json.getBytes(StandardCharsets.UTF_8));
        assertDoesNotThrow(() -> validador.validar(Sobre.arbol(json.getBytes(StandardCharsets.UTF_8))));
        assertEquals("EV-03", sobre.tipo());
    }

    @Test
    void rechazaDatosQueNoCumplenElEsquemaDelTipo() {
        ObjectNode arbol = Muestras.ev03(UUID.randomUUID(), "NIVEL_2").aJson();
        ((ObjectNode) arbol.get("datos")).remove("nombre");
        EventoInvalidoException e = assertThrows(EventoInvalidoException.class, () -> validador.validar(arbol));
        assertTrue(e.getMessage().contains("EV-03.v1.json"), e.getMessage());
    }

    @Test
    void rechazaUnNivelFueraDelCatalogo() {
        ObjectNode arbol = Muestras.ev03(UUID.randomUUID(), "NIVEL_2").aJson();
        ((ObjectNode) arbol.get("datos")).put("severidad", "NIVEL_9");
        assertThrows(EventoInvalidoException.class, () -> validador.validar(arbol));
    }

    @Test
    void exigeElFormatoUuid() {
        ObjectNode arbol = Muestras.ev04(UUID.randomUUID(), "NIVEL_1", "NIVEL_2").aJson();
        ((ObjectNode) arbol.get("datos")).put("idDesastre", "no-es-un-uuid");
        assertThrows(EventoInvalidoException.class, () -> validador.validar(arbol));
    }

    @Test
    void rechazaUnSobreSinCamposObligatorios() {
        ObjectNode arbol = Muestras.ev03(UUID.randomUUID(), "NIVEL_2").aJson();
        arbol.remove("secuenciaProductor");
        EventoInvalidoException e = assertThrows(EventoInvalidoException.class, () -> validador.validar(arbol));
        assertTrue(e.getMessage().contains("sobre.v1.json"), e.getMessage());
    }

    @Test
    void rechazaUnNombreQueNoEsElDelTipo() {
        ObjectNode arbol = Muestras.ev03(UUID.randomUUID(), "NIVEL_2").aJson();
        arbol.put("nombre", "desastre.otro");
        assertThrows(EventoInvalidoException.class, () -> validador.validar(arbol));
    }

    @Test
    void rechazaTiposQueNoSonDelAdaptador() {
        ObjectNode arbol = Muestras.ev03(UUID.randomUUID(), "NIVEL_2").aJson();
        arbol.put("tipo", "EV-18");
        assertThrows(EventoInvalidoException.class, () -> validador.validar(arbol));
    }

    @Test
    void rechazaLoQueNoEsJson() {
        assertThrows(EventoInvalidoException.class, () -> Sobre.arbol("{no es json".getBytes(StandardCharsets.UTF_8)));
    }
}
