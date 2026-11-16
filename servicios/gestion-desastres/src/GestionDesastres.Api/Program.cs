using GestionDesastres.Aplicacion.CasosDeUso;
using GestionDesastres.Infraestructura;
using Plataforma.Comun.Salud;
using Plataforma.Comun.Seguridad;

// Microservicio de Gestión de Desastres, en su versión mínima (DA-10): catálogo de tipos y perfiles en ejecución,
// registro manual, reclasificación, transiciones, delimitación versionada y organismos. Publica EV-03 a EV-06 y EV-27 y
// consume EV-18, EV-20 y EV-21. Con el argumento "migrar" es el migrador del esquema (DT-19).
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

if (args.Contains("migrar"))
{
    using ILoggerFactory registros = LoggerFactory.Create(l => l.AddConsole());
    ILogger log = registros.CreateLogger("Migrador");
    try
    {
        await MigracionGestionDesastres.EjecutarAsync(builder.Configuration, log);
        return 0;
    }
    catch (Exception e)
    {
        log.LogCritical(e, "La migración falló.");
        return 1;
    }
}

builder.Services.AddControllers();
builder.Services.AgregarIdentidad(builder.Configuration);
builder.Services.AgregarInfraestructura(builder.Configuration);

builder.Services.AddScoped<GuardadoDeDesastres>();
builder.Services.AddScoped<IRegistrarDesastreEntrada, RegistrarDesastre>();
builder.Services.AddScoped<IReclasificarEntrada, Reclasificar>();
builder.Services.AddScoped<ICambiarEstadoEntrada, CambiarEstado>();
builder.Services.AddScoped<IDelimitarEntrada, Delimitar>();
builder.Services.AddScoped<IAsignarOrganismosEntrada, AsignarOrganismos>();
builder.Services.AddScoped<IConsultarDesastreEntrada, ConsultarDesastre>();
builder.Services.AddScoped<IConsultarHistorialEntrada, ConsultarHistorial>();
builder.Services.AddScoped<AdministracionCatalogo>();
builder.Services.AddScoped<IGuardarTipoEntrada>(s => s.GetRequiredService<AdministracionCatalogo>());
builder.Services.AddScoped<IListarTiposEntrada>(s => s.GetRequiredService<AdministracionCatalogo>());
builder.Services.AddScoped<IPerfilDeTipoEntrada>(s => s.GetRequiredService<AdministracionCatalogo>());
builder.Services.AddScoped<IListarPerfilesEntrada>(s => s.GetRequiredService<AdministracionCatalogo>());
builder.Services.AddScoped<IGuardarPerfilEntrada>(s => s.GetRequiredService<AdministracionCatalogo>());
builder.Services.AddScoped<IConsultarPerfilPorNivelEntrada>(s => s.GetRequiredService<AdministracionCatalogo>());
builder.Services.AddScoped<IGuardarPerfilPorNivelEntrada>(s => s.GetRequiredService<AdministracionCatalogo>());
builder.Services.AddScoped<ActualizarProyectoDelDesastre>();
builder.Services.AddScoped<ActualizarCapaMunicipios>();

WebApplication app = builder.Build();
app.MapearSalud("gestion-desastres");
app.MapControllers();
await app.RunAsync();
return 0;

/// <summary>Para las pruebas con WebApplicationFactory.</summary>
public partial class Program;
