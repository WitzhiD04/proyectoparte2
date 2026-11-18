using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Plataforma.Comun.Adaptadores;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Mensajeria;
using SimSe6;

namespace AdaptadorSe6.Pruebas;

/// <summary>Salida falsa por lotes para probar <see cref="ManejadorLotesSe6"/> sin HTTP.</summary>
public sealed class SalidaLotesFalsa : ISalidaLotesSe6
{
    public string SistemaExterno => "SE-6";
    public Queue<Exception?> Respuestas { get; } = new();
    public List<IReadOnlyList<Sobre>> LotesRecibidos { get; } = [];
    public int Llamadas => LotesRecibidos.Count;

    public Task<ResultadoEntrega> Entregar(Sobre eventoCanonico, CancellationToken ct) =>
        EntregarLoteAsync([eventoCanonico], ct);

    public Task<ResultadoEntrega> EntregarLoteAsync(IReadOnlyList<Sobre> eventosCanonicos, CancellationToken ct)
    {
        LotesRecibidos.Add(eventosCanonicos);
        Exception? error = Respuestas.Count > 0 ? Respuestas.Dequeue() : null;
        return error is null
            ? Task.FromResult(ResultadoEntrega.Entregado)
            : Task.FromException<ResultadoEntrega>(error);
    }

    public Task SondearAsync(CancellationToken ct) => Task.CompletedTask;
}

public class PruebasLotesYSalida
{
    [Fact]
    public async Task El_colector_cierra_el_lote_en_cuanto_alcanza_el_tamano_sin_esperar_la_ventana()
    {
        Channel<int> canal = Channel.CreateUnbounded<int>();
        for (int i = 1; i <= 5; i++)
        {
            canal.Writer.TryWrite(i);
        }

        Stopwatch cronometro = Stopwatch.StartNew();
        IReadOnlyList<int> lote = await ColectorLotes.TomarLoteAsync(
            canal.Reader, tamanoMaximo: 4, ventana: TimeSpan.FromSeconds(10), TimeProvider.System, CancellationToken.None);
        cronometro.Stop();

        Assert.Equal([1, 2, 3, 4], lote);
        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task El_colector_entrega_el_lote_parcial_cuando_vence_el_tiempo()
    {
        Channel<int> canal = Channel.CreateUnbounded<int>();
        canal.Writer.TryWrite(10);
        canal.Writer.TryWrite(20);

        Stopwatch cronometro = Stopwatch.StartNew();
        IReadOnlyList<int> lote = await ColectorLotes.TomarLoteAsync(
            canal.Reader, tamanoMaximo: 10, ventana: TimeSpan.FromMilliseconds(150), TimeProvider.System, CancellationToken.None);
        cronometro.Stop();

        Assert.Equal([10, 20], lote);
        Assert.True(cronometro.Elapsed >= TimeSpan.FromMilliseconds(100));
        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task El_colector_acumula_elementos_que_llegan_durante_la_ventana_hasta_llenar_el_tamano()
    {
        Channel<int> canal = Channel.CreateUnbounded<int>();
        canal.Writer.TryWrite(1);

        _ = Task.Run(async () =>
        {
            await Task.Delay(40);
            canal.Writer.TryWrite(2);
            await Task.Delay(40);
            canal.Writer.TryWrite(3);
        });

        Stopwatch cronometro = Stopwatch.StartNew();
        IReadOnlyList<int> lote = await ColectorLotes.TomarLoteAsync(
            canal.Reader, tamanoMaximo: 3, ventana: TimeSpan.FromSeconds(5), TimeProvider.System, CancellationToken.None);
        cronometro.Stop();

        Assert.Equal([1, 2, 3], lote);
        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(2));
    }

    private static ManejadorLotesSe6 Manejador(SalidaLotesFalsa salida, MonitorCircuito monitor) =>
        new(salida, monitor, new OpcionesSalida { EsperaReintentoSegundos = 0.05 }, NullLogger<ManejadorLotesSe6>.Instance);

    [Fact]
    public async Task Con_SE6_caido_el_manejador_de_lotes_no_lanza_y_reintenta_hasta_entregar()
    {
        SalidaLotesFalsa salida = new();
        salida.Respuestas.Enqueue(new SistemaExternoNoDisponibleException("503"));
        salida.Respuestas.Enqueue(new Polly.Timeout.TimeoutRejectedException());
        salida.Respuestas.Enqueue(new HttpRequestException("conexión rechazada"));

        await Manejador(salida, new MonitorCircuito("SE-6"))
            .ManejarLoteAsync([Ejemplos.ProyectoCreado(), Ejemplos.PresupuestoCambiado()], CancellationToken.None);

        Assert.Equal(4, salida.Llamadas);
    }

    [Fact]
    public async Task Con_el_circuito_abierto_espera_hasta_que_la_compuerta_lo_cancela()
    {
        SalidaLotesFalsa salida = new();
        for (int i = 0; i < 1000; i++)
        {
            salida.Respuestas.Enqueue(new Polly.CircuitBreaker.BrokenCircuitException());
        }
        MonitorCircuito monitor = new("SE-6");
        monitor.AlAbrirse();
        using CancellationTokenSource compuerta = new(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Manejador(salida, monitor).ManejarLoteAsync([Ejemplos.ProyectoCreado()], compuerta.Token));
    }

    [Fact]
    public async Task Un_rechazo_de_SE6_es_permanente_y_va_a_la_cola_de_mensajes_muertos()
    {
        SalidaLotesFalsa salida = new();
        salida.Respuestas.Enqueue(new EntregaRechazadaException("422"));

        await Assert.ThrowsAsync<EventoInvalidoException>(() =>
            Manejador(salida, new MonitorCircuito("SE-6")).ManejarLoteAsync([Ejemplos.ProyectoCreado()], CancellationToken.None));
        Assert.Equal(1, salida.Llamadas);
    }
}

public class PruebasSimuladorSe6
{
    private static MovimientoFinanciero Movimiento(
        string proyecto = "proy-1",
        int version = 1,
        string clase = ClasesMovimiento.Apertura,
        string situacion = Situaciones.Abierta,
        decimal? presupuesto = 500_000_000m,
        string? fuente = "SGR",
        decimal? monto = 500_000_000m,
        decimal acumulado = 0m) =>
        new(proyecto, version, clase, "Escuela", Ejemplos.Desastre.ToString(), situacion, presupuesto, fuente, monto,
            acumulado, Ejemplos.Instante, "autor", "nota");

    [Fact]
    public void La_idempotencia_en_el_simulador_es_por_elemento_dentro_y_entre_lotes()
    {
        RegistroSe6 registro = new();
        MovimientoFinanciero m1 = Movimiento("proy-1", 1, ClasesMovimiento.Apertura);
        MovimientoFinanciero m2 = Movimiento("proy-1", 2, ClasesMovimiento.Presupuesto, Situaciones.Comprometida, 600_000_000m, "UNGRD", 600_000_000m);
        MovimientoFinanciero m3 = Movimiento("proy-1", 3, ClasesMovimiento.Costo, Situaciones.EnEjecucion, 600_000_000m, null, 150_000_000m, 150_000_000m);

        RespuestaLote primero = registro.RegistrarLote("lote-1", [m1, m2]);
        Assert.Equal(2, primero.Nuevos);
        Assert.Equal(0, primero.Repetidos);

        // Segundo lote con m2 repetido y m3 nuevo: solo m3 se aplica.
        RespuestaLote segundo = registro.RegistrarLote("lote-2", [m2, m3]);
        Assert.Equal(1, segundo.Nuevos);
        Assert.Equal(1, segundo.Repetidos);

        // Reenvío completo de lote-2: 0 nuevos, 2 repetidos.
        RespuestaLote reenvio = registro.RegistrarLote("lote-2", [m2, m3]);
        Assert.Equal(0, reenvio.Nuevos);
        Assert.Equal(2, reenvio.Repetidos);

        EstadoCuentaProyecto cuenta = Assert.Single(registro.Estado().Proyectos);
        Assert.Equal([1, 2, 3], cuenta.Versiones);
        Assert.Equal(3, cuenta.EntregasRepetidas);
        Assert.Equal(600_000_000m, cuenta.Presupuesto);
        Assert.Equal("UNGRD", cuenta.FuenteFinanciacion);
        Assert.Equal(150_000_000m, cuenta.CostoAcumulado);
        Assert.Equal(Situaciones.EnEjecucion, cuenta.Situacion);
    }

    [Fact]
    public void Un_movimiento_viejo_que_llega_en_un_lote_posterior_no_pisa_el_estado_mas_reciente()
    {
        RegistroSe6 registro = new();
        registro.RegistrarLote("lote-a", [
            Movimiento("proy-1", 4, ClasesMovimiento.Finalizacion, Situaciones.Liquidada, 600_000_000m, null, null, 580_000_000m),
        ]);
        registro.RegistrarLote("lote-b", [
            Movimiento("proy-1", 1, ClasesMovimiento.Apertura, Situaciones.Abierta, 500_000_000m, "SGR", 500_000_000m, 0m),
        ]);

        EstadoCuentaProyecto cuenta = registro.Proyecto("proy-1")!;
        Assert.Equal([1, 4], cuenta.Versiones);
        Assert.Equal(Situaciones.Liquidada, cuenta.Situacion);
        Assert.Equal(600_000_000m, cuenta.Presupuesto);
        Assert.Equal("SGR", cuenta.FuenteFinanciacion);
        Assert.Equal(580_000_000m, cuenta.CostoAcumulado);
    }

    [Fact]
    public void Un_costo_sin_monto_positivo_o_con_situacion_desconocida_es_invalido()
    {
        Assert.False(RegistroSe6.EsMovimientoValido(Movimiento(clase: ClasesMovimiento.Costo, monto: null)));
        Assert.False(RegistroSe6.EsMovimientoValido(Movimiento(clase: ClasesMovimiento.Costo, monto: 0m)));
        Assert.False(RegistroSe6.EsMovimientoValido(Movimiento(situacion: "OTRA")));
        Assert.False(RegistroSe6.EsMovimientoValido(Movimiento(version: 0)));
    }
}
