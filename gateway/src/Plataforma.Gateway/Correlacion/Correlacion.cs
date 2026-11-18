using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Plataforma.Gateway.Correlacion;

/// <summary>
/// Correlación de punta a punta (DA-20): cada petición sale del gateway con <c>traceparent</c> (W3C) y
/// <c>X-Id-Correlacion</c>. Los servicios copian <c>X-Id-Correlacion</c> en el <c>idCorrelacion</c> de los eventos
/// que emiten, y Auditoría lo indexa (QA-13). Si el cliente ya trae valores válidos se conservan, para que pueda
/// seguir su propia acción; si no, el gateway los asigna. La respuesta devuelve el <c>X-Id-Correlacion</c> usado.
/// </summary>
public sealed partial class CorrelacionPeticiones
{
    public const string EncabezadoCorrelacion = "X-Id-Correlacion";
    public const string EncabezadoTraza = "traceparent";

    private readonly RequestDelegate _siguiente;

    public CorrelacionPeticiones(RequestDelegate siguiente)
    {
        _siguiente = siguiente;
    }

    public Task InvokeAsync(HttpContext contexto)
    {
        IHeaderDictionary encabezados = contexto.Request.Headers;

        string? traza = encabezados[EncabezadoTraza].FirstOrDefault();
        if (traza is null || !EsTraceparent(traza))
        {
            Activity? actividad = Activity.Current;
            traza = actividad is { IdFormat: ActivityIdFormat.W3C } ? actividad.Id! : NuevoTraceparent();
            encabezados[EncabezadoTraza] = traza;
        }

        string? correlacion = encabezados[EncabezadoCorrelacion].FirstOrDefault();
        if (correlacion is null || !CorrelacionValida().IsMatch(correlacion))
        {
            // Por omisión, el id de traza W3C: así la correlación y la traza coinciden.
            correlacion = traza.Split('-')[1];
            encabezados[EncabezadoCorrelacion] = correlacion;
        }

        contexto.Response.OnStarting(() =>
        {
            contexto.Response.Headers[EncabezadoCorrelacion] = correlacion;
            return Task.CompletedTask;
        });
        return _siguiente(contexto);
    }

    public static bool EsTraceparent(string valor) =>
        TraceparentValido().IsMatch(valor) && valor[3..35] != new string('0', 32) && valor[36..52] != new string('0', 16);

    private static string NuevoTraceparent() =>
        $"00-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))}-01";

    [GeneratedRegex("^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$")]
    private static partial Regex TraceparentValido();

    /// <summary>Lo que el gateway acepta de un cliente: corto y sin caracteres que puedan romper un log o un encabezado.</summary>
    [GeneratedRegex("^[A-Za-z0-9._:-]{1,64}$")]
    private static partial Regex CorrelacionValida();
}
