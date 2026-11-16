using GestionDesastres.Aplicacion;
using GestionDesastres.Infraestructura.Mensajeria;
using GestionDesastres.Infraestructura.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plataforma.Comun.Mensajeria;
using Plataforma.Comun.Migraciones;

namespace GestionDesastres.Infraestructura;

/// <summary>Conecta cada puerto con su adaptador.</summary>
public static class Registro
{
    public static IServiceCollection AgregarInfraestructura(this IServiceCollection servicios, IConfiguration configuracion)
    {
        string cadena = configuracion.GetConnectionString("GestionDesastres")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:GestionDesastres.");
        servicios.AddDbContext<GestionDesastresDbContext>(o => o.UseNpgsql(cadena, n => n.UseNetTopologySuite()));
        servicios.AddHealthChecks().AddDbContextCheck<GestionDesastresDbContext>("base", tags: ["listo"]);
        servicios.AddHttpContextAccessor();
        servicios.AddSingleton<IReloj, RelojDelSistema>();
        servicios.AddScoped<IRepositorioDesastres, RepositorioDesastresEf>();
        servicios.AddScoped<ICatalogo, CatalogoEf>();
        servicios.AddScoped<IProyectosDelDesastre, ProyectosDelDesastreEf>();
        servicios.AddScoped<ICapaMunicipios, CapaMunicipiosPostgis>();
        servicios.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajoEf>();
        servicios.AddScoped<IPublicadorHechos, PublicadorHechosBandeja>();

        // Bus: publica EV-03 a 06 y EV-27 por la bandeja, y consume q.gestion-desastres (EV-18, EV-20, EV-21).
        servicios.AgregarBus(configuracion);
        servicios.AgregarPublicacion<GestionDesastresDbContext>();
        servicios.AgregarConsumo<GestionDesastresDbContext, ManejadorEventosGestion>();
        return servicios;
    }
}

/// <summary>Despliegue del esquema (DT-19): roles, migraciones (con el catálogo inicial y los permisos) y nada más.</summary>
public static class MigracionGestionDesastres
{
    public const string RolAplicacion = "gestion_desastres_app";

    public static Task EjecutarAsync(IConfiguration configuracion, ILogger log, CancellationToken ct = default)
    {
        string dueno = configuracion.GetConnectionString("GestionDesastresDueno")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:GestionDesastresDueno.");
        string clave = configuracion["Migracion:ClaveGestionDesastresApp"] is { Length: > 0 } c
            ? c : throw new InvalidOperationException("Falta Migracion:ClaveGestionDesastresApp.");
        return Migrador.EjecutarAsync(dueno, [new RolBase(RolAplicacion, clave)],
            [new BaseDelServicio("gestion_desastres", () => new GestionDesastresDbContext(
                new DbContextOptionsBuilder<GestionDesastresDbContext>().UseNpgsql(dueno, n => n.UseNetTopologySuite()).Options))],
            sembrar: false, log, ct);
    }
}

/// <summary>Para "dotnet ef migrations add": no se conecta a ninguna base.</summary>
public sealed class FabricaDisenoGestionDesastres : IDesignTimeDbContextFactory<GestionDesastresDbContext>
{
    public GestionDesastresDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<GestionDesastresDbContext>()
        .UseNpgsql("Host=localhost;Database=gestion_desastres", n => n.UseNetTopologySuite()).Options);
}
