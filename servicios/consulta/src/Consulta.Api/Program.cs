using Consulta.Aplicacion;
using Consulta.Dominio;
using Consulta.Infraestructura;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Comun.Salud;
using Plataforma.Comun.Seguridad;

// Microservicio Consulta (AD-15, FP-02): modelo de lectura de los desastres, alimentado por eventos, con la caché de
// geometrías (AD-17) y la marca de tiempo de cada dato (AD-16). Atiende la ruta de mayor carga de la plataforma y se escala
// sin escalar a Gestión de Desastres (AD-18). Con el argumento "migrar" es el migrador del esquema (DT-19).
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

if (args.Contains("migrar"))
{
    using ILoggerFactory registros = LoggerFactory.Create(l => l.AddConsole());
    ILogger log = registros.CreateLogger("Migrador");
    try
    {
        await MigracionConsulta.EjecutarAsync(builder.Configuration, log);
        return 0;
    }
    catch (Exception e)
    {
        log.LogCritical(e, "La migración falló.");
        return 1;
    }
}

builder.Services.AgregarIdentidad(builder.Configuration);
builder.Services.AgregarInfraestructura(builder.Configuration);
builder.Services.AddScoped<ActualizarProyecciones>();
builder.Services.AddScoped<ConsultarDesastres>();
builder.Services.AddScoped<ConsultarMapa>();

WebApplication app = builder.Build();
app.MapearSalud("consulta");

// Cualquier usuario autenticado consulta (RF-GD-16: "todos los actores autorizados"). Sin identidad: 401.
RouteGroupBuilder v1 = app.MapGroup("/v1").AddEndpointFilter(async (contexto, siguiente) =>
    contexto.HttpContext.RequestServices.GetRequiredService<IIdentidadActual>().Actual is null
        ? Results.Json(new { codigo = "SIN_IDENTIDAD", mensaje = "La petición no trae una identidad válida." }, statusCode: 401)
        : await siguiente(contexto));

// Tablero consolidado (FP-02). Es la ruta de mayor carga y se atiende con proyecciones.
v1.MapGet("/consulta/desastres", async (string? estado, string? tipo, ConsultarDesastres caso, CancellationToken ct) =>
    Results.Ok(await caso.TableroAsync(estado?.ToUpperInvariant(), tipo?.ToUpperInvariant(), ct)));

// Vista consolidada de un desastre, con la marca de tiempo de cada dato (Cuadro 15, RF-GD-16).
v1.MapGet("/consulta/desastres/{id:guid}", async (Guid id, ConsultarDesastres caso, CancellationToken ct) =>
{
    VistaDesastre? vista = await caso.DesastreAsync(id, ct);
    return vista is null
        ? Results.NotFound(new { codigo = "NO_ENCONTRADO", mensaje = $"No existe el desastre {id} en el modelo de lectura." })
        : Results.Ok(new
        {
            vista.Id, vista.Tipo, vista.Nombre, vista.Descripcion, vista.InstanteInicio, vista.Lat, vista.Lon,
            Severidad = new { valor = vista.Severidad.Valor, datoAl = vista.Severidad.DatoAl },
            Estado = new { valor = vista.Estado.Valor, datoAl = vista.Estado.DatoAl },
            Responsable = new { valor = vista.Responsable.Valor, datoAl = vista.Responsable.DatoAl },
            Participantes = new { valor = vista.Participantes.Valor.Select(p => new { organismo = p.Organismo, responsabilidad = p.Responsabilidad }), datoAl = vista.Participantes.DatoAl },
            Zona = new { valor = vista.Zona.Valor, datoAl = vista.Zona.DatoAl },
            vista.DatoAl,
        });
});

// Mapa de zonas afectadas desde la caché de geometrías (AD-17); degradado si la base no responde (CON-6).
v1.MapGet("/consulta/mapa", async (bool? soloActivos, ConsultarMapa caso, HttpResponse respuesta, CancellationToken ct) =>
{
    (string mapa, bool degradado) = await caso.EjecutarAsync(soloActivos ?? true, ct);
    respuesta.Headers["X-Degradado"] = degradado ? "true" : "false";
    return Results.Text(mapa, "application/geo+json");
});

// Evolución espacial y de severidad en el tiempo (RF-GD-17); con "en", el desastre como estaba en ese instante (RF-GD-09).
v1.MapGet("/consulta/desastres/{id:guid}/evolucion", async (Guid id, DateTimeOffset? en, ConsultarDesastres caso, CancellationToken ct) =>
{
    var evolucion = await caso.EvolucionAsync(id, en, ct);
    return evolucion is null
        ? Results.NotFound(new { codigo = "NO_ENCONTRADO", mensaje = $"No existe el desastre {id} en el modelo de lectura." })
        : Results.Ok(new
        {
            idDesastre = id,
            cambios = evolucion.Value.Cambios,
            zonas = evolucion.Value.Zonas,
            enInstante = evolucion.Value.EnInstante,
            datoAl = evolucion.Value.Cambios.Count == 0 ? (DateTimeOffset?)null : evolucion.Value.Cambios.Max(c => c.Instante),
        });
});

// Estado de las integraciones con los sistemas externos (EV-23, mitigación de ATAM R-10, E-09).
v1.MapGet("/integraciones", async (ConsultarDesastres caso, CancellationToken ct) => Results.Ok(await caso.IntegracionesAsync(ct)));

await app.RunAsync();
return 0;

/// <summary>Para las pruebas con WebApplicationFactory.</summary>
public partial class Program;
