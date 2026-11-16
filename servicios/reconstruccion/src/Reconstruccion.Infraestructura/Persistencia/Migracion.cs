using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Plataforma.Comun.Migraciones;

namespace Reconstruccion.Infraestructura.Persistencia;

/// <summary>
/// Despliegue del esquema de Reconstrucción y de la Consulta del Programa (DT-19). Corre con "migrar" y con las
/// cadenas del dueño, crea los roles de aplicación y aplica las migraciones, que incluyen los permisos.
/// </summary>
public static class MigracionReconstruccion
{
    public const string RolEscritura = "reconstruccion_app";
    public const string RolConsulta = "programa_app";
    public const string RolReplicacion = "replicador";

    public static Task EjecutarAsync(IConfiguration configuracion, bool sembrar, ILogger log, CancellationToken ct = default)
    {
        string dueno = Exigir(configuracion, "ConnectionStrings:ReconstruccionDueno");
        string duenoPrograma = Exigir(configuracion, "ConnectionStrings:ProgramaDueno");
        RolBase[] roles =
        [
            new(RolEscritura, Exigir(configuracion, "Migracion:ClaveReconstruccionApp")),
            new(RolConsulta, Exigir(configuracion, "Migracion:ClaveProgramaApp")),
            // La réplica de la base copia los datos de forma continua (segunda ubicación).
            new(RolReplicacion, Exigir(configuracion, "Migracion:ClaveReplicador"), Replicacion: true),
        ];
        BaseDelServicio[] bases =
        [
            new("reconstruccion", () => new ReconstruccionDbContext(Opciones<ReconstruccionDbContext>(dueno)), LeerSiembra),
            new("programa", () => new ProgramaDbContext(Opciones<ProgramaDbContext>(duenoPrograma))),
        ];
        return Migrador.EjecutarAsync(dueno, roles, bases, sembrar, log, ct);
    }

    private static DbContextOptions<T> Opciones<T>(string cadena) where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseNpgsql(cadena).Options;

    private static string LeerSiembra()
    {
        using Stream flujo = Assembly.GetExecutingAssembly().GetManifestResourceStream("siembra-datos-prueba.sql")
            ?? throw new FileNotFoundException("Falta la siembra de datos de prueba.");
        using StreamReader lector = new(flujo);
        return lector.ReadToEnd();
    }

    private static string Exigir(IConfiguration configuracion, string clave) =>
        configuracion[clave] is { Length: > 0 } valor ? valor : throw new InvalidOperationException($"Falta {clave}.");
}

/// <summary>Para "dotnet ef migrations add": no se conecta a ninguna base.</summary>
public sealed class FabricaDisenoReconstruccion : IDesignTimeDbContextFactory<ReconstruccionDbContext>
{
    public ReconstruccionDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ReconstruccionDbContext>().UseNpgsql("Host=localhost;Database=reconstruccion").Options);
}

public sealed class FabricaDisenoPrograma : IDesignTimeDbContextFactory<ProgramaDbContext>
{
    public ProgramaDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ProgramaDbContext>().UseNpgsql("Host=localhost;Database=programa").Options);
}
