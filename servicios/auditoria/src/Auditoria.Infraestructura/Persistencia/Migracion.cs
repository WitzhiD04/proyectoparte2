using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Plataforma.Comun.Migraciones;

namespace Auditoria.Infraestructura.Persistencia;

/// <summary>
/// Despliegue del esquema de Auditoría (DT-19). Corre con "migrar" y con la cadena del dueño: crea el rol de la
/// aplicación y aplica las migraciones, que incluyen la tabla particionada, sus particiones y los permisos.
/// </summary>
public static class MigracionAuditoria
{
    public const string RolApp = "auditoria_app";
    public const string Base = "auditoria";

    public static Task EjecutarAsync(IConfiguration configuracion, ILogger log, CancellationToken ct = default)
    {
        string dueno = Exigir(configuracion, "ConnectionStrings:AuditoriaDueno");
        // Los roles se crean conectados a la base "postgres": la base del servicio todavía puede no existir (EF la crea
        // al migrar) y los roles son del servidor, no de una base.
        string administracion = new NpgsqlConnectionStringBuilder(dueno) { Database = "postgres" }.ConnectionString;
        RolBase[] roles = [new(RolApp, Exigir(configuracion, "Migracion:ClaveAuditoriaApp"))];
        BaseDelServicio[] bases =
        [
            new(Base, () => new AuditoriaDbContext(new DbContextOptionsBuilder<AuditoriaDbContext>().UseNpgsql(dueno).Options)),
        ];
        return Migrador.EjecutarAsync(administracion, roles, bases, sembrar: false, log, ct);
    }

    private static string Exigir(IConfiguration configuracion, string clave) =>
        configuracion[clave] is { Length: > 0 } valor ? valor : throw new InvalidOperationException($"Falta {clave}.");
}

/// <summary>Para "dotnet ef migrations add": no se conecta a ninguna base.</summary>
public sealed class FabricaDisenoAuditoria : IDesignTimeDbContextFactory<AuditoriaDbContext>
{
    public AuditoriaDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AuditoriaDbContext>().UseNpgsql("Host=localhost;Database=auditoria").Options);
}
