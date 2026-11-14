using Microsoft.AspNetCore.Mvc;
using Reconstruccion.Aplicacion.Puertos;

namespace Reconstruccion.Api.Presenters;

/// <summary>Cuerpo de una respuesta de error.</summary>
public sealed record ErrorHttp(string Codigo, string Mensaje, IReadOnlyList<string>? Detalles = null);

/// <summary>
/// Presenter: implementa el puerto de salida y convierte lo que entrega el caso de uso en una respuesta HTTP.
/// </summary>
public sealed class PresentadorHttp<T> : IPuertoSalida<T>
{
    private readonly int _codigoExito;

    public PresentadorHttp(int codigoExito = StatusCodes.Status200OK)
    {
        _codigoExito = codigoExito;
    }

    public IActionResult Resultado { get; private set; } = new StatusCodeResult(StatusCodes.Status500InternalServerError);

    public void Exito(T respuesta)
    {
        Resultado = new ObjectResult(respuesta) { StatusCode = _codigoExito };
    }

    public void NoEncontrado(string mensaje)
    {
        Resultado = new NotFoundObjectResult(new ErrorHttp("NO_ENCONTRADO", mensaje));
    }

    public void NoAutorizado(string mensaje)
    {
        Resultado = new ObjectResult(new ErrorHttp("NO_AUTORIZADO", mensaje)) { StatusCode = StatusCodes.Status403Forbidden };
    }

    public void ReglaIncumplida(string codigo, string mensaje, IReadOnlyList<string>? detalles = null)
    {
        // 409: la acción no cabe en el estado actual o chocó con otro cambio. 422: el dato enviado incumple una regla.
        bool conflicto = codigo == "TRANSICION_NO_PERMITIDA" || codigo == "CONFLICTO" || codigo == "ESTADO_FINAL";
        int estado = conflicto ? StatusCodes.Status409Conflict : StatusCodes.Status422UnprocessableEntity;
        Resultado = new ObjectResult(new ErrorHttp(codigo, mensaje, detalles)) { StatusCode = estado };
    }
}
