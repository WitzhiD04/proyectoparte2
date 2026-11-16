using EvaluacionDanos.Aplicacion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Plataforma.Comun.Seguridad;

namespace EvaluacionDanos.Api.Seguridad;

/// <summary>
/// Autorización de nivel 1 (AD-22): la petición trae identidad y uno de los roles que la ruta admite. Sin identidad,
/// 401; sin el rol, 403. La identidad sale de <see cref="IIdentidadActual"/>: hoy de encabezados, con el gateway del
/// JWT, sin cambiar este servicio. Las reglas que dependen de los datos (nivel 2) están en los casos de uso.
/// Es un filtro de recurso (no de acción) para responder ANTES de leer el cuerpo: así una foto de 10 MB sin
/// identidad no se alcanza a recibir completa.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiereRolAttribute(params string[] roles) : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext contexto)
    {
        Identidad? identidad = contexto.HttpContext.RequestServices.GetRequiredService<IIdentidadActual>().Actual;
        if (identidad is null)
        {
            contexto.Result = new UnauthorizedObjectResult(new ErrorDto("La petición no trae una identidad válida (usuario)"));
            return;
        }
        if (roles.Length > 0 && !RolesEfectivos.De(identidad).Overlaps(roles))
        {
            contexto.Result = new ObjectResult(new ErrorDto($"Esta operación exige uno de estos roles: {string.Join(", ", roles)}"))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext contexto)
    {
    }
}

public static class RolesEfectivos
{
    /// <summary>
    /// TRANSICIÓN (se elimina con el gateway, DA-15): la app móvil actual no envía X-Roles. Una petición sin roles que
    /// se identifica con X-Dispositivo se trata como <c>evaluador</c>. Con el gateway el rol sale del token.
    /// </summary>
    public static IReadOnlySet<string> De(Identidad identidad) =>
        identidad.Roles.Count == 0 && identidad.IdDispositivo is not null
            ? new HashSet<string> { RolesPlataforma.Evaluador }
            : identidad.Roles;
}

/// <summary>Arma el actor de los casos de uso con la identidad de la petición.</summary>
public abstract class ControladorBase(IIdentidadActual identidad) : ControllerBase
{
    protected Identidad Identidad => identidad.Actual
        ?? throw new InvalidOperationException("La ruta no declara [RequiereRol]: no hay identidad.");

    protected Actor Actor
    {
        get
        {
            Identidad actual = Identidad;
            return new Actor(actual.Usuario, actual.Organizacion, actual.IdDispositivo,
                EsOperador: RolesEfectivos.De(actual).Contains(RolesPlataforma.Operador));
        }
    }

    /// <summary>Presenta el resultado de un caso de uso: la respuesta o el error con el código HTTP que corresponde.</summary>
    protected IActionResult Presentar<T>(Resultado<T> resultado, Func<T, IActionResult>? exito = null)
    {
        if (resultado.Exito)
        {
            return exito is null ? Ok(resultado.Valor) : exito(resultado.Valor!);
        }
        var error = new ErrorDto(resultado.Mensaje ?? "Error");
        return resultado.Fallo switch
        {
            TipoFallo.Invalido => BadRequest(error),
            TipoFallo.NoEncontrado => NotFound(error),
            TipoFallo.NoAutorizado => StatusCode(StatusCodes.Status403Forbidden, error),
            TipoFallo.ReglaIncumplida => UnprocessableEntity(error),
            TipoFallo.Concurrencia => Conflict(error),
            TipoFallo.NoDisponible => StatusCode(StatusCodes.Status503ServiceUnavailable, error),
            _ => StatusCode(StatusCodes.Status500InternalServerError, error)
        };
    }
}
