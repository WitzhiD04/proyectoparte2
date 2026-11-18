using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;
using Plataforma.Comun.Mensajeria;
using Plataforma.Comun.Migraciones;

namespace AdaptadorSe6;

/// <summary>
/// Base del adaptador (DA-11): la base compartida "adaptadores", con un esquema por adaptador (se6). Guarda la bandeja de
/// salida (EV-23, AD-45) y el registro de mensajes procesados del consumidor por lotes. Los adaptadores son efímeros
/// (AD-25): no guardan nada fuera de aquí.
/// </summary>
public sealed class AdaptadorSe6DbContext(DbContextOptions<AdaptadorSe6DbContext> opciones) : DbContext(opciones)
{
    public const string Esquema = "se6";

    public DbSet<FilaBandeja> Bandeja => Set<FilaBandeja>();
    public DbSet<FilaMensajeProcesado> Procesados => Set<FilaMensajeProcesado>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.HasDefaultSchema(Esquema);
        modelo.ConfigurarBandejaSalida();
        modelo.ConfigurarMensajesProcesados();
    }
}

/// <summary>Despliegue del esquema del adaptador (DT-19): rol de mínimo privilegio y migraciones con el rol dueño.</summary>
public static class MigracionAdaptadorSe6
{
    public const string RolApp = "adaptador_se6_app";
    public const string Base = "adaptadores";

    public static Task EjecutarAsync(IConfiguration configuracion, ILogger log, CancellationToken ct = default)
    {
        string dueno = Exigir(configuracion, "ConnectionStrings:AdaptadorSe6Dueno");
        RolBase[] roles = [new(RolApp, Exigir(configuracion, "Migracion:ClaveAdaptadorSe6App"))];
        BaseDelServicio[] bases = [new(Base, () => new AdaptadorSe6DbContext(Opciones(dueno)))];
        return Migrador.EjecutarAsync(dueno, roles, bases, sembrar: false, log, ct);
    }

    /// <summary>
    /// Opciones del DbContext. La ruta de búsqueda queda fija en el esquema del adaptador: la mensajería de comun usa SQL
    /// sin esquema (bandeja_salida, contador_productor), y así resuelve a las tablas de se6 y no a las de otro adaptador.
    /// </summary>
    public static DbContextOptions<AdaptadorSe6DbContext> Opciones(string cadena)
    {
        DbContextOptionsBuilder<AdaptadorSe6DbContext> opciones = new();
        Usar(opciones, cadena);
        return opciones.Options;
    }

    public static void Usar(DbContextOptionsBuilder opciones, string cadena)
    {
        string conEsquema = new NpgsqlConnectionStringBuilder(cadena) { SearchPath = AdaptadorSe6DbContext.Esquema }.ConnectionString;
        opciones.UseNpgsql(conEsquema, o => o.MigrationsHistoryTable("__EFMigrationsHistory", AdaptadorSe6DbContext.Esquema));
    }

    private static string Exigir(IConfiguration configuracion, string clave) =>
        configuracion[clave] is { Length: > 0 } valor ? valor : throw new InvalidOperationException($"Falta {clave}.");
}

/// <summary>Para "dotnet ef migrations add": no se conecta a ninguna base.</summary>
public sealed class FabricaDisenoAdaptadorSe6 : IDesignTimeDbContextFactory<AdaptadorSe6DbContext>
{
    public AdaptadorSe6DbContext CreateDbContext(string[] args) =>
        new(MigracionAdaptadorSe6.Opciones("Host=localhost;Database=adaptadores"));
}
