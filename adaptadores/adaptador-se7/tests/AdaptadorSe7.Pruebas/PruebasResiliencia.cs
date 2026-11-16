using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Comun.Adaptadores;
using Plataforma.Comun.Eventos;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace AdaptadorSe7.Pruebas;

/// <summary>Sistema externo de prueba: responde con lo que diga <see cref="Responder"/> y cuenta los intentos.</summary>
public sealed class SistemaFalso : HttpMessageHandler
{
    private int _intentos;
    public int Intentos => _intentos;
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Responder { get; set; } =
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Codigo(HttpStatusCode codigo) =>
        (_, _) => Task.FromResult(new HttpResponseMessage(codigo));

    /// <summary>Responde después de <paramref name="retraso"/>: más que el tiempo de espera del intento (modo "lento").</summary>
    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Lento(TimeSpan retraso) =>
        async (_, ct) =>
        {
            await Task.Delay(retraso, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
    {
        Interlocked.Increment(ref _intentos);
        return Responder(peticion, ct);
    }
}

/// <summary>
/// La resiliencia de comun (DT-09) con el registro real (AddHttpClient + AddResilienceHandler): reintento solo de
/// operaciones idempotentes, tiempo de espera por intento y cortacircuitos "3 de 3" que avisa a EV-23 (DA-11, QA-01).
/// </summary>
public class PruebasResiliencia
{
    public static OpcionesResiliencia Rapida() => new()
    {
        TiempoEsperaIntentoSegundos = 0.2,
        TiempoEsperaTotalSegundos = 5,
        Reintentos = 2,
        EsperaBaseReintentoSegundos = 0.01,
        VentanaSegundos = 30,
        DuracionAperturaSegundos = 0.5,   // el mínimo de Polly
    };

    private static (HttpClient Cliente, MonitorCircuito Monitor, ServiceProvider Proveedor) Cliente(SistemaFalso sistema, OpcionesResiliencia opciones)
    {
        ServiceCollection servicios = new();
        servicios.AddLogging();
        servicios.AddSingleton(new MonitorCircuito("SE-7"));
        servicios.AddHttpClient("se7", c => c.BaseAddress = new Uri("http://se7.prueba/"))
            .ConfigurePrimaryHttpMessageHandler(() => sistema)
            .AgregarResilienciaAdaptador(opciones);
        ServiceProvider proveedor = servicios.BuildServiceProvider();
        return (proveedor.GetRequiredService<IHttpClientFactory>().CreateClient("se7"), proveedor.GetRequiredService<MonitorCircuito>(), proveedor);
    }

    private static List<TransicionCircuito> Transiciones(MonitorCircuito monitor)
    {
        List<TransicionCircuito> lista = [];
        while (monitor.Transiciones.TryRead(out TransicionCircuito? t))
        {
            lista.Add(t);
        }
        return lista;
    }

    [Fact]
    public async Task Tres_tiempos_de_espera_agotados_abren_el_circuito_y_publican_EV23()
    {
        SistemaFalso sistema = new() { Responder = SistemaFalso.Lento(TimeSpan.FromSeconds(5)) };
        (HttpClient cliente, MonitorCircuito monitor, ServiceProvider proveedor) = Cliente(sistema, Rapida());
        using ServiceProvider liberar = proveedor;

        // Una sola operación idempotente: el intento y sus dos reintentos agotan el tiempo de espera (estímulo de QA-01).
        await Assert.ThrowsAsync<TimeoutRejectedException>(() => cliente.GetAsync("v1/salud"));

        Assert.Equal(3, sistema.Intentos);
        Assert.Equal(EstadoIntegracion.ABIERTO, monitor.Estado);
        TransicionCircuito abre = Assert.Single(Transiciones(monitor));
        Assert.Equal(EstadoIntegracion.CERRADO, abre.Anterior);
        Assert.Equal(EstadoIntegracion.ABIERTO, abre.Nuevo);
        Plataforma.Comun.Mensajeria.ValidadorContrato.Instancia.Validar(TraduccionEstadoIntegracion.ASobre(abre, "adaptador-se7", 3));

        // Abierto: el adaptador no invoca al sistema externo (Cuadro 5).
        await Assert.ThrowsAsync<BrokenCircuitException>(() => cliente.GetAsync("v1/salud"));
        Assert.Equal(3, sistema.Intentos);
    }

    [Fact]
    public async Task Al_responder_otra_vez_el_circuito_se_cierra_y_publica_el_cierre()
    {
        SistemaFalso sistema = new() { Responder = SistemaFalso.Codigo(HttpStatusCode.ServiceUnavailable) };
        (HttpClient cliente, MonitorCircuito monitor, ServiceProvider proveedor) = Cliente(sistema, Rapida());
        using ServiceProvider liberar = proveedor;

        using (HttpResponseMessage r = await cliente.GetAsync("v1/salud"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        }
        Assert.Equal(EstadoIntegracion.ABIERTO, monitor.Estado);
        DateTimeOffset desde = monitor.AbiertoDesde!.Value;

        // Pasada la apertura, una prueba fallida lo vuelve a abrir: para la plataforma sigue abierto desde el principio.
        // (El reintento no insiste contra un circuito abierto: la llamada termina en BrokenCircuitException.)
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        int antes = sistema.Intentos;
        await Assert.ThrowsAsync<BrokenCircuitException>(() => cliente.GetAsync("v1/salud"));
        Assert.Equal(antes + 1, sistema.Intentos);   // solo la llamada de prueba llegó al sistema
        Assert.Equal(EstadoIntegracion.ABIERTO, monitor.Estado);
        Assert.Equal(desde, monitor.AbiertoDesde);

        sistema.Responder = SistemaFalso.Codigo(HttpStatusCode.OK);
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        using (HttpResponseMessage r = await cliente.GetAsync("v1/salud"))
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }

        Assert.Equal(EstadoIntegracion.CERRADO, monitor.Estado);
        List<TransicionCircuito> transiciones = Transiciones(monitor);
        Assert.Equal([EstadoIntegracion.ABIERTO, EstadoIntegracion.CERRADO], transiciones.Select(t => t.Nuevo));
        Assert.Equal(EstadoIntegracion.ABIERTO, transiciones[1].Anterior);
    }

    [Fact]
    public async Task Solo_se_reintentan_las_operaciones_idempotentes()
    {
        SistemaFalso sistema = new() { Responder = SistemaFalso.Codigo(HttpStatusCode.BadGateway) };
        OpcionesResiliencia opciones = Rapida();
        opciones.MinimoLlamadas = 100;   // que el circuito no se abra y deje ver los intentos
        (HttpClient cliente, _, ServiceProvider proveedor) = Cliente(sistema, opciones);
        using ServiceProvider liberar = proveedor;

        (await cliente.PutAsync("v1/obras/x/versiones/1", new StringContent("{}"))).Dispose();
        Assert.Equal(3, sistema.Intentos);    // PUT de una versión: idempotente

        (await cliente.GetAsync("v1/cambios")).Dispose();
        Assert.Equal(6, sistema.Intentos);    // GET: idempotente

        (await cliente.PostAsync("v1/algo", new StringContent("{}"))).Dispose();
        Assert.Equal(7, sistema.Intentos);    // POST: un solo intento (Cuadro 5)
    }

    [Fact]
    public async Task Un_fallo_suelto_no_abre_el_circuito()
    {
        int llamada = 0;
        SistemaFalso sistema = new()
        {
            Responder = (_, _) => Task.FromResult(new HttpResponseMessage(
                Interlocked.Increment(ref llamada) == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)),
        };
        (HttpClient cliente, MonitorCircuito monitor, ServiceProvider proveedor) = Cliente(sistema, Rapida());
        using ServiceProvider liberar = proveedor;

        for (int i = 0; i < 5; i++)
        {
            (await cliente.GetAsync("v1/salud")).Dispose();
        }

        Assert.Equal(EstadoIntegracion.CERRADO, monitor.Estado);
        Assert.Empty(Transiciones(monitor));
    }

    [Fact]
    public void La_indisponibilidad_se_distingue_del_rechazo()
    {
        CancellationToken vivo = CancellationToken.None;
        Assert.True(Indisponibilidad.Es(new BrokenCircuitException(), vivo));
        Assert.True(Indisponibilidad.Es(new TimeoutRejectedException(), vivo));
        Assert.True(Indisponibilidad.Es(new HttpRequestException(), vivo));
        Assert.True(Indisponibilidad.Es(new SistemaExternoNoDisponibleException("503"), vivo));
        Assert.True(Indisponibilidad.Es(new TaskCanceledException(), vivo));   // HttpClient.Timeout
        Assert.False(Indisponibilidad.Es(new EntregaRechazadaException("422"), vivo));
        Assert.False(Indisponibilidad.Es(new OperationCanceledException(), new CancellationToken(true)));   // apagado
    }
}
