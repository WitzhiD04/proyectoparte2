using Microsoft.AspNetCore.Mvc;
using Reconstruccion.Api.Presenters;
using Reconstruccion.Dominio;

namespace Reconstruccion.Api.Controllers;

/// <summary>
/// Arma el actor con los encabezados de la petición. En la plataforma completa esos encabezados los pone
/// el API Gateway después de autenticar; aquí el módulo hace su propia validación de rol y jurisdicción.
/// </summary>
public abstract class ControladorBase : ControllerBase
{
    public const string EncabezadoUsuario = "X-Usuario";
    public const string EncabezadoRol = "X-Rol";
    public const string EncabezadoAmbito = "X-Ambito";

    protected bool IntentarActor(out Actor actor, out IActionResult error)
    {
        actor = new Actor("", Rol.Auditor);
        error = Unauthorized(new ErrorHttp("SIN_IDENTIDAD", "La petición no trae usuario y rol válidos."));

        string? usuario = Request.Headers[EncabezadoUsuario].FirstOrDefault();
        string? rolTexto = Request.Headers[EncabezadoRol].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(usuario) || !Enum.TryParse(rolTexto, true, out Rol rol) || !Enum.IsDefined(rol))
        {
            return false;
        }
        Ambito? ambito = null;
        string? ambitoTexto = Request.Headers[EncabezadoAmbito].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(ambitoTexto))
        {
            if (!Enum.TryParse(ambitoTexto, true, out Ambito leido) || !Enum.IsDefined(leido))
            {
                return false;
            }
            ambito = leido;
        }
        actor = new Actor(usuario.Trim(), rol, ambito);
        return true;
    }
}
