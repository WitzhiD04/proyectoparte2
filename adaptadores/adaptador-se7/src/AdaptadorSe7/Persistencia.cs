using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;
using Plataforma.Comun.Adaptadores;
using Plataforma.Comun.Mensajeria;
using Plataforma.Comun.Migraciones;

namespace AdaptadorSe7;

/// <summary>
/// Base del adaptador (DA-11): la base compartida "adaptadores", con un esquema por adaptador (se7). Guarda la bandeja de
/// salida (EV-23 y EV-24, AD-45), el registro de mensajes procesados del consumidor idempotente y la marca de avance de
/// la entrada (AD-14). Los adaptadores son efímeros (AD-25): no guardan nada fuera de aquí.
/// </summary>
public sealed class AdaptadorSe7DbContext(DbContextOptions<AdaptadorSe7DbContext> opciones) : DbContext(opciones)
{
    public const string Esquema = "se7";

    public DbSet<FilaBandeja> Bandeja => Set<FilaBandeja>();
    public DbSet<FilaMensajeProcesado> Procesados => Set<FilaMensajeProcesado>();
    public DbSet<FilaMarcaAvance> Marcas => Set<FilaMarcaAvance>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.HasDefaultSchema(Esquema);
        modelo.ConfigurarBandejaSalida();
        modelo.ConfigurarMensajesProcesados();
        modelo.ConfigurarMarcasAvance();
    }
}

/// <summary>Despliegue del esquema del adaptador (DT-19): rol de mínimo privilegio y migraciones con el rol dueño.</summary>
public static class MigracionAdaptadorSe7
{
    public const string RolApp = "adaptador_se7_app";
    public const string Base = "adaptadores";

    public static Task EjecutarAsync(IConfiguration configuracion, ILogger log, CancellationToken ct = default)
    {
        string dueno = Exigir(configuracion, "ConnectionStrings:AdaptadorSe7Dueno");
        RolBase[] roles = [new(RolApp, Exigir(configuracion, "Migracion:ClaveAdaptadorSe7App"))];
        BaseDelServicio[] bases = [new(Base, () => new AdaptadorSe7DbContext(Opciones(dueno)))];
        return Migrador.EjecutarAsync(dueno, roles, bases, sembrar: false, log, ct);
    }

    /// <summary>
    /// Opciones del DbContext. La ruta de búsqueda queda fija en el esquema del adaptador: la mensajería de comun usa SQL
    /// sin esquema (bandeja_salida, contador_productor), y así resuelve a las tablas de se7 y no a las de otro adaptador.
    /// </summary>
    public static DbContextOptions<AdaptadorSe7DbContext> Opciones(string cadena)
    {
        DbContextOptionsBuilder<AdaptadorSe7DbContext> opciones = new();
        Usar(opciones, cadena);
        return opciones.Options;
    }

    public static void Usar(DbContextOptionsBuilder opciones, string cadena)
    {
        string conEsquema = new NpgsqlConnectionStringBuilder(cadena) { SearchPath = AdaptadorSe7DbContext.Esquema }.ConnectionString;
        opciones.UseNpgsql(conEsquema, o => o.MigrationsHistoryTable("__EFMigrationsHistory", AdaptadorSe7DbContext.Esquema));
    }

    private static string Exigir(IConfiguration configuracion, string clave) =>
        configuracion[clave] is { Length: > 0 } valor ? valor : throw new InvalidOperationException($"Falta {clave}.");
}

/// <summary>Para "dotnet ef migrations add": no se conecta a ninguna base.</summary>
public sealed class FabricaDisenoAdaptadorSe7 : IDesignTimeDbContextFactory<AdaptadorSe7DbContext>
{
    public AdaptadorSe7DbContext CreateDbContext(string[] args) =>
        new(MigracionAdaptadorSe7.Opciones("Host=localhost;Database=adaptadores"));
}
