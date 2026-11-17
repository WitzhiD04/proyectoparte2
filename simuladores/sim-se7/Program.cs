using System.Text.Json;
using System.Text.Json.Serialization;
using SimSe7;

// Simulador de SE-7, sistema de proyectos de infraestructura externo (DA-11, QA-01). Estado en memoria.
//   PUT  /v1/obras/{codigoObra}/versiones/{version}   registra una versión de una obra (idempotente)
//   GET  /v1/cambios?desde={marca}&limite={n}         cambios de SE-7 desde una marca (consulta paginada idempotente)
//   GET  /v1/salud                                    sondeo del adaptador (sujeto a la caída)
//   POST /admin/caida {"modo":"on"|"off"|"lento"}     simula la caída de QA-01
//   POST /admin/cambios, POST /admin/generar, GET /admin/estado
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
OpcionesSimulador opciones = new();
builder.Configuration.GetSection("Simulador").Bind(opciones);
builder.Services.AddSingleton(opciones);
builder.Services.AddSingleton<RegistroSe7>();
builder.Services.AddHostedService<GeneradorCambios>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

WebApplication app = builder.Build();

// La caída solo afecta la API de SE-7 (/v1), no la administración ni el sondeo del contenedor.
app.Use(async (contexto, siguiente) =>
{
    if (contexto.Request.Path.StartsWithSegments("/v1"))
    {
        RegistroSe7 registro = contexto.RequestServices.GetRequiredService<RegistroSe7>();
        if (registro.Modo == "on")
        {
            contexto.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await contexto.Response.WriteAsync("SE-7 no disponible (simulado).");
            return;
        }
        if (registro.Modo == "lento")
        {
            // Responde, pero después del tiempo de espera del adaptador: el estímulo de QA-01 son los tiempos agotados.
            try
            {
                await Task.Delay(opciones.RetrasoLentoMs, contexto.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                return; // el cliente se cansó de esperar: la petición no se procesa
            }
        }
    }
    await siguiente(contexto);
});

app.MapGet("/salud/vivo", () => Results.Ok(new { estado = "ok" }));
app.MapGet("/v1/salud", () => Results.Ok(new { estado = "ok" }));

app.MapPut("/v1/obras/{codigoObra}/versiones/{version:int}", (string codigoObra, int version, VersionObra cuerpo, RegistroSe7 registro) =>
{
    if (version < 1 || string.IsNullOrWhiteSpace(cuerpo.Titulo) || !Situaciones.Todas.Contains(cuerpo.Situacion)
        || cuerpo.Operacion is not ("ALTA" or "MODIFICACION") || cuerpo.PorcentajeAvance is < 0 or > 100)
    {
        return Results.UnprocessableEntity(new { error = "Versión de obra inválida." });
    }
    bool nueva = registro.RegistrarVersion(codigoObra, version, cuerpo);
    RespuestaVersion respuesta = new(codigoObra, version, nueva);
    return nueva ? Results.Created($"/v1/obras/{codigoObra}/versiones/{version}", respuesta) : Results.Ok(respuesta);
});

app.MapGet("/v1/cambios", (string? desde, int? limite, RegistroSe7 registro) =>
{
    long marca = 0;
    if (!string.IsNullOrEmpty(desde) && (!long.TryParse(desde, out marca) || marca < 0))
    {
        return Results.BadRequest(new { error = "Marca de avance inválida." });
    }
    int tamano = Math.Clamp(limite ?? opciones.LimitePagina, 1, opciones.LimitePagina);
    return Results.Ok(registro.CambiosDesde(marca, tamano));
});

app.MapPost("/admin/caida", (SolicitudCaida cuerpo, RegistroSe7 registro, ILogger<RegistroSe7> log) =>
{
    try
    {
        registro.CambiarModo(cuerpo.Modo);
    }
    catch (ArgumentException e)
    {
        return Results.BadRequest(new { error = e.Message });
    }
    log.LogWarning("SE-7 en modo {Modo}.", cuerpo.Modo);
    return Results.Ok(new { modo = registro.Modo });
});

app.MapPost("/admin/cambios", (SolicitudCambio cuerpo, RegistroSe7 registro) =>
{
    try
    {
        return Results.Ok(registro.AgregarCambio(cuerpo.CodigoObra, cuerpo.Clase, cuerpo.PorcentajeAvance, cuerpo.Contratista));
    }
    catch (ArgumentException e)
    {
        return Results.BadRequest(new { error = e.Message });
    }
});

app.MapPost("/admin/generar", (RegistroSe7 registro) => Results.Ok(registro.Generar(Random.Shared)));
app.MapGet("/admin/estado", (RegistroSe7 registro) => Results.Ok(registro.Estado()));

app.Run();

namespace SimSe7
{
    /// <summary>Punto de entrada para WebApplicationFactory en las pruebas del adaptador.</summary>
    public sealed class MarcadorSimSe7;
}
