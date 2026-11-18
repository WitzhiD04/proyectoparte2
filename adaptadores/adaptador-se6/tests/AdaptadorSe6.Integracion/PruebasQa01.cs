using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Plataforma.Comun.Adaptadores;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;
using SimSe6;

namespace AdaptadorSe6.Integracion;

/// <summary>
/// QA-01 con SE-6: SE-6 deja de responder (tiempos de espera agotados o 503). La plataforma marca SE-6 como no disponible
/// (EV-23), deja de invocarlo, los movimientos esperan en q.salida.se6 sin ir a la cola de mensajes muertos y, al volver
/// SE-6, se entregan todos en lotes sin duplicados (QA-01 b, c y d).
/// </summary>
[Collection(ColeccionSe6.Nombre)]
public sealed class PruebasSalidaQa01(EntornoSe6 entorno) : IAsyncLifetime
{
    public Task InitializeAsync() => entorno.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<List<(EstadoIntegracionCambiadoV1 Datos, DateTimeOffset? PublicadoEn)>> Ev23Async()
    {
        await using AdaptadorSe6DbContext db = entorno.ComoApp();
        List<FilaBandeja> filas = await db.Bandeja.Where(b => b.Tipo == "EV-23").OrderBy(b => b.Secuencia).ToListAsync();
        return filas.Select(f => (Sobre.Leer(f.Sobre).LeerDatos<EstadoIntegracionCambiadoV1>(), f.PublicadoEn)).ToList();
    }

    [Theory]
    [InlineData("lento")]   // el estímulo de QA-01: tres invocaciones agotan su tiempo de espera
    [InlineData("on")]      // SE-6 responde 503
    public async Task Con_SE6_caido_los_movimientos_esperan_en_la_cola_y_se_entregan_todos_al_volver_sin_duplicados(string caida)
    {
        const int Proyectos = 5;
        await using SimuladorSe6 se6 = new();
        HttpClient administracion = se6.CreateClient();
        (await administracion.PostAsJsonAsync("/admin/caida", new { modo = caida })).EnsureSuccessStatusCode();

        using IHost adaptador = entorno.Adaptador(se6, new Dictionary<string, string?>
        {
            ["Se6:TamanoLote"] = "4",
            ["Se6:VentanaLoteSegundos"] = "0.2",
        });
        await adaptador.StartAsync();
        MonitorCircuito monitor = adaptador.Services.GetRequiredService<MonitorCircuito>();
        CompuertaLotesSe6 compuerta = adaptador.Services.GetRequiredService<CompuertaLotesSe6>();

        // Por cada proyecto: EV-18 (v1), EV-19 (v2) y EV-20 con costo (v3) -> 15 movimientos para SE-6.
        List<Guid> ids = Enumerable.Range(0, Proyectos).Select(_ => Guid.NewGuid()).ToList();
        List<Sobre> eventos = [];
        for (int i = 0; i < Proyectos; i++)
        {
            Guid id = ids[i];
            eventos.Add(EntornoSe6.ProyectoCreado(id, i));
            eventos.Add(EntornoSe6.PresupuestoCambiado(id, 2, 600_000_000m));
            eventos.Add(EntornoSe6.CostoRegistrado(id, 3, 150_000_000m, 150_000_000m));
        }
        await entorno.PublicarComoReconstruccionAsync(eventos);

        // (b) El circuito se abre y EV-23 ABIERTO sale por la bandeja al bus en 30 s o menos.
        await EntornoSe6.EsperarAsync(() => monitor.Estado == EstadoIntegracion.ABIERTO, "el circuito se abre", 30);
        await EntornoSe6.EsperarAsync(async () => (await Ev23Async()).Any(e => e.PublicadoEn is not null), "EV-23 ABIERTO publicado", 30);
        var abierto = (await Ev23Async()).Single();
        Assert.Equal("SE-6", abierto.Datos.SistemaExterno);
        Assert.Equal(EstadoIntegracion.CERRADO, abierto.Datos.EstadoAnterior);
        Assert.Equal(EstadoIntegracion.ABIERTO, abierto.Datos.EstadoNuevo);
        Assert.True(abierto.PublicadoEn!.Value - abierto.Datos.Instante <= TimeSpan.FromSeconds(30));

        // Con el circuito abierto el consumo se pausa: los 15 mensajes quedan listos en q.salida.se6, ninguno en la DLQ.
        await EntornoSe6.EsperarAsync(() => !compuerta.Consumiendo, "la compuerta pausa el consumo", 10);
        await EntornoSe6.EsperarAsync(async () => await entorno.ListosAsync("q.salida.se6") == eventos.Count, "los 15 eventos en la cola", 20);
        int registradosAntes = se6.Registro.Estado().MovimientosRegistrados;
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(eventos.Count, (int)await entorno.ListosAsync("q.salida.se6"));
        Assert.Equal(0u, await entorno.ListosAsync("dlq.salida.se6"));
        Assert.Equal(registradosAntes, se6.Registro.Estado().MovimientosRegistrados);
        Assert.Single(await Ev23Async());

        // SE-6 vuelve: el circuito se cierra, EV-23 CERRADO sale y se entregan todos los lotes.
        (await administracion.PostAsJsonAsync("/admin/caida", new { modo = "off" })).EnsureSuccessStatusCode();
        await EntornoSe6.EsperarAsync(() => se6.Registro.Estado().MovimientosRegistrados == eventos.Count, "SE-6 recibe los 15 movimientos", 60);
        await EntornoSe6.EsperarAsync(async () => await entorno.ListosAsync("q.salida.se6") == 0, "la cola de salida queda vacía", 30);
        await EntornoSe6.EsperarAsync(async () => (await Ev23Async()).Count(e => e.PublicadoEn is not null) == 2, "EV-23 CERRADO publicado", 30);

        // (c, d) Sin duplicados y sin pérdidas.
        EstadoSimulador estado = se6.Registro.Estado();
        Assert.Equal(Proyectos, estado.Proyectos.Count);
        Assert.Equal(eventos.Count, estado.MovimientosRegistrados);
        Assert.All(estado.Proyectos, p =>
        {
            Assert.Equal([1, 2, 3], p.Versiones);
            Assert.Equal(600_000_000m, p.Presupuesto);
            Assert.Equal(150_000_000m, p.CostoAcumulado);
        });
        Assert.Equal(0u, await entorno.ListosAsync("dlq.salida.se6"));
        await using (AdaptadorSe6DbContext db = entorno.ComoApp())
        {
            Assert.Equal(eventos.Count, await db.Procesados.CountAsync());
        }

        var cerrado = (await Ev23Async())[1];
        Assert.Equal(EstadoIntegracion.ABIERTO, cerrado.Datos.EstadoAnterior);
        Assert.Equal(EstadoIntegracion.CERRADO, cerrado.Datos.EstadoNuevo);
        Assert.Equal(EstadoIntegracion.CERRADO, monitor.Estado);

        List<Sobre> enConsulta = (await entorno.VaciarAsync("q.consulta-programa")).Where(s => s.Tipo == "EV-23")
            .DistinctBy(s => s.IdEvento).ToList();
        Assert.Equal([EstadoIntegracion.ABIERTO, EstadoIntegracion.CERRADO],
            enConsulta.Select(s => s.LeerDatos<EstadoIntegracionCambiadoV1>().EstadoNuevo));
        Assert.All(enConsulta, s => Assert.Equal("adaptador-se6", s.Productor));

        await adaptador.StopAsync();
    }

    [Fact]
    public async Task El_lote_respeta_el_tamano_cuando_se_llena_y_el_tiempo_cuando_es_parcial()
    {
        await using SimuladorSe6 se6 = new();
        // Lote de tamaño 4 y ventana larga (15 s): 8 eventos listos en la cola deben entregarse en 2 lotes de 4 sin esperar los 15 s.
        List<Sobre> ocho = Enumerable.Range(1, 8).Select(i => EntornoSe6.ProyectoCreado(Guid.NewGuid(), i)).ToList();
        await entorno.PublicarComoReconstruccionAsync(ocho);

        using (IHost porTamano = entorno.Adaptador(se6, new Dictionary<string, string?>
        {
            ["Se6:TamanoLote"] = "4",
            ["Se6:VentanaLoteSegundos"] = "15",
            ["Resiliencia:TiempoEsperaIntentoSegundos"] = "3",
        }))
        {
            Stopwatch cronometro = Stopwatch.StartNew();
            await porTamano.StartAsync();
            await EntornoSe6.EsperarAsync(() => se6.Registro.Estado().MovimientosRegistrados == 8, "8 movimientos por tamaño", 10);
            cronometro.Stop();

            Assert.Equal(2, se6.Registro.Estado().LotesRecibidos);
            Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(8));
            await porTamano.StopAsync();
        }

        // Lote de tamaño 10 y ventana corta (0.4 s): 3 eventos deben entregarse en 1 lote parcial al vencer el tiempo.
        using (IHost porTiempo = entorno.Adaptador(se6, new Dictionary<string, string?>
        {
            ["Se6:TamanoLote"] = "10",
            ["Se6:VentanaLoteSegundos"] = "0.4",
            ["Resiliencia:TiempoEsperaIntentoSegundos"] = "3",
        }))
        {
            await porTiempo.StartAsync();
            List<Sobre> tres = Enumerable.Range(9, 3).Select(i => EntornoSe6.ProyectoCreado(Guid.NewGuid(), i)).ToList();

            await entorno.PublicarComoReconstruccionAsync(tres);
            await EntornoSe6.EsperarAsync(() => se6.Registro.Estado().MovimientosRegistrados == 11, "3 movimientos por tiempo", 15);

            Assert.Equal(3, se6.Registro.Estado().LotesRecibidos);
            await porTiempo.StopAsync();
        }
    }

    [Fact]
    public async Task Del_EV20_solo_envia_costo_y_cierre_y_un_reenvio_no_duplica_en_SE6()
    {
        await using SimuladorSe6 se6 = new();
        using IHost adaptador = entorno.Adaptador(se6, new Dictionary<string, string?>
        {
            ["Se6:TamanoLote"] = "5",
            ["Se6:VentanaLoteSegundos"] = "0.2",
            ["Resiliencia:TiempoEsperaIntentoSegundos"] = "3",
        });
        await adaptador.StartAsync();

        Guid id = Guid.NewGuid();
        Sobre v1Creacion = EntornoSe6.ProyectoCreado(id, 1, 500_000_000m);
        Sobre v2Presupuesto = EntornoSe6.PresupuestoCambiado(id, 2, 600_000_000m, "UNGRD");
        Sobre v3Aprobado = EntornoSe6.EstadoCambiado(id, 3, EstadoProyecto.FORMULADO, EstadoProyecto.APROBADO, 0m, 0m);
        Sobre v4Contratista = EntornoSe6.ContratistaRegistrado(id, 4, "Constructora Andina");
        Sobre v5Avance = EntornoSe6.AvanceRegistrado(id, 5, 50m);
        Sobre v6Costo = EntornoSe6.CostoRegistrado(id, 6, 180_000_000m, 180_000_000m);
        Sobre v7Avance100 = EntornoSe6.AvanceRegistrado(id, 7, 100m);
        Sobre v8Finalizado = EntornoSe6.EstadoCambiado(id, 8, EstadoProyecto.EN_EJECUCION, EstadoProyecto.FINALIZADO, 100m, 180_000_000m);
        // Reenvío del costo con otro idEvento y otra claveIdempotencia: SE-6 lo descarta por (idProyecto, versionHistorial).
        Sobre reenvioCosto = v6Costo with { IdEvento = Guid.NewGuid(), ClaveIdempotencia = v6Costo.ClaveIdempotencia + ":reenvio" };

        await entorno.PublicarComoReconstruccionAsync([
            v1Creacion, v1Creacion, v2Presupuesto, v3Aprobado, v4Contratista, v5Avance, v6Costo, v7Avance100, v8Finalizado,
        ]);

        await EntornoSe6.EsperarAsync(async () =>
        {
            await using AdaptadorSe6DbContext db = entorno.ComoApp();
            return await db.Procesados.CountAsync() == 8; // el duplicado exacto de v1Creacion se descarta por claveIdempotencia
        }, "los 8 mensajes iniciales se procesan", 30);

        // En un lote posterior llega el reenvío de v6Costo con otro idEvento y otra claveIdempotencia:
        // pasa el filtro del consumidor, llega a SE-6 y SE-6 lo descarta por (idProyecto, versionHistorial).
        await entorno.PublicarComoReconstruccionAsync([reenvioCosto]);

        await EntornoSe6.EsperarAsync(async () =>
        {
            await using AdaptadorSe6DbContext db = entorno.ComoApp();
            return await db.Procesados.CountAsync() == 9;
        }, "el reenvío en otro lote se procesa", 30);

        EstadoCuentaProyecto cuenta = Assert.Single(se6.Registro.Estado().Proyectos);
        // Solo v1 (APERTURA), v2 (PRESUPUESTO), v6 (COSTO) y v8 (FINALIZACION) llegaron a SE-6; v3, v4, v5 y v7 se ignoraron.
        Assert.Equal([1, 2, 6, 8], cuenta.Versiones);
        Assert.Equal(1, cuenta.EntregasRepetidas); // reenvioCosto fue descartado por SE-6 por (idProyecto + versionHistorial)
        Assert.Equal(Situaciones.Liquidada, cuenta.Situacion);
        Assert.Equal(600_000_000m, cuenta.Presupuesto);
        Assert.Equal("UNGRD", cuenta.FuenteFinanciacion);
        Assert.Equal(180_000_000m, cuenta.CostoAcumulado);
        Assert.Equal(180_000_000m, cuenta.Movimientos.Single(m => m.VersionHistorial == 6).MontoMovimiento);
        Assert.Equal(0u, await entorno.ListosAsync("dlq.salida.se6"));

        await adaptador.StopAsync();
    }
}

/// <summary>Esquema y permisos (DT-02, DT-19): el rol del adaptador solo ve su esquema se6 y no borra ni modifica mensajes procesados.</summary>
[Collection(ColeccionSe6.Nombre)]
public sealed class PruebasBase(EntornoSe6 entorno)
{
    [Fact]
    public async Task Migrar_otra_vez_no_falla_y_el_esquema_es_se6()
    {
        await MigracionAdaptadorSe6.EjecutarAsync(entorno.ConfiguracionMigrador, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        await using NpgsqlConnection conexion = new(entorno.App);
        await conexion.OpenAsync();
        await using NpgsqlCommand comando = new("SHOW search_path", conexion);
        Assert.Equal("se6", (string?)await comando.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData("DELETE FROM se6.bandeja_salida")]
    [InlineData("UPDATE se6.mensajes_procesados SET tipo = 'x'")]
    [InlineData("DELETE FROM se6.mensajes_procesados")]
    [InlineData("TRUNCATE se6.bandeja_salida")]
    [InlineData("CREATE TABLE se6.intrusa (id int)")]
    [InlineData("CREATE TABLE public.intrusa (id int)")]
    [InlineData("SELECT * FROM se7.bandeja_salida")]
    public async Task El_rol_del_adaptador_tiene_minimo_privilegio(string sql)
    {
        await using (NpgsqlConnection dueno = new(entorno.Dueno))
        {
            await dueno.OpenAsync();
            await using NpgsqlCommand crear = new("CREATE SCHEMA IF NOT EXISTS se7; CREATE TABLE IF NOT EXISTS se7.bandeja_salida (id int)", dueno);
            await crear.ExecuteNonQueryAsync();
        }
        await using NpgsqlConnection conexion = new(entorno.App);
        await conexion.OpenAsync();
        await using NpgsqlCommand comando = new(sql, conexion);
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }
}
