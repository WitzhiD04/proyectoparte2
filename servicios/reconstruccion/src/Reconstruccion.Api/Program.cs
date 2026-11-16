using Plataforma.Comun.Salud;
using Reconstruccion.Api.Presenters;
using Reconstruccion.Aplicacion.CasosDeUso;
using Reconstruccion.Infraestructura;
using Reconstruccion.Infraestructura.Persistencia;

// La misma imagen se despliega con dos roles (AD-01):
//   escritura -> servicio de Reconstrucción (proyectos, historial, bandeja de salida y relevo de eventos)
//   consulta  -> servicio de Consulta del Programa (modelo de lectura y consolidado, AD-42)
// Con el argumento "migrar" la imagen es el migrador (DT-19): aplica el esquema con el rol dueño y termina.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

if (args.Contains("migrar"))
{
    using ILoggerFactory registros = LoggerFactory.Create(l => l.AddConsole());
    ILogger log = registros.CreateLogger("Migrador");
    try
    {
        await MigracionReconstruccion.EjecutarAsync(builder.Configuration, sembrar: args.Contains("--sembrar"), log);
        return 0;
    }
    catch (Exception e)
    {
        log.LogCritical(e, "La migración falló.");
        return 1;
    }
}

// El rol es obligatorio: un proceso sin rol no debe levantar por error la escritura y la consulta juntas (H-36).
string rol = (builder.Configuration["Reconstruccion:Rol"] ?? "").Trim().ToLowerInvariant();
if (rol is not (Registro.RolEscritura or Registro.RolConsulta))
{
    throw new InvalidOperationException(
        $"Reconstruccion:Rol debe ser '{Registro.RolEscritura}' o '{Registro.RolConsulta}' (llegó '{rol}').");
}
bool escritura = rol == Registro.RolEscritura;

builder.Services.AddControllers();

// Aquí se conecta cada puerto con su implementación (adaptadores) según el rol.
builder.Services.AgregarInfraestructura(builder.Configuration, rol);

if (escritura)
{
    builder.Services.AddScoped<GuardadoDeProyectos>();
    builder.Services.AddScoped<IConsultarDesastresEntrada, ConsultarDesastres>();
    builder.Services.AddScoped<IConsultarCandidatasEntrada, ConsultarCandidatas>();
    builder.Services.AddScoped<ICrearProyectosPorLoteEntrada, CrearProyectosPorLote>();
    builder.Services.AddScoped<IConsultarProyectosEntrada, ConsultarProyectos>();
    builder.Services.AddScoped<IConsultarProyectoEntrada, ConsultarProyecto>();
    builder.Services.AddScoped<IAsignarPresupuestoEntrada, AsignarPresupuesto>();
    builder.Services.AddScoped<IAsignarResponsableEntrada, AsignarResponsable>();
    builder.Services.AddScoped<IAprobarProyectoEntrada, AprobarProyecto>();
    builder.Services.AddScoped<IIniciarEjecucionEntrada, IniciarEjecucion>();
    builder.Services.AddScoped<IRegistrarAvanceYCostoEntrada, RegistrarAvanceYCosto>();
    builder.Services.AddScoped<IAdjuntarDocumentoEntrada, AdjuntarDocumento>();
    builder.Services.AddScoped<IFinalizarProyectoEntrada, FinalizarProyecto>();
    builder.Services.AddScoped<ICancelarProyectoEntrada, CancelarProyecto>();
    builder.Services.AddScoped<IConsultarHistorialEntrada, ConsultarHistorial>();
}
else
{
    builder.Services.AddScoped<IConsultarConsolidadoEntrada, ConsultarConsolidado>();
    builder.Services.AddScoped<ActualizarProyeccionPrograma>();
}

WebApplication app = builder.Build();

// /salud/vivo y /salud/listo (base de datos y bus), para el sondeo de las réplicas (AD-24).
app.MapearSalud(escritura ? "reconstruccion" : "consulta-programa", rol);

// Cada rol solo atiende sus rutas: el consolidado es del servicio de consulta y lo demás del de escritura.
app.Use(async (contexto, siguiente) =>
{
    bool esPrograma = contexto.Request.Path.StartsWithSegments("/v1/programa");
    bool esApi = contexto.Request.Path.StartsWithSegments("/v1");
    if (esApi && esPrograma == escritura)
    {
        contexto.Response.StatusCode = StatusCodes.Status404NotFound;
        await contexto.Response.WriteAsJsonAsync(new ErrorHttp("RUTA_DE_OTRO_SERVICIO", "Esta ruta la atiende el otro servicio del módulo."));
        return;
    }
    await siguiente();
});

app.MapControllers();
await app.RunAsync();
return 0;
