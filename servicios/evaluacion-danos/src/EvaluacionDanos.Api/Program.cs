using EvaluacionDanos.Aplicacion;
using EvaluacionDanos.Aplicacion.CasosDeUso;
using EvaluacionDanos.Infraestructura;
using EvaluacionDanos.Infraestructura.Persistencia;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Comun.Salud;
using Plataforma.Comun.Seguridad;

// Microservicio de Evaluación de Daños (base propia, AD-01). Recibe por lotes las evaluaciones que los evaluadores
// registraron sin conexión, detecta conflictos y duplicados sin resolverlos (QA-14), y atiende la revisión humana:
// validación, resolución y catálogo. Publica sus eventos por la bandeja de salida (AD-45) y mantiene copias locales
// de los desastres y de la capa de municipios con los eventos de su cola (E-29, DA-07).
// Con el argumento "migrar" la imagen es el migrador (DT-19): aplica el esquema con el rol dueño y termina.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

if (args.Contains("migrar"))
{
    using ILoggerFactory registros = LoggerFactory.Create(l => l.AddConsole());
    ILogger log = registros.CreateLogger("Migrador");
    try
    {
        await MigracionEvaluacionDanos.EjecutarAsync(builder.Configuration, sembrar: args.Contains("--sembrar"), log);
        return 0;
    }
    catch (Exception e)
    {
        log.LogCritical(e, "La migración falló.");
        return 1;
    }
}

// X-Usuario lleva el nombre del evaluador ("Ana Pérez"): sin esto Kestrel rechaza con 400 cualquier
// encabezado con tildes. Se aceptan en UTF-8.
builder.WebHost.ConfigureKestrel(o => o.RequestHeaderEncodingSelector = _ => System.Text.Encoding.UTF8);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => JsonEvaluacion.Configurar(o.JsonSerializerOptions))
    // Si el JSON llega mal formado se responde 400 con { "error": "..." }, como el resto de errores.
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = contexto =>
        new BadRequestObjectResult(new ErrorDto(PrimerError(contexto.ModelState))));
builder.Services.AddRazorPages();
// Que Razor no convierta las tildes en entidades (&#xF3;); sigue escapando <, >, & y comillas.
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(o =>
    o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));
// La descarga previa puede ser grande y el evaluador tiene poca conectividad (DA-19).
builder.Services.AddResponseCompression();

// Identidad de la petición (DA-15): hoy por encabezados; con el gateway, por el JWT, sin cambiar este servicio.
builder.Services.AgregarIdentidad(builder.Configuration);

// Aquí se conecta cada puerto con su implementación (adaptadores).
builder.Services.AgregarInfraestructura(builder.Configuration);

// Casos de uso.
builder.Services.AddScoped<SincronizarLote>();
builder.Services.AddScoped<SubirEvidencia>();
builder.Services.AddScoped<ICasoDeUso<SolicitudDescarga, DescargaDto>, ConsultarDescarga>();
builder.Services.AddScoped<ICasoDeUso<string, List<ConflictoDto>>, ConsultarConflictos>();
builder.Services.AddScoped<ICasoDeUso<string?, List<EvaluacionResumenDto>>, ConsultarEvaluacionesDeDispositivo>();
builder.Services.AddScoped<ICasoDeUso<bool, List<DesastreDto>>, ConsultarDesastres>();
builder.Services.AddScoped<ICasoDeUso<SolicitudMapaCalor, MapaCalorDto>, ConsultarMapaCalor>();
builder.Services.AddScoped<ICasoDeUso<SolicitudRegistro, ResultadoOperacionDto>, RegistrarEvaluacion>();
builder.Services.AddScoped<ICasoDeUso<SolicitudActualizacion, EvaluacionDetalleDto>, ActualizarEvaluacion>();
builder.Services.AddScoped<ICasoDeUso<SolicitudAsociacion, EvaluacionDetalleDto>, AsociarEvaluacion>();
builder.Services.AddScoped<ICasoDeUso<SolicitudValidacion, EvaluacionDetalleDto>, ValidarEvaluacion>();
builder.Services.AddScoped<ICasoDeUso<SolicitudResolucion, ResolucionDto>, ResolverConflicto>();
builder.Services.AddScoped<ICasoDeUso<bool, List<TipoActivoDto>>, ConsultarCatalogo>();
builder.Services.AddScoped<CrearTipoActivo>();
builder.Services.AddScoped<ModificarTipoActivo>();
// Los usa el adaptador de entrada de la cola del servicio.
builder.Services.AddScoped<ActualizarCopiaDeDesastres>();
builder.Services.AddScoped<CargarCapaGeografica>();

WebApplication app = builder.Build();

app.UseResponseCompression();
app.UseStaticFiles();

// /salud/vivo y /salud/listo (base, bus y capa de municipios), para el sondeo de las réplicas (AD-24).
app.MapearSalud("evaluacion-danos");
// La app móvil prueba la conexión con /salud y busca "estado" en la respuesta: se conserva.
app.MapGet("/salud", () => Results.Ok(new { estado = "ok", instante = DateTime.UtcNow }));
app.MapControllers();
app.MapRazorPages();
await app.RunAsync();
return 0;

static string PrimerError(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary estado)
{
    // Los errores de lectura del JSON traen la ruta del campo ("$.operaciones[0].evaluacion.lat").
    string? general = null;
    foreach (var entrada in estado)
    {
        foreach (var error in entrada.Value.Errors)
        {
            if (entrada.Key.StartsWith('$'))
            {
                return $"El campo '{entrada.Key.TrimStart('$', '.')}' no tiene un valor válido";
            }
            general ??= error.ErrorMessage;
        }
    }
    return general ?? "Petición inválida";
}
