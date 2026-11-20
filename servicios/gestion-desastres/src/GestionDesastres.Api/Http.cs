using GestionDesastres.Aplicacion;
using GestionDesastres.Dominio;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Comun.Seguridad;

namespace GestionDesastres.Api;

/// <summary>Cuerpo de una respuesta de error.</summary>
public sealed record ErrorHttp(string Codigo, string Mensaje);

/// <summary>Presenter: implementa el puerto de salida y lo convierte en una respuesta HTTP.</summary>
public sealed class PresentadorHttp<T> : IPuertoSalida<T>
{
    private readonly int _codigoExito;
    public PresentadorHttp(int codigoExito = StatusCodes.Status200OK) { _codigoExito = codigoExito; }

    public IActionResult Resultado { get; private set; } = new StatusCodeResult(StatusCodes.Status500InternalServerError);

    public void Exito(T respuesta) => Resultado = new ObjectResult(respuesta) { StatusCode = _codigoExito };
    public void NoEncontrado(string mensaje) => Resultado = new NotFoundObjectResult(new ErrorHttp("NO_ENCONTRADO", mensaje));
    public void NoAutorizado(string mensaje) =>
        Resultado = new ObjectResult(new ErrorHttp("NO_AUTORIZADO", mensaje)) { StatusCode = StatusCodes.Status403Forbidden };

    public void ReglaIncumplida(string codigo, string mensaje)
    {
        // 409: la acción no cabe en el estado actual o chocó con otro cambio. 422: el dato enviado incumple una regla.
        bool conflicto = codigo is "TRANSICION_NO_PERMITIDA" or "CONFLICTO" or "DESASTRE_CERRADO" or "PROYECTOS_ABIERTOS"
            or "TIPO_EXISTE" or "PERFIL_EXISTE" or "ID_EXISTE" or "PERFIL_EN_USO";
        Resultado = new ObjectResult(new ErrorHttp(codigo, mensaje))
            { StatusCode = conflicto ? StatusCodes.Status409Conflict : StatusCodes.Status422UnprocessableEntity };
    }
}

/// <summary>
/// Base de los controladores: arma el actor del dominio con la identidad de la petición (IIdentidadActual, DA-15) y
/// traduce los valores canónicos que usa la API (los mismos del contrato de eventos, E-08) a los del dominio.
/// </summary>
public abstract class ControladorBase : ControllerBase
{
    protected bool IntentarActor(out Actor actor, out IActionResult error)
    {
        Identidad? identidad = HttpContext.RequestServices.GetRequiredService<IIdentidadActual>().Actual;
        actor = new Actor("", null, false, false, false, false);
        error = Unauthorized(new ErrorHttp("SIN_IDENTIDAD", "La petición no trae una identidad válida."));
        if (identidad is null)
        {
            return false;
        }
        actor = new Actor(identidad.Usuario, identidad.Organizacion,
            identidad.Tiene(RolesPlataforma.Autoridad), identidad.Tiene(RolesPlataforma.Organismo), identidad.Tiene(RolesPlataforma.Administrador),
            identidad.Tiene(RolesPlataforma.Autoridad) && identidad.Ambito == AmbitosPlataforma.Nacional);
        return true;
    }

    protected IActionResult Invalido(string mensaje) => UnprocessableEntity(new ErrorHttp("DATO_INVALIDO", mensaje));

    protected static bool LeerSeveridad(string? texto, out Severidad severidad)
    {
        severidad = default;
        return texto?.Trim().ToUpperInvariant() switch
        {
            "NIVEL_1" => Asignar(Severidad.Nivel1, out severidad),
            "NIVEL_2" => Asignar(Severidad.Nivel2, out severidad),
            "NIVEL_3" => Asignar(Severidad.Nivel3, out severidad),
            "NIVEL_4" => Asignar(Severidad.Nivel4, out severidad),
            _ => false,
        };
    }

    protected static bool LeerEstado(string? texto, out EstadoDesastre estado)
    {
        estado = default;
        return texto?.Trim().ToUpperInvariant() switch
        {
            "DETECTADO" => Asignar(EstadoDesastre.Detectado, out estado),
            "REGISTRADO" => Asignar(EstadoDesastre.Registrado, out estado),
            "EN_EVALUACION" => Asignar(EstadoDesastre.EnEvaluacion, out estado),
            "EN_ATENCION" => Asignar(EstadoDesastre.EnAtencion, out estado),
            "EN_RECUPERACION" => Asignar(EstadoDesastre.EnRecuperacion, out estado),
            "EN_RECONSTRUCCION" => Asignar(EstadoDesastre.EnReconstruccion, out estado),
            "CERRADO" => Asignar(EstadoDesastre.Cerrado, out estado),
            _ => false,
        };
    }

    protected static bool LeerFuente(string? texto, out FuenteDelimitacion fuente)
    {
        fuente = FuenteDelimitacion.DecisionAutoridad;
        return texto is null
            || Enum.TryParse(texto.Replace("_", ""), ignoreCase: true, out fuente);
    }

    private static bool Asignar<T>(T valor, out T destino)
    {
        destino = valor;
        return true;
    }
}
