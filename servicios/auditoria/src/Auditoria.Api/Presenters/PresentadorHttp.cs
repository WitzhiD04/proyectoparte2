using Auditoria.Aplicacion.Puertos;
using Microsoft.AspNetCore.Mvc;

namespace Auditoria.Api.Presenters;

/// <summary>Cuerpo de una respuesta de error.</summary>
public sealed record ErrorHttp(string Codigo, string Mensaje);

/// <summary>
/// Presenter: implementa el puerto de salida y convierte lo que entrega el caso de uso en una respuesta HTTP.
/// Opcionalmente transforma el resultado antes de escribirlo (por ejemplo, el sobre de texto a JSON).
/// </summary>
public sealed class PresentadorHttp<T> : IPuertoSalida<T>
{
    private readonly Func<T, object?> _transformar;

    public PresentadorHttp(Func<T, object?>? transformar = null)
    {
        _transformar = transformar ?? (r => r);
    }

    public IActionResult Resultado { get; private set; } = new StatusCodeResult(StatusCodes.Status500InternalServerError);

    public void Exito(T respuesta)
    {
        Resultado = new OkObjectResult(_transformar(respuesta));
    }

    public void NoAutorizado(string mensaje)
    {
        Resultado = new ObjectResult(new ErrorHttp("NO_AUTORIZADO", mensaje)) { StatusCode = StatusCodes.Status403Forbidden };
    }

    public void ReglaIncumplida(string codigo, string mensaje)
    {
        // Un parámetro de consulta inválido es un error del cliente en la petición (400).
        int estado = codigo == "CONSULTA_INVALIDA" ? StatusCodes.Status400BadRequest : StatusCodes.Status422UnprocessableEntity;
        Resultado = new ObjectResult(new ErrorHttp(codigo, mensaje)) { StatusCode = estado };
    }
}
