using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Plataforma.Comun.Salud;

public static class SaludPlataforma
{
    /// <summary>Etiqueta de las comprobaciones que forman parte de la preparación (base de datos, bus).</summary>
    public const string Listo = "listo";

    /// <summary>
    /// /salud/vivo: el proceso responde (no revisa dependencias, para no reiniciar un pod por una caída del bus).
    /// /salud/listo: todas las comprobaciones con la etiqueta "listo".
    /// </summary>
    public static IEndpointRouteBuilder MapearSalud(this IEndpointRouteBuilder rutas, string servicio, string? rol = null)
    {
        rutas.MapHealthChecks("/salud/vivo", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = (contexto, informe) => Escribir(contexto, informe, servicio, rol),
        });
        rutas.MapHealthChecks("/salud/listo", new HealthCheckOptions
        {
            Predicate = registro => registro.Tags.Contains(Listo),
            ResponseWriter = (contexto, informe) => Escribir(contexto, informe, servicio, rol),
        });
        return rutas;
    }

    private static Task Escribir(HttpContext contexto, HealthReport informe, string servicio, string? rol)
    {
        contexto.Response.ContentType = "application/json; charset=utf-8";
        var cuerpo = new
        {
            servicio,
            rol,
            estado = informe.Status.ToString(),
            instante = DateTimeOffset.UtcNow,
            dependencias = informe.Entries.ToDictionary(
                e => e.Key,
                e => new { estado = e.Value.Status.ToString(), detalle = e.Value.Description ?? e.Value.Exception?.Message }),
        };
        return contexto.Response.WriteAsync(JsonSerializer.Serialize(cuerpo, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
