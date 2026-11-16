using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Plataforma.Comun.Mensajeria;
using Plataforma.Comun.Migraciones;

namespace AdaptadorSe2;

/// <summary>Una versión de una capa que ya se dejó en la bandeja. Evita republicar la misma versión.</summary>
public sealed class CapaPublicada
{
    public string Capa { get; set; } = "";
    public int VersionCapa { get; set; }
    public string Hash { get; set; } = "";
    public int Partes { get; set; }
    public int Divisiones { get; set; }
    public DateTimeOffset Instante { get; set; }
}

/// <summary>Base propia del adaptador (AD-01): su bandeja de salida (AD-45) y el registro de capas publicadas.</summary>
public sealed class AdaptadorSe2DbContext(DbContextOptions<AdaptadorSe2DbContext> opciones) : DbContext(opciones)
{
    public DbSet<CapaPublicada> CapasPublicadas => Set<CapaPublicada>();
    public DbSet<FilaBandeja> Bandeja => Set<FilaBandeja>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.ConfigurarBandejaSalida();
        modelo.Entity<CapaPublicada>(e =>
        {
            e.ToTable("capas_publicadas");
            e.HasKey(x => new { x.Capa, x.VersionCapa });
            e.Property(x => x.Capa).HasColumnName("capa").HasMaxLength(20);
            e.Property(x => x.VersionCapa).HasColumnName("version_capa");
            e.Property(x => x.Hash).HasColumnName("hash").HasMaxLength(64).IsRequired();
            e.Property(x => x.Partes).HasColumnName("partes");
            e.Property(x => x.Divisiones).HasColumnName("divisiones");
            e.Property(x => x.Instante).HasColumnName("instante");
        });
    }
}

/// <summary>Despliegue del esquema del adaptador (DT-19): rol de mínimo privilegio y migraciones con el rol dueño.</summary>
public static class MigracionAdaptadorSe2
{
    public const string RolApp = "adaptador_se2_app";

    public static Task EjecutarAsync(IConfiguration configuracion, ILogger log, CancellationToken ct = default)
    {
        string dueno = Exigir(configuracion, "ConnectionStrings:AdaptadorSe2Dueno");
        RolBase[] roles = [new(RolApp, Exigir(configuracion, "Migracion:ClaveAdaptadorSe2App"))];
        BaseDelServicio[] bases = [new("adaptador_se2", () => new AdaptadorSe2DbContext(Opciones(dueno)))];
        // Los roles se crean conectado a la base de mantenimiento: la del adaptador aún no existe la primera vez.
        string administracion = new NpgsqlConnectionStringBuilder(dueno) { Database = "postgres" }.ConnectionString;
        return Migrador.EjecutarAsync(administracion, roles, bases, sembrar: false, log, ct);
    }

    public static DbContextOptions<AdaptadorSe2DbContext> Opciones(string cadena) =>
        new DbContextOptionsBuilder<AdaptadorSe2DbContext>().UseNpgsql(cadena).Options;

    private static string Exigir(IConfiguration configuracion, string clave) =>
        configuracion[clave] is { Length: > 0 } valor ? valor : throw new InvalidOperationException($"Falta {clave}.");
}

/// <summary>Para "dotnet ef migrations add": no se conecta a ninguna base.</summary>
public sealed class FabricaDisenoAdaptadorSe2 : IDesignTimeDbContextFactory<AdaptadorSe2DbContext>
{
    public AdaptadorSe2DbContext CreateDbContext(string[] args) =>
        new(MigracionAdaptadorSe2.Opciones("Host=localhost;Database=adaptador_se2"));
}
