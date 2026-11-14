using Reconstruccion.Api.Presenters;
using Reconstruccion.Aplicacion.CasosDeUso;
using Reconstruccion.Infraestructura;

// El mismo programa se despliega con dos roles:
//   escritura -> servicio de Reconstrucción (proyectos, historial, bandeja de salida y relevo de eventos)
//   consulta  -> servicio de Consulta del Programa (modelo de lectura y consolidado)
// "todo" levanta ambos en un solo proceso, útil para desarrollo.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string rol = (builder.Configuration["Reconstruccion:Rol"] ?? "todo").ToLowerInvariant();
bool escritura = rol == "escritura" || rol == "todo";
bool consulta = rol == "consulta" || rol == "todo";

builder.Services.AddControllers();

// Aquí se conecta cada puerto con su implementación (gateways) según el rol.
builder.Services.AgregarInfraestructura(builder.Configuration, escritura, consulta);

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
if (consulta)
{
    builder.Services.AddScoped<IConsultarConsolidadoEntrada, ConsultarConsolidado>();
    builder.Services.AddScoped<ActualizarProyeccionPrograma>();
}

WebApplication app = builder.Build();

app.MapGet("/salud", () => Results.Ok(new { estado = "ok", rol }));

// Cada rol solo atiende sus rutas: el consolidado es del servicio de consulta y lo demás del de escritura.
app.Use(async (contexto, siguiente) =>
{
    bool esPrograma = contexto.Request.Path.StartsWithSegments("/v1/programa");
    bool esApi = contexto.Request.Path.StartsWithSegments("/v1");
    if (esApi && ((esPrograma && !consulta) || (!esPrograma && !escritura)))
    {
        contexto.Response.StatusCode = StatusCodes.Status404NotFound;
        await contexto.Response.WriteAsJsonAsync(new ErrorHttp("RUTA_DE_OTRO_SERVICIO", "Esta ruta la atiende el otro servicio del módulo."));
        return;
    }
    await siguiente();
});

app.MapControllers();
app.Run();
