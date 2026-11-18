using System.Text.Json;
using System.Text.Json.Serialization;
using SimSe6;

// Simulador de SE-6, sistema financiero externo (DA-11, QA-01). Estado en memoria.
//   PUT  /v1/lotes/{idLote}                           registra un lote de movimientos (idempotente por elemento)
//   POST /v1/lotes                                    registra un lote de movimientos
//   GET  /v1/movimientos?proyecto={codigoProyecto}    consulta los movimientos recibidos
//   GET  /v1/proyectos, GET /v1/proyectos/{codigo}    consulta las cuentas de los proyectos en SE-6
//   GET  /v1/salud                                    sondeo del adaptador (sujeto a la caída)
//   POST /admin/caida {"modo":"on"|"off"|"lento"}     simula la caída de QA-01
//   GET  /admin/estado                                estado completo del simulador
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
OpcionesSimulador opciones = new();
builder.Configuration.GetSection("Simulador").Bind(opciones);
builder.Services.AddSingleton(opciones);
builder.Services.AddSingleton<RegistroSe6>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

WebApplication app = builder.Build();

// La caída solo afecta la API de SE-6 (/v1), no la administración ni el sondeo del contenedor.
app.Use(async (contexto, siguiente) =>
{
    if (contexto.Request.Path.StartsWithSegments("/v1"))
    {
        RegistroSe6 registro = contexto.RequestServices.GetRequiredService<RegistroSe6>();
        if (registro.Modo == "on")
        {
            contexto.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await contexto.Response.WriteAsync("SE-6 no disponible (simulado).");
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

static IResult ProcesarLote(string idLote, SolicitudLoteMovimientos? cuerpo, RegistroSe6 registro)
{
    if (string.IsNullOrWhiteSpace(idLote) || cuerpo?.Movimientos is null || cuerpo.Movimientos.Count == 0
        || !cuerpo.Movimientos.All(RegistroSe6.EsMovimientoValido))
    {
        return Results.UnprocessableEntity(new { error = "Lote de movimientos financieros inválido." });
    }
    RespuestaLote respuesta = registro.RegistrarLote(idLote, cuerpo.Movimientos);
    return respuesta.Nuevos > 0
        ? Results.Created($"/v1/lotes/{idLote}", respuesta)
        : Results.Ok(respuesta);
}

app.MapPut("/v1/lotes/{idLote}", (string idLote, SolicitudLoteMovimientos cuerpo, RegistroSe6 registro) =>
    ProcesarLote(idLote, cuerpo, registro));

app.MapPost("/v1/lotes", (SolicitudLoteMovimientos cuerpo, RegistroSe6 registro) =>
    ProcesarLote(Guid.NewGuid().ToString("N"), cuerpo, registro));

app.MapGet("/v1/movimientos", (string? proyecto, RegistroSe6 registro) =>
    Results.Ok(registro.Movimientos(proyecto)));

app.MapGet("/v1/proyectos", (RegistroSe6 registro) =>
    Results.Ok(registro.Estado().Proyectos));

app.MapGet("/v1/proyectos/{codigoProyecto}", (string codigoProyecto, RegistroSe6 registro) =>
    registro.Proyecto(codigoProyecto) is { } cuenta
        ? Results.Ok(cuenta)
        : Results.NotFound(new { error = "Proyecto no registrado en SE-6." }));

app.MapPost("/admin/caida", (SolicitudCaida cuerpo, RegistroSe6 registro, ILogger<RegistroSe6> log) =>
{
    try
    {
        registro.CambiarModo(cuerpo.Modo);
    }
    catch (ArgumentException e)
    {
        return Results.BadRequest(new { error = e.Message });
    }
    log.LogWarning("SE-6 en modo {Modo}.", cuerpo.Modo);
    return Results.Ok(new { modo = registro.Modo });
});

app.MapGet("/admin/estado", (RegistroSe6 registro) => Results.Ok(registro.Estado()));

app.Run();

namespace SimSe6
{
    /// <summary>Punto de entrada para WebApplicationFactory en las pruebas del adaptador.</summary>
    public sealed class MarcadorSimSe6;
}
