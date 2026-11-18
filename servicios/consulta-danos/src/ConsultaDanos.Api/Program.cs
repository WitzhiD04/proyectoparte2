using ConsultaDanos.Api.Tablero;
using ConsultaDanos.Aplicacion;
using ConsultaDanos.Dominio;
using ConsultaDanos.Infraestructura;
using Plataforma.Comun.Salud;
using Plataforma.Comun.Seguridad;

// Microservicio Consulta de Daños (DA-08, AD-34): modelo de lectura de la evaluación de daños, alimentado por eventos, con
// la caché de geometrías y respuestas (AD-17, E-21) y la marca de tiempo de cada dato (AD-16). Es la ruta de lectura de
// mayor volumen del módulo y queda separada de la sincronización (CU-3 del SAD): se escala sin escalar a Evaluación de
// Daños (AD-18, QA-05). Con el argumento "migrar" es el migrador del esquema (DT-19).
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

if (args.Contains("migrar"))
{
    using ILoggerFactory registros = LoggerFactory.Create(l => l.AddConsole());
    ILogger log = registros.CreateLogger("Migrador");
    try
    {
        await MigracionConsultaDanos.EjecutarAsync(builder.Configuration, log);
        return 0;
    }
    catch (Exception e)
    {
        log.LogCritical(e, "La migración falló.");
        return 1;
    }
}

// Parámetros de despliegue (QA-05 c: se cambian sin tocar el código).
IConfigurationSection seccion = builder.Configuration.GetSection("ConsultaDanos");
string[] criticos = seccion.GetSection("TiposCriticos").Get<string[]>() ?? [];
ParametrosConsulta parametros = new(
    criticos.Length == 0 ? Priorizacion.TiposCriticosPorOmision : criticos.Select(t => t.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal),
    new UmbralesRevision(seccion.GetValue("UmbralConflictos", UmbralesRevision.PorOmision.Conflictos),
        seccion.GetValue("UmbralHoras", UmbralesRevision.PorOmision.Horas)),
    TimeSpan.FromSeconds(seccion.GetValue("VigenciaRespuestaSegundos", 60)));

builder.Services.AgregarIdentidad(builder.Configuration);
builder.Services.AgregarInfraestructura(builder.Configuration);
builder.Services.AddSingleton(parametros);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ActualizarProyecciones>();
builder.Services.AddScoped<ConsultarMapa>();
builder.Services.AddScoped<ConsultarConsolidado>();
builder.Services.AddScoped<ConsultarEvolucion>();
builder.Services.AddScoped<ConsultarEvolucionZona>();
builder.Services.AddScoped<ConsultarIndicadores>();
builder.Services.AgregarTablero(builder.Configuration);

WebApplication app = builder.Build();
app.UsarTablero();
app.MapearSalud("consulta-danos");

// Cualquier usuario autenticado consulta (RF-ED-11: autoridad y organismos; la consola también la ve el operador). Sin
// identidad: 401. Si ni la base ni la caché pueden responder: 503, para que el cliente reintente.
RouteGroupBuilder v1 = app.MapGroup("/v1/consulta-danos").AddEndpointFilter(async (contexto, siguiente) =>
{
    if (contexto.HttpContext.RequestServices.GetRequiredService<IIdentidadActual>().Actual is null)
    {
        return Results.Json(new { codigo = "SIN_IDENTIDAD", mensaje = "La petición no trae una identidad válida." }, statusCode: 401);
    }
    try
    {
        return await siguiente(contexto);
    }
    catch (DatosNoDisponiblesException e)
    {
        return Results.Json(new { codigo = "NO_DISPONIBLE", mensaje = e.Message }, statusCode: 503);
    }
});

// Mapa de daños por zona (RF-ED-11, AD-32): coropleta por municipio y, con puntos=true, los activos.
v1.MapGet("/mapa", async (Guid? desastre, string? nivelMinimo, bool? soloValidadas, bool? puntos, ConsultarMapa caso,
    HttpResponse respuesta, CancellationToken ct) =>
{
    if (desastre is null)
    {
        return Invalido("Falta el parámetro desastre (el identificador del desastre).");
    }
    if (!LeerNivel(nivelMinimo, out NivelDano minimo))
    {
        return Invalido($"nivelMinimo '{nivelMinimo}' no es un nivel de daño (SIN_DANO, LEVE, MODERADO, GRAVE o DESTRUCCION_TOTAL).");
    }
    RespuestaLectura r = await caso.EjecutarAsync(new FiltroMapa(desastre.Value, minimo, soloValidadas ?? false, puntos ?? false), ct);
    respuesta.Headers["X-Degradado"] = r.Degradado ? "true" : "false";
    return Results.Text(r.Contenido, "application/geo+json");
});

// Consolidado del impacto por zona y por tipo, con la priorización (RF-ED-10, DA-08). Solo evaluaciones validadas.
v1.MapGet("/consolidado", async (Guid? desastre, ConsultarConsolidado caso, HttpResponse respuesta, CancellationToken ct) =>
{
    if (desastre is null)
    {
        return Invalido("Falta el parámetro desastre (el identificador del desastre).");
    }
    RespuestaLectura r = await caso.EjecutarAsync(desastre.Value, ct);
    respuesta.Headers["X-Degradado"] = r.Degradado ? "true" : "false";
    return Results.Text(r.Contenido, "application/json");
});

// Evolución de un activo (RF-ED-15): la secuencia de sus evaluaciones en el tiempo con su nivel de daño.
v1.MapGet("/activos/{id:guid}/evolucion", async (Guid id, ConsultarEvolucion caso, CancellationToken ct) =>
{
    EvolucionActivo? e = await caso.EjecutarAsync(id, ct);
    if (e is null)
    {
        return Results.NotFound(new { codigo = "NO_ENCONTRADO", mensaje = $"No hay evaluaciones del activo {id} en el modelo de lectura." });
    }
    EstadoActivo v = e.Vigente;
    ContenidoActivo? c = v.Contenido;
    return Results.Ok(new
    {
        idActivo = id,
        datoAl = e.DatoAl,
        vigente = new
        {
            idEvaluacion = c?.IdEvaluacion, idDesastre = c?.IdDesastre, tipoActivo = c?.TipoActivo, nivelDano = c?.Nivel.ToString(),
            peso = c is null ? (double?)null : IndiceDano.Peso(c.Nivel), codigoMunicipio = c?.CodigoMunicipio, lat = c?.Lat, lon = c?.Lon,
            estadoEvaluacion = c?.EstadoEvaluacion, versionEvaluacion = c?.VersionEvaluacion, versionActivo = v.VersionActivo,
            validada = v.Validada, estadoValidacion = v.Validacion?.ToString(), conflictoPendiente = e.ConflictoPendiente,
            fusionado = v.Fusionado, fusionadoEn = v.FusionadoEn,
        },
        evaluaciones = e.Entradas.Select(h => new
        {
            instante = h.Instante, idEvaluacion = h.IdEvaluacion, versionEvaluacion = h.VersionEvaluacion, origen = h.Origen,
            nivelDano = h.Nivel.ToString(), peso = IndiceDano.Peso(h.Nivel), tipoActivo = h.TipoActivo, estadoEvaluacion = h.EstadoEvaluacion,
            estadoValidacion = h.Validacion?.ToString(), enConflicto = h.EnConflicto, idDesastre = h.IdDesastre,
            codigoMunicipio = h.CodigoMunicipio, autor = h.Autor, detalle = h.Detalle,
        }),
    });
});

// Evolución de una zona (RF-ED-15, E-40): la secuencia de evaluaciones del municipio o departamento y su agregado por día.
v1.MapGet("/zonas/{codigo}/evolucion", async (string codigo, string? desastre, ConsultarEvolucionZona caso, CancellationToken ct) =>
{
    if (CodigoZona.TipoDe(codigo) is null)
    {
        return Invalido($"'{codigo}' no es un código DIVIPOLA: 5 dígitos para un municipio o 2 para un departamento.");
    }
    Guid? idDesastre = null;
    if (!string.IsNullOrWhiteSpace(desastre))
    {
        if (!Guid.TryParse(desastre, out Guid leido))
        {
            return Invalido($"desastre '{desastre}' no es un identificador válido.");
        }
        idDesastre = leido;
    }
    EvolucionZona? e = await caso.EjecutarAsync(codigo, idDesastre, ct);
    if (e is null)
    {
        return Results.NotFound(new { codigo = "NO_ENCONTRADO", mensaje = $"La zona {codigo} no existe en la capa de municipios." });
    }
    if (e.Entradas.Count == 0)
    {
        string alcance = idDesastre is null ? "" : $" para el desastre {idDesastre}";
        return Results.NotFound(new { codigo = "NO_ENCONTRADO", mensaje = $"La zona {codigo} no tiene evaluaciones{alcance}." });
    }
    return Results.Ok(new
    {
        zona = new { codigo = e.Zona.Codigo, nombre = e.Zona.Nombre, tipo = e.Zona.Tipo.ToString() },
        idDesastre,
        datoAl = e.DatoAl,
        evaluaciones = e.Entradas.Select(h => new
        {
            instante = h.Instante, idActivo = h.IdActivo, tipoActivo = h.TipoActivo, nivelDano = h.Nivel.ToString(),
            peso = IndiceDano.Peso(h.Nivel), origen = h.Origen, estadoValidacion = h.Validacion?.ToString(), autor = h.Autor,
            idEvaluacion = h.IdEvaluacion, versionEvaluacion = h.VersionEvaluacion, enConflicto = h.EnConflicto,
            idDesastre = h.IdDesastre, codigoMunicipio = h.CodigoMunicipio,
        }),
        porDia = e.PorDia.Select(d => new
        {
            fecha = d.Fecha, activos = d.Distribucion.Total,
            distribucion = Distribucion.Niveles.ToDictionary(n => n.ToString(), n => d.Distribucion[n]),
        }),
    });
});

// Indicadores de la bandeja de revisión y alarma de R-08 (DA-09, E-09). La bandeja misma está en Evaluación de Daños.
v1.MapGet("/indicadores-revision", async (ConsultarIndicadores caso, CancellationToken ct) =>
{
    (IndicadoresRevision i, DateTimeOffset? datoAl) = await caso.EjecutarAsync(ct);
    return Results.Ok(new
    {
        conflictosPendientes = i.ConflictosPendientes, masViejoDesde = i.MasViejoDesde, antiguedadHoras = i.AntiguedadHoras,
        umbralConflictos = i.Umbrales.Conflictos, umbralHoras = i.Umbrales.Horas, superaTamano = i.SuperaTamano,
        superaAntiguedad = i.SuperaAntiguedad, alarma = i.Alarma, datoAl,
    });
});

await app.RunAsync();
return 0;

static IResult Invalido(string mensaje) => Results.BadRequest(new { codigo = "DATO_INVALIDO", mensaje });

static bool LeerNivel(string? texto, out NivelDano nivel)
{
    nivel = NivelDano.SIN_DANO;
    if (string.IsNullOrWhiteSpace(texto)) return true;
    return !char.IsDigit(texto.Trim()[0]) && Enum.TryParse(texto.Trim(), ignoreCase: true, out nivel) && Enum.IsDefined(nivel);
}

/// <summary>Para las pruebas con WebApplicationFactory.</summary>
public partial class Program;
