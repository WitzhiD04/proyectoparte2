package plataforma.adaptador.se8.nucleo;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import java.time.Clock;
import java.time.ZoneOffset;
import java.util.Optional;
import java.util.UUID;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.ArgumentCaptor;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;
import plataforma.adaptador.se8.Muestras;
import plataforma.adaptador.se8.contrato.ContratoAdaptador;
import plataforma.adaptador.se8.contrato.EntregaRechazadaException;
import plataforma.adaptador.se8.contrato.ResultadoEntrega;
import plataforma.adaptador.se8.contrato.SistemaExternoNoDisponibleException;
import plataforma.adaptador.se8.contrato.Sobre;
import plataforma.adaptador.se8.persistencia.AlmacenSe8;
import plataforma.adaptador.se8.persistencia.Entrega;
import plataforma.adaptador.se8.traduccion.AcuseSe8;
import plataforma.adaptador.se8.traduccion.OpcionesSustituto;
import plataforma.adaptador.se8.traduccion.TraduccionSe8;

/** Consumo idempotente: un evento se aplica una sola vez y un duplicado no produce un segundo envío. */
@ExtendWith(MockitoExtension.class)
class ProcesadorSe8Test {

    @Mock
    AlmacenSe8 almacen;
    @Mock
    ContratoAdaptador<AcuseSe8> adaptador;

    private ProcesadorSe8 procesador;

    @BeforeEach
    void preparar() {
        procesador = new ProcesadorSe8(almacen, adaptador, OpcionesSustituto.POR_OMISION,
                Clock.fixed(Muestras.AHORA, ZoneOffset.UTC));
    }

    @Test
    void laPrimeraVezRegistraLaEntregaLaEnviaYMarcaElMensaje() {
        Sobre ev03 = Muestras.ev03(UUID.randomUUID(), "NIVEL_2");
        when(almacen.yaProcesado(ev03.claveIdempotencia())).thenReturn(false);
        when(almacen.buscarEntrega(anyString())).thenReturn(Optional.empty());
        when(adaptador.entregar(ev03)).thenReturn(ResultadoEntrega.ACEPTADA);

        assertEquals(ProcesadorSe8.Resultado.APLICADO, procesador.procesar(ev03));

        ArgumentCaptor<Entrega> entrega = ArgumentCaptor.forClass(Entrega.class);
        verify(almacen).guardar(entrega.capture());
        assertEquals(Entrega.Estado.ENVIADA, entrega.getValue().getEstado());
        assertEquals("sustituto:" + ev03.claveIdempotencia(), entrega.getValue().getClaveEnvio());
        verify(almacen).registrarProcesado(ev03, Muestras.AHORA);
    }

    @Test
    void unMensajeDuplicadoNoGeneraUnSegundoEnvio() {
        Sobre ev03 = Muestras.ev03(UUID.randomUUID(), "NIVEL_2");
        when(almacen.yaProcesado(ev03.claveIdempotencia())).thenReturn(true);

        assertEquals(ProcesadorSe8.Resultado.YA_PROCESADO, procesador.procesar(ev03));

        verify(adaptador, never()).entregar(any());
        verify(almacen, never()).guardar(any());
        verify(almacen, never()).registrarProcesado(any(), any());
    }

    @Test
    void otraCopiaDelMismoEnvioConOtraClaveTampocoSeReenvia() {
        Sobre ev28 = Muestras.ev28(UUID.randomUUID(), 1);
        Entrega yaEnviada = Entrega.nueva(TraduccionSe8.aSe8(ev28, OpcionesSustituto.POR_OMISION), ev28, Muestras.AHORA);
        yaEnviada.marcarEnviada(Muestras.AHORA);
        when(almacen.yaProcesado(ev28.claveIdempotencia())).thenReturn(false);
        when(almacen.buscarEntrega(yaEnviada.getClaveEnvio())).thenReturn(Optional.of(yaEnviada));

        assertEquals(ProcesadorSe8.Resultado.YA_PROCESADO, procesador.procesar(ev28));
        verify(adaptador, never()).entregar(any());
        verify(almacen).registrarProcesado(ev28, Muestras.AHORA);
    }

    @Test
    void conSe8CaidoLaExcepcionSaleYNoSeMarcaNada() {
        Sobre ev03 = Muestras.ev03(UUID.randomUUID(), "NIVEL_1");
        when(almacen.yaProcesado(ev03.claveIdempotencia())).thenReturn(false);
        when(almacen.buscarEntrega(anyString())).thenReturn(Optional.empty());
        when(adaptador.entregar(ev03)).thenThrow(new SistemaExternoNoDisponibleException("503"));

        assertThrows(SistemaExternoNoDisponibleException.class, () -> procesador.procesar(ev03));
        verify(almacen, never()).registrarProcesado(any(), any());
    }

    @Test
    void unRechazoDeSe8DejaLaEntregaFallidaYPublicaEv29SiVinoDeNotificacion() {
        Sobre ev28 = Muestras.ev28(UUID.randomUUID(), 1);
        when(almacen.yaProcesado(ev28.claveIdempotencia())).thenReturn(false);
        when(almacen.buscarEntrega(anyString())).thenReturn(Optional.empty());
        when(adaptador.entregar(ev28)).thenThrow(new EntregaRechazadaException("422 destino inválido"));
        Sobre ev29 = TraduccionSe8.aEvento(new AcuseSe8("notificacion:" + UUID.randomUUID() + ":1", 0, "RECHAZADA",
                "x", null), Muestras.AHORA);
        when(adaptador.traducir(any())).thenReturn(ev29);

        assertEquals(ProcesadorSe8.Resultado.RECHAZADO, procesador.procesar(ev28));

        ArgumentCaptor<Entrega> entrega = ArgumentCaptor.forClass(Entrega.class);
        verify(almacen).guardar(entrega.capture());
        assertEquals(Entrega.Estado.FALLIDA, entrega.getValue().getEstado());
        verify(almacen).agregarABandeja(ev29);
        verify(almacen).registrarProcesado(ev28, Muestras.AHORA);
    }

    @Test
    void conElModoSustitutoApagadoEv03SeMarcaSinEnviar() {
        ProcesadorSe8 apagado = new ProcesadorSe8(almacen, adaptador, new OpcionesSustituto(false, "CORREO", "x", "x"),
                Clock.fixed(Muestras.AHORA, ZoneOffset.UTC));
        Sobre ev03 = Muestras.ev03(UUID.randomUUID(), "NIVEL_1");
        when(almacen.yaProcesado(ev03.claveIdempotencia())).thenReturn(false);

        assertEquals(ProcesadorSe8.Resultado.OMITIDO, apagado.procesar(ev03));
        verify(adaptador, never()).entregar(any());
        verify(almacen).registrarProcesado(ev03, Muestras.AHORA);
    }
}
