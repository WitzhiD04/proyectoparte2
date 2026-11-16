using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Reconstruccion.Dominio;
using Reconstruccion.Infraestructura.Persistencia;
using Testcontainers.PostgreSql;

namespace Reconstruccion.Integracion;

/// <summary>Un PostgreSQL 18 con el esquema aplicado por el migrador, igual que en el despliegue (DT-19).</summary>
public sealed class BaseMigrada : IAsyncLifetime
{
    public const string ClaveApp = "reconstruccion_prueba";
    public const string ClavePrograma = "programa_prueba";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.4")
        .WithDatabase("reconstruccion").Build();

    public string Dueno => _postgres.GetConnectionString();
    public string DuenoPrograma => new NpgsqlConnectionStringBuilder(Dueno) { Database = "programa" }.ConnectionString;
    public string App => new NpgsqlConnectionStringBuilder(Dueno) { Username = "reconstruccion_app", Password = ClaveApp }.ConnectionString;
    public string AppPrograma => new NpgsqlConnectionStringBuilder(DuenoPrograma) { Username = "programa_app", Password = ClavePrograma }.ConnectionString;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        IConfiguration configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ReconstruccionDueno"] = Dueno,
            ["ConnectionStrings:ProgramaDueno"] = DuenoPrograma,
            ["Migracion:ClaveReconstruccionApp"] = ClaveApp,
            ["Migracion:ClaveProgramaApp"] = ClavePrograma,
            ["Migracion:ClaveReplicador"] = "replicador_prueba",
        }).Build();
        await MigracionReconstruccion.EjecutarAsync(configuracion, sembrar: true, NullLogger.Instance);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}

public class PruebasPersistencia : IClassFixture<BaseMigrada>
{
    private readonly BaseMigrada _base;
    public PruebasPersistencia(BaseMigrada baseMigrada) { _base = baseMigrada; }

    private ReconstruccionDbContext ComoApp() =>
        new(new DbContextOptionsBuilder<ReconstruccionDbContext>().UseNpgsql(_base.App).Options);

    [Fact]
    public async Task El_migrador_deja_las_dos_bases_al_dia_y_con_los_datos_de_prueba()
    {
        // El historial de migraciones solo lo lee el dueño: el rol de la aplicación no tiene acceso a él.
        await using ReconstruccionDbContext dueno = new(new DbContextOptionsBuilder<ReconstruccionDbContext>().UseNpgsql(_base.Dueno).Options);
        Assert.Empty(await dueno.Database.GetPendingMigrationsAsync());
        await using ProgramaDbContext duenoPrograma = new(new DbContextOptionsBuilder<ProgramaDbContext>().UseNpgsql(_base.DuenoPrograma).Options);
        Assert.Empty(await duenoPrograma.Database.GetPendingMigrationsAsync());

        await using ReconstruccionDbContext db = ComoApp();
        Assert.Equal(9, await db.Evaluaciones.CountAsync());
        Assert.Equal("63130", (await db.Evaluaciones.SingleAsync(e => e.Id == Guid.Parse("aaaaaaaa-0000-0000-0000-000000000006"))).CodigoMunicipio);
    }

    [Fact]
    public async Task Migrar_dos_veces_no_falla_ni_duplica()
    {
        IConfiguration configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ReconstruccionDueno"] = _base.Dueno,
            ["ConnectionStrings:ProgramaDueno"] = _base.DuenoPrograma,
            ["Migracion:ClaveReconstruccionApp"] = BaseMigrada.ClaveApp,
            ["Migracion:ClaveProgramaApp"] = BaseMigrada.ClavePrograma,
            ["Migracion:ClaveReplicador"] = "replicador_prueba",
        }).Build();
        await MigracionReconstruccion.EjecutarAsync(configuracion, sembrar: true, NullLogger.Instance);

        await using ReconstruccionDbContext db = ComoApp();
        Assert.Equal(9, await db.Evaluaciones.CountAsync());
    }

    [Theory]
    [InlineData("UPDATE historial SET autor = 'otro'")]
    [InlineData("DELETE FROM historial")]
    [InlineData("UPDATE registros_costo SET monto = 1")]
    [InlineData("DELETE FROM registros_avance")]
    [InlineData("DELETE FROM documentos")]
    [InlineData("CREATE TABLE intrusa (id int)")]
    [InlineData("DROP TABLE proyectos")]
    public async Task El_servicio_no_puede_reescribir_el_historial_ni_cambiar_el_esquema(string sql)
    {
        // AD-41 y QA-15: el almacén no admite actualización ni borrado del historial. Lo garantiza la base, no el código.
        await using NpgsqlConnection conexion = new(_base.App);
        await conexion.OpenAsync();
        await using NpgsqlCommand comando = new(sql, conexion);
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
        Assert.Contains(error.SqlState, new[] { PostgresErrorCodes.InsufficientPrivilege, "42501" });
    }

    [Fact]
    public async Task El_servicio_si_puede_insertar_en_el_historial()
    {
        await using ReconstruccionDbContext db = ComoApp();
        ProyectoFila proyecto = new()
        {
            Id = Guid.NewGuid(), IdDesastre = Guid.NewGuid(), IdActivo = Guid.NewGuid(), Nombre = "Prueba",
            Alcance = "Municipal", Estado = "Formulado", Version = 1,
        };
        db.Proyectos.Add(proyecto);
        db.Historial.Add(new EntradaHistorialFila
        {
            Id = Guid.NewGuid(), IdProyecto = proyecto.Id, Version = 1, TipoCambio = nameof(TipoCambio.Creacion),
            ValorNuevo = "Formulado", Autor = "prueba", Justificacion = "prueba", Instante = DateTimeOffset.UtcNow,
            Procedencia = ProyectoReconstruccion.ProcedenciaPlataforma,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Una_evaluacion_no_puede_quedar_vigente_en_dos_proyectos()
    {
        await using ReconstruccionDbContext db = ComoApp();
        Guid evaluacion = Guid.NewGuid();
        foreach (int i in new[] { 1, 2 })
        {
            ProyectoFila p = new()
            {
                Id = Guid.NewGuid(), IdDesastre = Guid.NewGuid(), IdActivo = Guid.NewGuid(), Nombre = $"P{i}",
                Alcance = "Municipal", Estado = "Formulado", Version = 1,
            };
            p.Evaluaciones.Add(new EvaluacionOrigenFila
            {
                IdProyecto = p.Id, IdEvaluacion = evaluacion, IdActivo = p.IdActivo, TipoActivo = "Escuela",
                NivelDano = "Grave", CodigoMunicipio = "63001", Vigente = true,
            });
            db.Proyectos.Add(p);
        }
        DbUpdateException error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)error.InnerException!).SqlState);
    }
}
