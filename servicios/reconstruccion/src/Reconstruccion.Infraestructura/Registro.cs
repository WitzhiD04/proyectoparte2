using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Comun.Mensajeria;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Infraestructura.Archivos;
using Reconstruccion.Infraestructura.Mensajeria;
using Reconstruccion.Infraestructura.Persistencia;

namespace Reconstruccion.Infraestructura;

/// <summary>Conecta cada puerto con su adaptador según el rol del servicio (AD-01: la misma imagen, dos servicios).</summary>
public static class Registro
{
    public const string RolEscritura = "escritura";
    public const string RolConsulta = "consulta";

    public static IServiceCollection AgregarInfraestructura(this IServiceCollection servicios, IConfiguration configuracion, string rol)
    {
        servicios.AddSingleton<IReloj, RelojDelSistema>();
        servicios.AgregarBus(configuracion);

        if (rol == RolEscritura)
        {
            servicios.AddDbContext<ReconstruccionDbContext>(o => o.UseNpgsql(Cadena(configuracion, "Reconstruccion")));
            servicios.AddHealthChecks().AddDbContextCheck<ReconstruccionDbContext>("base", tags: ["listo"]);
            servicios.AddHttpContextAccessor();
            servicios.AddScoped<IRepositorioProyectos, RepositorioProyectosEf>();
            servicios.AddScoped<IHistorial, HistorialEf>();
            servicios.AddScoped<ICopiasLocales, CopiasLocalesEf>();
            servicios.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajoEf>();
            servicios.AddScoped<IPublicadorHechos, PublicadorHechosBandeja>();
            servicios.AddSingleton<IAlmacenDocumentos, AlmacenDocumentosMinio>();
            // Bandeja de salida y relevo hacia el bus (AD-45, DA-04).
            servicios.AgregarPublicacion<ReconstruccionDbContext>();
        }
        else if (rol == RolConsulta)
        {
            servicios.AddDbContext<ProgramaDbContext>(o => o.UseNpgsql(Cadena(configuracion, "Programa")));
            servicios.AddHealthChecks().AddDbContextCheck<ProgramaDbContext>("base", tags: ["listo"]);
            servicios.AddScoped<IProyeccionPrograma, ProyeccionProgramaEf>();
            // Consumidor idempotente de q.consulta-programa, con su adaptador de entrada (DA-03).
            servicios.AgregarConsumo<ProgramaDbContext, ManejadorEventosPrograma>();
        }
        else
        {
            throw new InvalidOperationException($"Rol desconocido: {rol}. Use {RolEscritura} o {RolConsulta}.");
        }
        return servicios;
    }

    private static string Cadena(IConfiguration configuracion, string nombre) =>
        configuracion.GetConnectionString(nombre) ?? throw new InvalidOperationException($"Falta ConnectionStrings:{nombre}.");
}
