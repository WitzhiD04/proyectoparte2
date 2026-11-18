using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Comun.Adaptadores;
using Plataforma.Comun.Eventos;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace AdaptadorSe6.Pruebas;

/// <summary>Sistema externo de prueba: responde con lo que diga <see cref="Responder"/> y cuenta los intentos.</summary>
public sealed class SistemaFalso : HttpMessageHandler
{
    private int _intentos;
    public int Intentos => _intentos;
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Responder { get; set; } =
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Codigo(HttpStatusCode codigo) =>
        (_, _) => Task.FromResult(new HttpResponseMessage(codigo));

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
/// Resiliencia común (DT-09) hacia SE-6: reintento en entregas de lote por PUT, tiempo de espera por intento y
/// cortacircuitos "3 de 3" que publica EV-23 (DA-11, QA-01).
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
        DuracionAperturaSegundos = 0.5,
    };

    private static (ClienteSe6 Cliente, MonitorCircuito Monitor, ServiceProvider Proveedor) CrearCliente(
        SistemaFalso sistema, OpcionesResiliencia opciones)
    {
        ServiceCollection servicios = new();
        servicios.AddLogging();
        servicios.AddSingleton(new MonitorCircuito("SE-6"));
        servicios.AddHttpClient<ClienteSe6>(c => c.BaseAddress = new Uri("http://se6.prueba/"))
            .ConfigurePrimaryHttpMessageHandler(() => sistema)
            .AgregarResilienciaAdaptador(opciones);
        ServiceProvider proveedor = servicios.BuildServiceProvider();
        return (proveedor.GetRequiredService<ClienteSe6>(), proveedor.GetRequiredService<MonitorCircuito>(), proveedor);
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
    public async Task Tres_tiempos_de_espera_agotados_en_la_entrega_del_lote_abren_el_circuito_y_publican_EV23()
    {
        SistemaFalso sistema = new() { Responder = SistemaFalso.Lento(TimeSpan.FromSeconds(5)) };
        (ClienteSe6 cliente, MonitorCircuito monitor, ServiceProvider proveedor) = CrearCliente(sistema, Rapida());
        using ServiceProvider liberar = proveedor;

        await Assert.ThrowsAsync<TimeoutRejectedException>(() =>
            cliente.EntregarLoteAsync([Ejemplos.ProyectoCreado()], CancellationToken.None));

        Assert.Equal(3, sistema.Intentos);
        Assert.Equal(EstadoIntegracion.ABIERTO, monitor.Estado);
        TransicionCircuito abre = Assert.Single(Transiciones(monitor));
        Assert.Equal("SE-6", abre.SistemaExterno);
        Assert.Equal(EstadoIntegracion.CERRADO, abre.Anterior);
        Assert.Equal(EstadoIntegracion.ABIERTO, abre.Nuevo);
        Plataforma.Comun.Mensajeria.ValidadorContrato.Instancia.Validar(
            TraduccionEstadoIntegracion.ASobre(abre, "adaptador-se6", 5));

        // Con el circuito abierto, no invoca a SE-6.
        await Assert.ThrowsAsync<BrokenCircuitException>(() =>
            cliente.EntregarLoteAsync([Ejemplos.ProyectoCreado()], CancellationToken.None));
        Assert.Equal(3, sistema.Intentos);
    }

    [Fact]
    public async Task Al_responder_otra_vez_el_circuito_se_cierra_y_publica_el_cierre()
    {
        SistemaFalso sistema = new() { Responder = SistemaFalso.Codigo(HttpStatusCode.ServiceUnavailable) };
        (ClienteSe6 cliente, MonitorCircuito monitor, ServiceProvider proveedor) = CrearCliente(sistema, Rapida());
        using ServiceProvider liberar = proveedor;

        await Assert.ThrowsAsync<SistemaExternoNoDisponibleException>(() =>
            cliente.EntregarLoteAsync([Ejemplos.ProyectoCreado()], CancellationToken.None));
        Assert.Equal(EstadoIntegracion.ABIERTO, monitor.Estado);
        DateTimeOffset desde = monitor.AbiertoDesde!.Value;

        // Una prueba fallida durante SEMIABIERTO vuelve a abrir el circuito sin publicar otro EV-23.
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        int antes = sistema.Intentos;
        await Assert.ThrowsAsync<BrokenCircuitException>(() => cliente.SondearAsync(CancellationToken.None));
        Assert.Equal(antes + 1, sistema.Intentos);
        Assert.Equal(EstadoIntegracion.ABIERTO, monitor.Estado);
        Assert.Equal(desde, monitor.AbiertoDesde);

        // Cuando SE-6 responde, el sondeo cierra el circuito y emite EV-23 CERRADO.
        sistema.Responder = SistemaFalso.Codigo(HttpStatusCode.OK);
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        await cliente.SondearAsync(CancellationToken.None);

        Assert.Equal(EstadoIntegracion.CERRADO, monitor.Estado);
        List<TransicionCircuito> transiciones = Transiciones(monitor);
        Assert.Equal([EstadoIntegracion.ABIERTO, EstadoIntegracion.CERRADO], transiciones.Select(t => t.Nuevo));
    }
}
