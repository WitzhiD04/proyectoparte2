using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Infraestructura.Archivos;
using Reconstruccion.Infraestructura.Mensajeria;
using Reconstruccion.Infraestructura.Persistencia;

namespace Reconstruccion.Infraestructura;

public static class Registro
{
    /// <summary>Registra la implementación de cada puerto según el rol con el que corre el servicio.</summary>
    public static IServiceCollection AgregarInfraestructura(this IServiceCollection servicios, IConfiguration configuracion, bool escritura, bool consulta)
    {
        servicios.AddSingleton<IReloj, RelojDelSistema>();

        if (escritura)
        {
            servicios.AddDbContext<ReconstruccionDbContext>(o => o.UseNpgsql(configuracion.GetConnectionString("Reconstruccion")));
            servicios.AddScoped<IRepositorioProyectos, RepositorioProyectosEf>();
            servicios.AddScoped<IHistorial, HistorialEf>();
            servicios.AddScoped<IBandejaSalida, BandejaSalidaEf>();
            servicios.AddScoped<ICopiasLocales, CopiasLocalesEf>();
            servicios.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajoEf>();
            servicios.AddSingleton<IAlmacenDocumentos, AlmacenDocumentosMinio>();
            servicios.AddHostedService<RelevoEventos>();
        }

        if (consulta)
        {
            servicios.AddDbContext<ProgramaDbContext>(o => o.UseNpgsql(configuracion.GetConnectionString("Programa")));
            servicios.AddScoped<IProyeccionPrograma, ProyeccionProgramaEf>();
            servicios.AddHostedService<ConsumidorProyeccion>();
        }
        return servicios;
    }
}
