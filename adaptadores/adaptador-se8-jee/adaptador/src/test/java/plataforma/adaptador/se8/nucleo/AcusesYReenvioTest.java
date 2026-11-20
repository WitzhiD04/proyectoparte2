package plataforma.adaptador.se8.nucleo;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyInt;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import java.time.Duration;
import java.util.List;
import java.util.Optional;
import java.util.UUID;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import plataforma.adaptador.se8.Muestras;
import plataforma.adaptador.se8.RelojManual;
import plataforma.adaptador.se8.cliente.ClienteSe8;
import plataforma.adaptador.se8.cliente.TransporteSe8;
import plataforma.adaptador.se8.contrato.ContratoAdaptador;
import plataforma.adaptador.se8.contrato.Sobre;
import plataforma.adaptador.se8.persistencia.AlmacenSe8;
import plataforma.adaptador.se8.persistencia.Entrega;
import plataforma.adaptador.se8.resiliencia.Cortacircuitos;
import plataforma.adaptador.se8.resiliencia.EjecutorResiliente;
import plataforma.adaptador.se8.resiliencia.FalloTransitorioException;
import plataforma.adaptador.se8.resiliencia.OpcionesResiliencia;
import plataforma.adaptador.se8.traduccion.AcuseSe8;
import plataforma.adaptador.se8.traduccion.OpcionesSustituto;
import plataforma.adaptador.se8.traduccion.TraduccionSe8;

/** Acuses (solo ellos llevan a ENTREGADA) y reenvío por falta de acuse a los 5 s (QA-08). */
class AcusesYReenvioTest {

    private RelojManual reloj;
    private AlmacenSe8 almacen;
    @SuppressWarnings("unchecked")
    private final ContratoAdaptador<AcuseSe8> adaptador = mock(ContratoAdaptador.class);

    @BeforeEach
    void preparar() {
        reloj = new RelojManual(Muestras.AHORA);
        almacen = mock(AlmacenSe8.class);
    }

    private static Entrega enviada(Sobre origen) {
        Entrega e = Entrega.nueva(TraduccionSe8.aSe8(origen, OpcionesSustituto.POR_OMISION), origen, Muestras.AHORA);
        e.marcarEnviada(Muestras.AHORA);
        return e;
    }

    @Test
    void elAcuseDeUnaAlertaDeNotificacionLaEntregaYPublicaEv29() {
        Entrega e = enviada(Muestras.ev28(UUID.randomUUID(), 1));
        AcuseSe8 acuse = new AcuseSe8(e.getClaveEnvio(), 3, "ENTREGADA", null, null);
        Sobre ev29 = TraduccionSe8.aEvento(acuse, Muestras.AHORA);
        when(almacen.buscarEntrega(e.getClaveEnvio())).thenReturn(Optional.of(e));
        when(adaptador.traducir(acuse)).thenReturn(ev29);

        assertEquals(AplicadorAcuses.Resultado.APLICADO, new AplicadorAcuses(almacen, adaptador, reloj).aplicar(acuse));
        assertEquals(Entrega.Estado.ENTREGADA, e.getEstado());
        verify(almacen).agregarABandeja(ev29);
    }

    @Test
    void elAcuseDelModoSustitutoNoPublicaNada() {
        Entrega e = enviada(Muestras.ev03(UUID.randomUUID(), "NIVEL_1"));
        when(almacen.buscarEntrega(e.getClaveEnvio())).thenReturn(Optional.of(e));
        AplicadorAcuses aplicador = new AplicadorAcuses(almacen, adaptador, reloj);

        assertEquals(AplicadorAcuses.Resultado.APLICADO, aplicador.aplicar(new AcuseSe8(e.getClaveEnvio(), 1, "ENTREGADA", null, null)));
        assertEquals(AplicadorAcuses.Resultado.REPETIDO, aplicador.aplicar(new AcuseSe8(e.getClaveEnvio(), 1, "ENTREGADA", null, null)));
        verify(almacen, never()).agregarABandeja(any());
    }

    @Test
    void unAcuseDeUnaAlertaDesconocidaNoCambiaNada() {
        when(almacen.buscarEntrega("x")).thenReturn(Optional.empty());
        assertEquals(AplicadorAcuses.Resultado.DESCONOCIDO,
                new AplicadorAcuses(almacen, adaptador, reloj).aplicar(new AcuseSe8("x", 1, "ENTREGADA", null, null)));
    }

    /** Transporte de prueba: cuenta los envíos y puede estar caído. */
    private static final class Transporte implements TransporteSe8 {
        boolean caido;
        int envios;

        @Override
        public Respuesta enviar(plataforma.adaptador.se8.traduccion.AlertaSe8 alerta, Duration t) throws FalloTransitorioException {
            if (caido) {
                throw new FalloTransitorioException("503");
            }
            envios++;
            return new Respuesta(200, "{}");
        }

        @Override
        public void salud(Duration t) {
        }

        @Override
        public PaginaAcuses acuses(long desde, int limite, Duration t) {
            return new PaginaAcuses(List.of(), false);
        }
    }

    private ReenvioSinAcuse reenvio(Transporte transporte) {
        OpcionesResiliencia sinReintentos = new OpcionesResiliencia(3, 20, 0, 0.5, 3, 1.0, 30, 15);
        ClienteSe8 cliente = new ClienteSe8(transporte, new EjecutorResiliente(sinReintentos,
                new Cortacircuitos(sinReintentos, reloj), reloj, d -> { }, false));
        return new ReenvioSinAcuse(almacen, cliente, Duration.ofSeconds(5), 3, "Sala de crisis", reloj);
    }

    @Test
    void sinAcuseEn5sSeReenviaConLaMismaReferencia() {
        Entrega e = enviada(Muestras.ev03(UUID.randomUUID(), "NIVEL_2"));
        reloj.avanzar(Duration.ofSeconds(6));
        when(almacen.entregasSinAcuse(Muestras.AHORA.plusSeconds(1), 20)).thenReturn(List.of(e));
        Transporte transporte = new Transporte();

        ReenvioSinAcuse.Resumen r = reenvio(transporte).revisar(20);

        assertEquals(1, r.reenviadas());
        assertEquals(1, transporte.envios);
        assertEquals(Entrega.Estado.ENVIADA, e.getEstado());
        assertEquals(2, e.getEnvios());
    }

    @Test
    void conSe8CaidoQuedaPendiente() {
        Entrega e = enviada(Muestras.ev04(UUID.randomUUID(), "NIVEL_1", "NIVEL_2"));
        when(almacen.entregasSinAcuse(any(), anyInt())).thenReturn(List.of(e));
        Transporte transporte = new Transporte();
        transporte.caido = true;

        ReenvioSinAcuse.Resumen r = reenvio(transporte).revisar(20);

        assertEquals(1, r.pendientes());
        assertEquals(Entrega.Estado.PENDIENTE, e.getEstado(), "QA-08: se reintenta y queda pendiente");
    }

    @Test
    void alAgotarLosEnviosQuedaFallida() {
        Entrega e = enviada(Muestras.ev03(UUID.randomUUID(), "NIVEL_1"));
        e.marcarEnviada(Muestras.AHORA);
        e.marcarEnviada(Muestras.AHORA); // 3 envíos = máximo
        when(almacen.entregasSinAcuse(any(), anyInt())).thenReturn(List.of(e));
        Transporte transporte = new Transporte();

        ReenvioSinAcuse.Resumen r = reenvio(transporte).revisar(20);

        assertEquals(1, r.fallidas());
        assertEquals(0, transporte.envios);
        assertEquals(Entrega.Estado.FALLIDA, e.getEstado());
    }
}
