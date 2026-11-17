using Microsoft.AspNetCore.Mvc;
using Plataforma.Comun.Seguridad;
using Reconstruccion.Api.Presenters;
using Reconstruccion.Dominio;

namespace Reconstruccion.Api.Controllers;

/// <summary>
/// Arma el actor del dominio con la identidad de la petición (<see cref="IIdentidadActual"/>, DA-15). Según
/// "Identidad:Modo", la identidad sale del JWT de Keycloak o de los encabezados de transición (X-Usuario, X-Roles,
/// X-Ambito, X-Territorio); este controlador no cambia entre un modo y otro. La regla de nivel 2 (jurisdicción, DA-14)
/// la aplica el dominio con el ámbito y el territorio del actor.
/// </summary>
public abstract class ControladorBase : ControllerBase
{
    protected bool IntentarActor(out Actor actor, out IActionResult error)
    {
        Identidad? identidad = HttpContext.RequestServices.GetRequiredService<IIdentidadActual>().Actual;
        actor = new Actor("", Rol.Auditor);
        if (identidad is null)
        {
            error = Unauthorized(new ErrorHttp("SIN_IDENTIDAD", "La petición no trae una identidad válida."));
            return false;
        }
        Rol? rol = RolDe(identidad);
        if (rol is null)
        {
            // Hay identidad, pero ninguno de sus roles opera en Reconstrucción: es 403, no 401 (convenciones).
            error = new ObjectResult(new ErrorHttp("NO_AUTORIZADO", "Su rol no opera en Reconstrucción."))
                { StatusCode = StatusCodes.Status403Forbidden };
            return false;
        }
        error = new EmptyResult();
        actor = new Actor(identidad.Usuario, rol.Value, AmbitoDe(identidad.Ambito), identidad.Territorio);
        return true;
    }

    /// <summary>
    /// Roles del realm que operan en Reconstrucción. Si la persona tiene más de uno, prima el de mayor alcance:
    /// autoridad, luego responsable y luego auditor (el dominio trabaja con un rol por actor).
    /// </summary>
    private static Rol? RolDe(Identidad identidad)
    {
        if (identidad.Tiene(RolesPlataforma.Autoridad)) return Rol.Autoridad;
        if (identidad.Tiene(RolesPlataforma.ResponsableProyecto)) return Rol.Responsable;
        if (identidad.Tiene(RolesPlataforma.Auditor)) return Rol.Auditor;
        return null;
    }

    /// <summary>Claim "ambito" (LOCAL, REGIONAL o NACIONAL) al ámbito del dominio. El adaptador ya rechaza otros valores.</summary>
    private static Ambito? AmbitoDe(string? ambito) => ambito switch
    {
        AmbitosPlataforma.Local => Ambito.Local,
        AmbitosPlataforma.Regional => Ambito.Regional,
        AmbitosPlataforma.Nacional => Ambito.Nacional,
        _ => null,
    };
}
