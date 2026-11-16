using Auditoria.Aplicacion.CasosDeUso;
using Auditoria.Infraestructura.Persistencia;
using Auditoria.Infraestructura.Procesos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Auditoria.Integracion;

/// <summary>Esquema, particiones y permisos de mínimo privilegio del registro (AD-41, QA-13 b y e, DT-02).</summary>
public class PruebasPersistencia : IClassFixture<BaseMigrada>
{
    private readonly BaseMigrada _base;
    public PruebasPersistencia(BaseMigrada baseMigrada) { _base = baseMigrada; }

    private static async Task<List<string>> ListaAsync(string cadena, string sql)
    {
        await using NpgsqlConnection conexion = new(cadena);
        await conexion.OpenAsync();
        await using NpgsqlCommand comando = new(sql, conexion);
        await using NpgsqlDataReader lector = await comando.ExecuteReaderAsync();
        List<string> filas = new();
        while (await lector.ReadAsync())
        {
            filas.Add(Convert.ToString(lector.GetValue(0))!);
        }
        return filas;
    }

    private static async Task<object?> EscalarAsync(string cadena, string sql)
    {
        await using NpgsqlConnection conexion = new(cadena);
        await conexion.OpenAsync();
        await using NpgsqlCommand comando = new(sql, conexion);
        return await comando.ExecuteScalarAsync();
    }

    private static string Insercion(Guid id, string instante, long? secuencia = null, string productor = "prueba") => $@"
        INSERT INTO registro (id_evento, instante_emision, tipo, nombre, version, productor, secuencia_productor,
            instante_registro, clave_idempotencia, procedencia, sobre)
        VALUES ('{id}', '{instante}', 'EV-18', 'proyecto.creado', 1, '{productor}', {(secuencia?.ToString() ?? "NULL")},
            now(), 'clave:{id}', '{{}}', '{{}}')";

    [Fact]
    public async Task El_migrador_deja_la_base_al_dia_y_se_puede_repetir()
    {
        await Migracion.MigrarAsync(_base.Dueno);
        await using AuditoriaDbContext dueno = new(new DbContextOptionsBuilder<AuditoriaDbContext>().UseNpgsql(_base.Dueno).Options);
        Assert.Empty(await dueno.Database.GetPendingMigrationsAsync());
        // La tabla particionada se escribió a mano en la migración: el modelo de EF sigue coincidiendo con el snapshot.
        Assert.False(dueno.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task El_registro_esta_particionado_por_mes_con_el_anio_en_curso_el_siguiente_y_una_particion_por_omision()
    {
        Assert.Equal("r", (await ListaAsync(_base.Dueno,
            "SELECT partstrat::text FROM pg_partitioned_table WHERE partrelid = 'registro'::regclass")).Single());
        List<string> particiones = await ListaAsync(_base.Dueno, @"
            SELECT c.relname FROM pg_inherits i JOIN pg_class c ON c.oid = i.inhrelid
            WHERE i.inhparent = 'registro'::regclass");

        int anio = DateTime.UtcNow.Year;
        IEnumerable<string> esperadas = Enumerable.Range(0, 24)
            .Select(m => new DateTime(anio, 1, 1).AddMonths(m))
            .Select(f => $"registro_{f:yyyy_MM}");
        Assert.All(esperadas, p => Assert.Contains(p, particiones));
        Assert.Contains("registro_fuera_de_rango", particiones);
    }

    [Fact]
    public async Task Un_evento_va_a_la_particion_de_su_mes_y_uno_sin_particion_no_se_rechaza()
    {
        Guid actual = Guid.NewGuid();
        Guid antiguo = Guid.NewGuid();
        await using (NpgsqlConnection conexion = new(_base.App))
        {
            await conexion.OpenAsync();
            await new NpgsqlCommand(Insercion(actual, DateTime.UtcNow.ToString("O")), conexion).ExecuteNonQueryAsync();
            await new NpgsqlCommand(Insercion(antiguo, "2001-03-04T05:06:07Z"), conexion).ExecuteNonQueryAsync();
        }
        Assert.Equal($"registro_{DateTime.UtcNow:yyyy_MM}",
            await EscalarAsync(_base.Dueno, $"SELECT tableoid::regclass::text FROM registro WHERE id_evento = '{actual}'"));
        Assert.Equal("registro_fuera_de_rango",
            await EscalarAsync(_base.Dueno, $"SELECT tableoid::regclass::text FROM registro WHERE id_evento = '{antiguo}'"));
    }

    [Theory]
    [InlineData("UPDATE registro SET actor = 'otro'")]
    [InlineData("DELETE FROM registro")]
    [InlineData("TRUNCATE registro")]
    [InlineData("UPDATE registro_fuera_de_rango SET actor = 'otro'")]
    [InlineData("DELETE FROM registro_fuera_de_rango")]
    [InlineData("SELECT count(*) FROM registro_fuera_de_rango")]
    [InlineData("ALTER TABLE registro DETACH PARTITION registro_fuera_de_rango")]
    [InlineData("DROP TABLE registro")]
    [InlineData("CREATE TABLE intrusa (id int)")]
    [InlineData("UPDATE mensajes_procesados SET tipo = 'otro'")]
    [InlineData("DELETE FROM mensajes_procesados")]
    [InlineData("UPDATE huecos_secuencia SET detectado_en = now()")]
    [InlineData("UPDATE huecos_secuencia SET secuencia = 1")]
    [InlineData("DELETE FROM huecos_secuencia")]
    [InlineData("DELETE FROM seguimiento_productor")]
    [InlineData("UPDATE seguimiento_productor SET productor = 'otro'")]
    [InlineData("SELECT crear_particiones_registro(DATE '1990-01-01', 500)")]
    public async Task El_servicio_no_puede_modificar_ni_borrar_el_registro_ni_cambiar_el_esquema(string sql)
    {
        // AD-41 y QA-13 (b): 0 entradas modificables o eliminables. Lo garantiza la base, no el código.
        await using NpgsqlConnection conexion = new(_base.App);
        await conexion.OpenAsync();
        await using NpgsqlCommand comando = new(sql, conexion);
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
        Assert.Contains(error.SqlState, new[] { PostgresErrorCodes.InsufficientPrivilege, PostgresErrorCodes.RaiseException });
    }

    [Fact]
    public async Task El_servicio_crea_particiones_por_adelantado_solo_dentro_del_limite()
    {
        object? creadas = await EscalarAsync(_base.App, "SELECT asegurar_particiones_registro(36)");
        Assert.True(Convert.ToInt32(creadas) >= 0);
        string lejana = $"registro_{DateTime.UtcNow.AddMonths(36):yyyy_MM}";
        Assert.Contains(lejana, await ListaAsync(_base.Dueno,
            "SELECT c.relname FROM pg_inherits i JOIN pg_class c ON c.oid = i.inhrelid WHERE i.inhparent = 'registro'::regclass"));
        // Repetirlo no crea nada nuevo.
        Assert.Equal(0, Convert.ToInt32(await EscalarAsync(_base.App, "SELECT asegurar_particiones_registro(36)")));

        foreach (string fuera in new[] { "0", "37", "NULL" })
        {
            PostgresException error = await Assert.ThrowsAsync<PostgresException>(
                () => EscalarAsync(_base.App, $"SELECT asegurar_particiones_registro({fuera})"));
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
        }
    }

    [Fact]
    public async Task La_llave_impide_registrar_dos_veces_el_mismo_evento()
    {
        Guid id = Guid.NewGuid();
        string instante = DateTime.UtcNow.ToString("O");
        await using NpgsqlConnection conexion = new(_base.App);
        await conexion.OpenAsync();
        await new NpgsqlCommand(Insercion(id, instante), conexion).ExecuteNonQueryAsync();
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(
            () => new NpgsqlCommand(Insercion(id, instante), conexion).ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
    }

    [Fact]
    public async Task Los_huecos_se_detectan_y_reportan_con_los_adaptadores_reales_y_el_rol_del_servicio()
    {
        // R-02 con la base real: secuencia 1, 2, 4 del productor A; el 3 de B llega antes del plazo.
        string a = "huecos-a-" + Guid.NewGuid().ToString("N")[..6];
        string b = "huecos-b-" + Guid.NewGuid().ToString("N")[..6];
        await using (NpgsqlConnection conexion = new(_base.App))
        {
            await conexion.OpenAsync();
            foreach ((string productor, long s) in new[] { (a, 1L), (a, 2L), (a, 4L), (b, 1L), (b, 2L), (b, 4L) })
            {
                await new NpgsqlCommand(Insercion(Guid.NewGuid(), DateTime.UtcNow.ToString("O"), s, productor), conexion).ExecuteNonQueryAsync();
            }
        }

        // En segundos enteros: la base guarda microsegundos y la comparación debe ser exacta.
        DateTimeOffset t0 = new(DateTimeOffset.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero);
        RelojDePrueba reloj = new(t0);
        await using ServiceProvider servicios = _base.Proveedor(reloj, new OpcionesAuditoria { PlazoHuecoSegundos = 60 });
        ProcesoRevisionHuecos proceso = servicios.GetRequiredService<ProcesoRevisionHuecos>();

        Assert.NotNull(await proceso.RevisarUnaVezAsync(CancellationToken.None));

        reloj.Ahora = t0.AddSeconds(30);
        await using (NpgsqlConnection conexion = new(_base.App))
        {
            await conexion.OpenAsync();
            await new NpgsqlCommand(Insercion(Guid.NewGuid(), DateTime.UtcNow.ToString("O"), 3, b), conexion).ExecuteNonQueryAsync();
        }
        await proceso.RevisarUnaVezAsync(CancellationToken.None);

        reloj.Ahora = t0.AddSeconds(61);
        await proceso.RevisarUnaVezAsync(CancellationToken.None);
        reloj.Ahora = t0.AddSeconds(200);
        await proceso.RevisarUnaVezAsync(CancellationToken.None);

        await using AuditoriaDbContext db = _base.ComoApp();
        FilaHueco hueco = await db.Huecos.SingleAsync(h => h.Productor == a);
        Assert.Equal(3, hueco.Secuencia);
        Assert.Equal(t0.AddSeconds(61), hueco.ReportadoEn);
        Assert.Null(hueco.CubiertoEn);
        Assert.Equal(2, (await db.Seguimientos.SingleAsync(s => s.Productor == a)).MayorContiguo);

        FilaHueco breve = await db.Huecos.SingleAsync(h => h.Productor == b);
        Assert.Null(breve.ReportadoEn);
        Assert.Equal(t0.AddSeconds(30), breve.CubiertoEn);
        Assert.Equal(4, (await db.Seguimientos.SingleAsync(s => s.Productor == b)).MayorContiguo);

        // El evento perdido de A llega por fin: el hueco reportado queda cubierto y se conserva en el rastro.
        await using (NpgsqlConnection conexion = new(_base.App))
        {
            await conexion.OpenAsync();
            await new NpgsqlCommand(Insercion(Guid.NewGuid(), DateTime.UtcNow.ToString("O"), 3, a), conexion).ExecuteNonQueryAsync();
        }
        reloj.Ahora = t0.AddSeconds(300);
        await proceso.RevisarUnaVezAsync(CancellationToken.None);
        await using AuditoriaDbContext despues = _base.ComoApp();
        FilaHueco cubierto = await despues.Huecos.SingleAsync(h => h.Productor == a);
        Assert.Equal(t0.AddSeconds(61), cubierto.ReportadoEn);
        Assert.Equal(t0.AddSeconds(300), cubierto.CubiertoEn);
        Assert.Equal(4, (await despues.Seguimientos.SingleAsync(s => s.Productor == a)).MayorContiguo);
    }

    [Fact]
    public async Task Solo_una_replica_revisa_las_secuencias_a_la_vez()
    {
        await using ServiceProvider servicios = _base.Proveedor(new RelojDePrueba(DateTimeOffset.UtcNow));
        ProcesoRevisionHuecos proceso = servicios.GetRequiredService<ProcesoRevisionHuecos>();

        // Otra réplica tiene el candado.
        await using NpgsqlConnection otra = new(_base.App);
        await otra.OpenAsync();
        await using NpgsqlTransaction transaccion = await otra.BeginTransactionAsync();
        await new NpgsqlCommand($"SELECT pg_advisory_xact_lock({ProcesoRevisionHuecos.Candado})", otra, transaccion).ExecuteNonQueryAsync();

        Assert.Null(await proceso.RevisarUnaVezAsync(CancellationToken.None));
        await transaccion.RollbackAsync();
        Assert.NotNull(await proceso.RevisarUnaVezAsync(CancellationToken.None));
    }

    [Fact]
    public async Task La_consulta_filtra_por_fechas_y_pagina_en_orden_cronologico()
    {
        string actor = "actor-" + Guid.NewGuid().ToString("N")[..6];
        DateTimeOffset t0 = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        await using (NpgsqlConnection conexion = new(_base.App))
        {
            await conexion.OpenAsync();
            // Insertadas en desorden.
            foreach (int minuto in new[] { 5, 1, 3, 2, 4 })
            {
                await new NpgsqlCommand($@"
                    INSERT INTO registro (id_evento, instante_emision, tipo, nombre, version, productor, instante_registro,
                        clave_idempotencia, actor, procedencia, sobre)
                    VALUES ('{Guid.NewGuid()}', '{t0.AddMinutes(minuto):O}', 'EV-20', 'proyecto.estado-avance-cambiado', 1,
                        'reconstruccion', now(), 'c:{Guid.NewGuid()}', '{actor}',
                        '{{""fuente"":""plataforma"",""sistemaOrigen"":null,""confianza"":""ALTA""}}', '{{""minuto"":{minuto}}}')", conexion)
                    .ExecuteNonQueryAsync();
            }
        }
        await using ServiceProvider servicios = _base.Proveedor(new RelojDePrueba(DateTimeOffset.UtcNow));
        using IServiceScope ambito = servicios.CreateScope();
        IConsultarRegistroEntrada consultar = ambito.ServiceProvider.GetRequiredService<IConsultarRegistroEntrada>();
        Salida<Aplicacion.PaginaRegistroDto> salida = new();
        await consultar.EjecutarAsync(new SolicitudConsultaRegistro(
            new Dominio.Solicitante("contraloria", new HashSet<string> { "auditor" }),
            new Aplicacion.FiltroRegistro(Actor: actor, Desde: t0.AddMinutes(2), Hasta: t0.AddMinutes(4)), 1, 2), salida);

        Assert.Equal(3, salida.Respuesta!.Total);
        Assert.Equal([t0.AddMinutes(2), t0.AddMinutes(3)], salida.Respuesta.Entradas.Select(e => e.InstanteEmision));
        Assert.Equal("ALTA", salida.Respuesta.Entradas[0].Procedencia.Confianza);
    }
}

public sealed class Salida<T> : Aplicacion.Puertos.IPuertoSalida<T>
{
    public T? Respuesta { get; private set; }
    public string? Error { get; private set; }
    public void Exito(T respuesta) => Respuesta = respuesta;
    public void NoAutorizado(string mensaje) => Error = "NO_AUTORIZADO";
    public void ReglaIncumplida(string codigo, string mensaje) => Error = codigo;
}
