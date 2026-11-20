using System.Text.Json;
using System.Text.Json.Serialization;
using SimSe8;

// Simulador de SE-8, sistema externo de comunicación y notificación (AD-66, QA-08). Estado en memoria.
//   PUT  /v1/alertas/{referencia}                    registra una alerta (idempotente): 202 nueva, 200 repetida, 422 inválida
//   GET  /v1/acuses?desde={marca}&limite={n}          acuses desde una marca (consulta paginada idempotente)
//   GET  /v1/salud                                    sondeo del adaptador (sujeto a la caída)
//   POST /admin/caida {"modo":"on"|"off"|"lento"|"sin-acuse"}
//   GET  /admin/estado, GET /admin/alertas/{referencia}, POST /admin/reiniciar
// Cada alerta nueva se acusa después de RetrasoAcuseMs y el acuse se avisa al adaptador con POST a UrlAcuse.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
OpcionesSimulador opciones = new();
builder.Configuration.GetSection("Simulador").Bind(opciones);
builder.Services.AddSingleton(opciones);
builder.Services.AddSingleton<RegistroSe8>();
builder.Services.AddHttpClient("acuses");
builder.Services.AddHostedService<CanalAcuses>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

WebApplication app = builder.Build();

// La caída solo afecta la API de SE-8 (/v1), no la administración ni el sondeo del contenedor.
app.Use(async (contexto, siguiente) =>
{
    if (contexto.Request.Path.StartsWithSegments("/v1"))
    {
        RegistroSe8 registro = contexto.RequestServices.GetRequiredService<RegistroSe8>();
        if (registro.Modo == "on")
        {
            contexto.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await contexto.Response.WriteAsync("SE-8 no disponible (simulado).");
            return;
        }
        if (registro.Modo == "lento")
        {
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

app.MapPut("/v1/alertas/{referencia}", (string referencia, AlertaSe8 cuerpo, RegistroSe8 registro) =>
{
    if (cuerpo.ReferenciaExterna != referencia || string.IsNullOrWhiteSpace(cuerpo.Destinatario)
        || string.IsNullOrWhiteSpace(cuerpo.Titulo) || string.IsNullOrWhiteSpace(cuerpo.Cuerpo)
        || cuerpo.Medio is null || !Vocabulario.Medios.Contains(cuerpo.Medio)
        || cuerpo.Prioridad is null || !Vocabulario.Prioridades.Contains(cuerpo.Prioridad))
    {
        return Results.UnprocessableEntity(new { error = "Alerta inválida." });
    }
    bool nueva = registro.Registrar(cuerpo);
    RespuestaAlerta respuesta = new(referencia, nueva, "RECIBIDA");
    return nueva ? Results.Accepted($"/admin/alertas/{referencia}", respuesta) : Results.Ok(respuesta);
});

app.MapGet("/v1/acuses", (string? desde, int? limite, RegistroSe8 registro) =>
{
    long marca = 0;
    if (!string.IsNullOrEmpty(desde) && (!long.TryParse(desde, out marca) || marca < 0))
    {
        return Results.BadRequest(new { error = "Marca de avance inválida." });
    }
    return Results.Ok(registro.AcusesDesde(marca, Math.Clamp(limite ?? opciones.LimitePagina, 1, opciones.LimitePagina)));
});

app.MapPost("/admin/caida", (SolicitudCaida cuerpo, RegistroSe8 registro, ILogger<RegistroSe8> log) =>
{
    try
    {
        registro.CambiarModo(cuerpo.Modo);
    }
    catch (ArgumentException e)
    {
        return Results.BadRequest(new { error = e.Message });
    }
    log.LogWarning("SE-8 en modo {Modo}.", cuerpo.Modo);
    return Results.Ok(new { modo = registro.Modo });
});

app.MapGet("/admin/estado", (RegistroSe8 registro) => Results.Ok(registro.Estado()));
app.MapGet("/admin/alertas/{referencia}", (string referencia, RegistroSe8 registro) =>
    registro.Alerta(referencia) is { } alerta ? Results.Ok(alerta) : Results.NotFound());
app.MapPost("/admin/reiniciar", (RegistroSe8 registro) =>
{
    registro.Reiniciar();
    return Results.Ok(new { modo = registro.Modo });
});

app.Run();
