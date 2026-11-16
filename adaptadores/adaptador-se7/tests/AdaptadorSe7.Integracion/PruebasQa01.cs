using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Plataforma.Comun.Adaptadores;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;
using SimSe7;

namespace AdaptadorSe7.Integracion;

/// <summary>
/// QA-01 con SE-7: SE-7 deja de responder (tiempos de espera agotados o 503). La plataforma marca SE-7 como no disponible
/// (EV-23), deja de invocarlo, los mensajes esperan en q.salida.se7 sin ir a la cola de mensajes muertos y, al volver
/// SE-7, se entregan todos sin duplicados (QA-01 b, c y d).
/// </summary>
[Collection(ColeccionSe7.Nombre)]
public sealed class PruebasSalidaQa01(EntornoSe7 entorno) : IAsyncLifetime
{
    public Task InitializeAsync() => entorno.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<List<(EstadoIntegracionCambiadoV1 Datos, DateTimeOffset? PublicadoEn)>> Ev23Async()
    {
        await using AdaptadorSe7DbContext db = entorno.ComoApp();
        List<FilaBandeja> filas = await db.Bandeja.Where(b => b.Tipo == "EV-23").OrderBy(b => b.Secuencia).ToListAsync();
        return filas.Select(f => (Sobre.Leer(f.Sobre).LeerDatos<EstadoIntegracionCambiadoV1>(), f.PublicadoEn)).ToList();
    }

    [Theory]
    [InlineData("lento")]   // el estímulo de QA-01: tres invocaciones agotan su tiempo de espera
    [InlineData("on")]      // SE-7 responde 503
    public async Task Con_SE7_caido_los_EV18_esperan_en_la_cola_y_se_entregan_todos_al_volver(string caida)
    {
        const int Proyectos = 10;
        await using SimuladorSe7 se7 = new();
        HttpClient administracion = se7.CreateClient();
        (await administracion.PostAsJsonAsync("/admin/caida", new { modo = caida })).EnsureSuccessStatusCode();

        using IHost adaptador = entorno.Adaptador("salida", se7);
        await adaptador.StartAsync();
        MonitorCircuito monitor = adaptador.Services.GetRequiredService<MonitorCircuito>();
        CompuertaConsumo<AdaptadorSe7DbContext> compuerta = adaptador.Services.GetRequiredService<CompuertaConsumo<AdaptadorSe7DbContext>>();

        List<Guid> ids = Enumerable.Range(0, Proyectos).Select(_ => Guid.NewGuid()).ToList();
        await entorno.PublicarComoReconstruccionAsync(ids.Select((id, i) => EntornoSe7.ProyectoCreado(id, i)));

        // (b) El circuito se abre y EV-23 ABIERTO sale por la bandeja al bus en 30 s o menos.
        await EntornoSe7.EsperarAsync(() => monitor.Estado == EstadoIntegracion.ABIERTO, "el circuito se abre", 30);
        await EntornoSe7.EsperarAsync(async () => (await Ev23Async()).Any(e => e.PublicadoEn is not null), "EV-23 ABIERTO publicado", 30);
        var abierto = (await Ev23Async()).Single();
        Assert.Equal("SE-7", abierto.Datos.SistemaExterno);
        Assert.Equal(EstadoIntegracion.CERRADO, abierto.Datos.EstadoAnterior);
        Assert.Equal(EstadoIntegracion.ABIERTO, abierto.Datos.EstadoNuevo);
        Assert.True(abierto.PublicadoEn!.Value - abierto.Datos.Instante <= TimeSpan.FromSeconds(30));

        // Con el circuito abierto el consumo se pausa: los diez mensajes quedan listos en la cola, ninguno en la DLQ.
        await EntornoSe7.EsperarAsync(() => !compuerta.Consumiendo, "la compuerta pausa el consumo", 10);
        await EntornoSe7.EsperarAsync(async () => await entorno.ListosAsync("q.salida.se7") == Proyectos, "los 10 EV-18 en la cola", 20);
        int registradasAntes = se7.Registro.Estado().VersionesRegistradas;
        await Task.Delay(TimeSpan.FromSeconds(4));   // varias aperturas y pruebas fallidas del cortacircuitos
        Assert.Equal(Proyectos, (int)await entorno.ListosAsync("q.salida.se7"));
        Assert.Equal(0u, await entorno.ListosAsync("dlq.salida.se7"));
        Assert.Equal(registradasAntes, se7.Registro.Estado().VersionesRegistradas);   // dejó de invocarlo
        Assert.Single(await Ev23Async());    // las pruebas fallidas no publican otra apertura

        // SE-7 vuelve: el circuito se cierra, EV-23 CERRADO sale y se entregan todos.
        (await administracion.PostAsJsonAsync("/admin/caida", new { modo = "off" })).EnsureSuccessStatusCode();
        await EntornoSe7.EsperarAsync(() => se7.Registro.Estado().Obras.Count == Proyectos, "SE-7 recibe las 10 obras", 60);
        await EntornoSe7.EsperarAsync(async () => await entorno.ListosAsync("q.salida.se7") == 0, "la cola de salida queda vacía", 30);
        await EntornoSe7.EsperarAsync(async () => (await Ev23Async()).Count(e => e.PublicadoEn is not null) == 2, "EV-23 CERRADO publicado", 30);

        // (c, d) Sin duplicados y sin pérdidas.
        EstadoSimulador estado = se7.Registro.Estado();
        Assert.Equal(ids.Select(i => i.ToString()).Order(), estado.Obras.Select(o => o.CodigoObra).Order());
        Assert.All(estado.Obras, o => Assert.Equal([1], o.Versiones));
        Assert.Equal(Proyectos, estado.VersionesRegistradas);
        Assert.Equal(0u, await entorno.ListosAsync("dlq.salida.se7"));
        await using (AdaptadorSe7DbContext db = entorno.ComoApp())
        {
            Assert.Equal(Proyectos, await db.Procesados.CountAsync(p => p.Tipo == "EV-18"));
        }
        var cerrado = (await Ev23Async())[1];
        Assert.Equal(EstadoIntegracion.ABIERTO, cerrado.Datos.EstadoAnterior);
        Assert.Equal(EstadoIntegracion.CERRADO, cerrado.Datos.EstadoNuevo);
        Assert.Equal(EstadoIntegracion.CERRADO, monitor.Estado);

        // Los dos EV-23 llegaron a los consumidores (q.consulta-programa los proyecta para la consola).
        List<Sobre> enConsulta = (await entorno.VaciarAsync("q.consulta-programa")).Where(s => s.Tipo == "EV-23").ToList();
        Assert.Equal([EstadoIntegracion.ABIERTO, EstadoIntegracion.CERRADO],
            enConsulta.Select(s => s.LeerDatos<EstadoIntegracionCambiadoV1>().EstadoNuevo));
        Assert.All(enConsulta, s => Assert.Equal("adaptador-se7", s.Productor));

        await adaptador.StopAsync();
    }

    [Fact]
    public async Task Un_EV18_repetido_no_se_registra_dos_veces_en_SE7_y_el_eco_de_SE7_no_se_reenvia()
    {
        await using SimuladorSe7 se7 = new();
        using IHost adaptador = entorno.Adaptador("salida", se7);
        await adaptador.StartAsync();

        Guid id = Guid.NewGuid();
        Sobre creado = EntornoSe7.ProyectoCreado(id, 1);
        // El mismo hecho con otro identificador de evento y otra clave (por ejemplo, un reenvío tras una conmutación con
        // otra numeración): SE-7 lo descarta por obra y versión (Cuadro 14).
        Sobre reenvio = creado with { IdEvento = Guid.NewGuid(), ClaveIdempotencia = creado.ClaveIdempotencia + ":reenvio" };
        ResumenProyecto resumen = new(Guid.Parse("1b7d2f7e-3c1a-4c55-8e6f-2a9e1d0c4b11"), "x", EstadoProyecto.EN_EJECUCION, 1m, 0m, 40m, 3);
        Sobre eco = Sobre.Crear(CatalogoEventos.EstadoOAvanceCambiado, CatalogoEventos.Reconstruccion, $"proyecto:{id}:v3",
            new EstadoOAvanceCambiadoV1(id, TipoCambioProyecto.AVANCE, EstadoProyecto.EN_EJECUCION, EstadoProyecto.EN_EJECUCION, 40m, 0m,
                null, "0", "40", 3, "SE-7", "Reportado por SE-7", resumen),
            DateTimeOffset.UtcNow, Procedencia.DeSistemaExterno("SE-7"));
        await entorno.PublicarComoReconstruccionAsync([creado, creado, reenvio, eco]);

        await EntornoSe7.EsperarAsync(async () =>
        {
            await using AdaptadorSe7DbContext db = entorno.ComoApp();
            return await db.Procesados.CountAsync() == 3;   // el duplicado exacto lo descarta el consumidor
        }, "los mensajes se procesan", 30);

        EstadoObra obra = Assert.Single(se7.Registro.Estado().Obras);
        Assert.Equal([1], obra.Versiones);           // la versión 3 (eco de SE-7) no se envió
        Assert.Equal(1, obra.EntregasRepetidas);     // el reenvío llegó y SE-7 lo descartó
        Assert.Equal(0u, await entorno.ListosAsync("dlq.salida.se7"));
        await adaptador.StopAsync();
    }
}

/// <summary>Entrada de SE-7: cada cambio sale una sola vez en EV-24, aunque el adaptador se reinicie o corra dos veces.</summary>
[Collection(ColeccionSe7.Nombre)]
public sealed class PruebasEntrada(EntornoSe7 entorno) : IAsyncLifetime
{
    public Task InitializeAsync() => entorno.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<FilaMarcaAvance?> MarcaAsync()
    {
        await using AdaptadorSe7DbContext db = entorno.ComoApp();
        return await db.Marcas.AsNoTracking().SingleOrDefaultAsync(m => m.Flujo == RegistroAdaptadorSe7.FlujoEntrada);
    }

    [Fact]
    public async Task La_entrada_publica_EV24_una_vez_por_cambio_aunque_se_reinicie_a_mitad()
    {
        await using SimuladorSe7 se7 = new();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        for (int i = 1; i <= 7; i++)
        {
            se7.Registro.AgregarCambio((i % 2 == 0 ? a : b).ToString(), "AVANCE", i * 10, null);
        }
        se7.Registro.AgregarCambio("OBRA-PROPIA-1", "AVANCE", 50, null);   // no es de la plataforma: no sale
        Dictionary<string, string?> porPaginas = new() { ["Se7:TamanoPagina"] = "3", ["Entrada:MaximoPaginasPorRonda"] = "1" };

        // Primer proceso: confirma una página y se detiene a mitad (reinicio, o KEDA lo baja a cero).
        using (IHost primero = entorno.Adaptador("entrada", se7, porPaginas))
        {
            await primero.StartAsync();
            await EntornoSe7.EsperarAsync(async () => (await MarcaAsync())?.Valor is not null, "la primera página se confirma", 30);
            await primero.StopAsync();
        }
        string marcaIntermedia = (await MarcaAsync())!.Valor!;
        Assert.NotEqual("8", marcaIntermedia);

        // Mientras estaba detenido, SE-7 siguió reportando.
        se7.Registro.AgregarCambio(a.ToString(), "CONTRATISTA", null, "Constructora 001 S.A.S.");
        se7.Registro.AgregarCambio(b.ToString(), "AVANCE", 95, null);

        // Dos procesos a la vez (dos réplicas): retoman desde la marca y no publican nada dos veces.
        using IHost segundo = entorno.Adaptador("entrada", se7, porPaginas);
        using IHost tercero = entorno.Adaptador("entrada", se7, porPaginas);
        await Task.WhenAll(segundo.StartAsync(), tercero.StartAsync());
        await EntornoSe7.EsperarAsync(async () => (await MarcaAsync())?.Valor == "10", "la marca llega al último cambio", 60);
        await EntornoSe7.EsperarAsync(async () =>
        {
            await using AdaptadorSe7DbContext db = entorno.ComoApp();
            return await db.Bandeja.Where(f => f.Tipo == "EV-24").AllAsync(f => f.PublicadoEn != null);
        }, "los EV-24 se publican", 30);
        await Task.WhenAll(segundo.StopAsync(), tercero.StopAsync());

        Assert.Equal(10, (await MarcaAsync())!.Cambios);
        List<string> esperadas = [.. Enumerable.Range(1, 7).Select(i => $"se7:avance:{i}"), "se7:contratista:9", "se7:avance:10"];
        await using (AdaptadorSe7DbContext db = entorno.ComoApp())
        {
            List<string> enBandeja = await db.Bandeja.Where(f => f.Tipo == "EV-24").Select(f => f.ClaveIdempotencia).ToListAsync();
            Assert.Equal(esperadas.Order(), enBandeja.Order());
        }
        // Reconstrucción (q.reconstruccion) recibe exactamente un EV-24 por cambio, válido según el contrato.
        List<Sobre> recibidos = (await entorno.VaciarAsync("q.reconstruccion")).Where(s => s.Tipo == "EV-24").ToList();
        Assert.Equal(esperadas.Order(), recibidos.Select(s => s.ClaveIdempotencia).Order());
        Assert.All(recibidos, ValidadorContrato.Instancia.Validar);
        ReporteExternoProyectoV1 contratista = recibidos.Single(s => s.ClaveIdempotencia == "se7:contratista:9").LeerDatos<ReporteExternoProyectoV1>();
        Assert.Equal(a, contratista.IdProyecto);
        Assert.Equal("Constructora 001 S.A.S.", contratista.Contratista);
        Assert.Equal("9", contratista.MarcaAvance);
    }

    [Fact]
    public async Task Con_SE7_caido_la_entrada_no_mueve_la_marca_y_al_volver_recupera_el_rango_pendiente()
    {
        await using SimuladorSe7 se7 = new();
        Guid a = Guid.NewGuid();
        se7.Registro.AgregarCambio(a.ToString(), "AVANCE", 10, null);
        se7.Registro.CambiarModo("on");

        using IHost entrada = entorno.Adaptador("entrada", se7);
        await entrada.StartAsync();
        MonitorCircuito monitor = entrada.Services.GetRequiredService<MonitorCircuito>();
        await EntornoSe7.EsperarAsync(() => monitor.Estado == EstadoIntegracion.ABIERTO, "el circuito de la entrada se abre", 30);
        Assert.Null((await MarcaAsync())?.Valor);

        se7.Registro.AgregarCambio(a.ToString(), "AVANCE", 20, null);
        se7.Registro.CambiarModo("off");
        await EntornoSe7.EsperarAsync(async () => (await MarcaAsync())?.Valor == "2", "la entrada recupera desde la marca", 30);
        Assert.Equal(EstadoIntegracion.CERRADO, monitor.Estado);
        // EV-23 de la apertura y del cierre, y los dos avances: el pendiente y el nuevo.
        await EntornoSe7.EsperarAsync(async () =>
        {
            await using AdaptadorSe7DbContext db = entorno.ComoApp();
            return await db.Bandeja.CountAsync() == 4;
        }, "EV-23 del cierre en la bandeja", 30);
        await using (AdaptadorSe7DbContext db = entorno.ComoApp())
        {
            List<string> claves = await db.Bandeja.Select(f => f.ClaveIdempotencia).ToListAsync();
            Assert.Equal(["integracion", "integracion", "se7:avance:1", "se7:avance:2"],
                claves.Select(c => c.StartsWith("integracion", StringComparison.Ordinal) ? "integracion" : c).Order(StringComparer.Ordinal));
        }
        await entrada.StopAsync();
    }
}

/// <summary>Esquema y permisos (DT-02, DT-19): el rol del adaptador solo ve su esquema y no borra ni cambia el historial.</summary>
[Collection(ColeccionSe7.Nombre)]
public sealed class PruebasBase(EntornoSe7 entorno)
{
    [Fact]
    public async Task Migrar_otra_vez_no_falla_y_el_esquema_es_se7()
    {
        await MigracionAdaptadorSe7.EjecutarAsync(entorno.ConfiguracionMigrador, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        await using NpgsqlConnection conexion = new(entorno.App);
        await conexion.OpenAsync();
        await using NpgsqlCommand comando = new("SHOW search_path", conexion);
        Assert.Equal("se7", (string?)await comando.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData("DELETE FROM se7.bandeja_salida")]
    [InlineData("DELETE FROM se7.marcas_avance")]
    [InlineData("UPDATE se7.mensajes_procesados SET tipo = 'x'")]
    [InlineData("DELETE FROM se7.mensajes_procesados")]
    [InlineData("TRUNCATE se7.bandeja_salida")]
    [InlineData("CREATE TABLE se7.intrusa (id int)")]
    [InlineData("CREATE TABLE public.intrusa (id int)")]
    [InlineData("SELECT * FROM se6.bandeja_salida")]
    public async Task El_rol_del_adaptador_tiene_minimo_privilegio(string sql)
    {
        // Otro adaptador en la misma base (DA-11): el rol de SE-7 no ve su esquema.
        await using (NpgsqlConnection dueno = new(entorno.Dueno))
        {
            await dueno.OpenAsync();
            await using NpgsqlCommand crear = new("CREATE SCHEMA IF NOT EXISTS se6; CREATE TABLE IF NOT EXISTS se6.bandeja_salida (id int)", dueno);
            await crear.ExecuteNonQueryAsync();
        }
        await using NpgsqlConnection conexion = new(entorno.App);
        await conexion.OpenAsync();
        await using NpgsqlCommand comando = new(sql, conexion);
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }
}
