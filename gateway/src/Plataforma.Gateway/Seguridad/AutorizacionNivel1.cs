using Plataforma.Comun.Seguridad;
using Plataforma.Gateway.Catalogo;
using Yarp.ReverseProxy.Model;

namespace Plataforma.Gateway.Seguridad;

/// <summary>
/// Nivel 1 de la autorización (AD-22, DA-15): el gateway valida el JWT con el mismo emisor, audiencia y llaves que los
/// servicios (<see cref="ValidadorJwt"/> de comun), y exige uno de los roles que declara la ruta en el catálogo. Sin
/// token válido responde 401; con un rol que no corresponde, 403. Si pasa, reenvía el mismo token: cada servicio lo
/// vuelve a validar y aplica su regla de nivel 2. Los mensajes no dicen nada del recurso pedido (QA-11 d).
/// </summary>
public sealed class AutorizacionNivel1
{
    public const string ClaveIdentidad = "gateway.identidad";
    private const string PrefijoBearer = "Bearer ";

    private readonly RequestDelegate _siguiente;
    private readonly ValidadorJwt _validador;
    private readonly ILogger<AutorizacionNivel1> _log;

    public AutorizacionNivel1(RequestDelegate siguiente, ValidadorJwt validador, ILogger<AutorizacionNivel1> log)
    {
        _siguiente = siguiente;
        _validador = validador;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        RouteModel ruta = contexto.GetReverseProxyFeature().Route;
        IReadOnlyDictionary<string, string>? metadatos = ruta.Config.Metadata;
        string roles = metadatos?.GetValueOrDefault(CatalogoRutas.Roles) ?? "";
        if (roles == CatalogoRutas.Publico)
        {
            await _siguiente(contexto);
            return;
        }

        Identidad? identidad = await ValidarAsync(contexto);
        if (identidad is null)
        {
            await Responder(contexto, StatusCodes.Status401Unauthorized, "SIN_IDENTIDAD", "La petición no trae un token válido.");
            return;
        }
        contexto.Items[ClaveIdentidad] = identidad;
        if (roles != CatalogoRutas.CualquierRol && !CatalogoRutas.LeerRoles(roles).Any(identidad.Tiene))
        {
            _log.LogInformation("Nivel 1: {Usuario} ({Organizacion}) sin el rol de la ruta {Ruta} {Metodo}",
                identidad.Usuario, identidad.Organizacion, ruta.Config.RouteId, contexto.Request.Method);
            await Responder(contexto, StatusCodes.Status403Forbidden, "NO_AUTORIZADO", "Su rol no permite esta operación.");
            return;
        }
        await _siguiente(contexto);
    }

    private Task<Identidad?> ValidarAsync(HttpContext contexto)
    {
        string? autorizacion = contexto.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(autorizacion) || !autorizacion.StartsWith(PrefijoBearer, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<Identidad?>(null);
        }
        string token = autorizacion[PrefijoBearer.Length..].Trim();
        string? dispositivo = contexto.Request.Headers[IdentidadPorEncabezados.Dispositivo].FirstOrDefault();
        return _validador.ValidarAsync(token, dispositivo, contexto.RequestAborted);
    }

    internal static Task Responder(HttpContext contexto, int estado, string codigo, string mensaje)
    {
        contexto.Response.StatusCode = estado;
        return contexto.Response.WriteAsJsonAsync(new { codigo, mensaje });
    }
}
