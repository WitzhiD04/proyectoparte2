using System.Text.Json;
using Auditoria.Api.Presenters;
using Auditoria.Aplicacion;
using Auditoria.Aplicacion.CasosDeUso;
using Auditoria.Dominio;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Comun.Seguridad;

namespace Auditoria.Api.Controllers;

/// <summary>
/// Consulta del registro de auditoría (AD-21, DA-17). Solo tiene rutas GET: el registro se alimenta exclusivamente del
/// bus y no existe ninguna ruta de escritura, modificación ni borrado. Una prueba de integración lo verifica.
/// </summary>
[ApiController]
[Route("v1/auditoria")]
public sealed class AuditoriaController : ControllerBase
{
    private readonly IIdentidadActual _identidad;

    public AuditoriaController(IIdentidadActual identidad)
    {
        _identidad = identidad;
    }

    /// <summary>El rastro, filtrado y paginado, en orden cronológico de emisión (QA-13, QA-15).</summary>
    [HttpGet]
    public async Task<IActionResult> Consultar(
        [FromQuery] string? entidad, [FromQuery] string? idEntidad, [FromQuery] string? actor, [FromQuery] string? tipo,
        [FromQuery] string? idCorrelacion, [FromQuery] DateTimeOffset? desde, [FromQuery] DateTimeOffset? hasta,
        [FromQuery] int? pagina, [FromQuery] int? tamano,
        [FromServices] IConsultarRegistroEntrada casoDeUso, CancellationToken ct)
    {
        if (!IntentarSolicitante(out Solicitante solicitante, out IActionResult error)) return error;
        FiltroRegistro filtro = new(Vacio(entidad), Vacio(idEntidad), Vacio(actor), Vacio(tipo), Vacio(idCorrelacion), desde, hasta);
        PresentadorHttp<PaginaRegistroDto> presentador = new(p => new
        {
            p.Pagina,
            p.Tamano,
            p.Total,
            Entradas = p.Entradas.Select(AHttp).ToList(),
        });
        await casoDeUso.EjecutarAsync(new SolicitudConsultaRegistro(solicitante, filtro,
            pagina ?? 1, tamano ?? ConsultarRegistro.TamanoPorOmision), presentador, ct);
        return presentador.Resultado;
    }

    /// <summary>Huecos en la secuencia de los productores (ATAM R-02).</summary>
    [HttpGet("huecos")]
    public async Task<IActionResult> Huecos([FromServices] IConsultarHuecosEntrada casoDeUso, CancellationToken ct)
    {
        if (!IntentarSolicitante(out Solicitante solicitante, out IActionResult error)) return error;
        PresentadorHttp<HuecosDto> presentador = new();
        await casoDeUso.EjecutarAsync(new SolicitudHuecos(solicitante), presentador, ct);
        return presentador.Resultado;
    }

    /// <summary>Sin identidad: 401. Los roles se evalúan en el caso de uso (403).</summary>
    private bool IntentarSolicitante(out Solicitante solicitante, out IActionResult error)
    {
        Identidad? identidad = _identidad.Actual;
        solicitante = new Solicitante("", new HashSet<string>());
        error = Unauthorized(new ErrorHttp("SIN_IDENTIDAD", "La petición no trae una identidad válida."));
        if (identidad is null)
        {
            return false;
        }
        solicitante = new Solicitante(identidad.Usuario, identidad.Roles);
        return true;
    }

    private static string? Vacio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>El sobre se devuelve como JSON, no como texto escapado.</summary>
    private static object AHttp(EntradaDto e)
    {
        using JsonDocument sobre = JsonDocument.Parse(e.Sobre);
        return new
        {
            e.IdEvento, e.Tipo, e.Nombre, e.Version, e.Productor, e.SecuenciaProductor, e.InstanteEmision,
            e.InstanteRegistro, e.IdCorrelacion, e.ClaveIdempotencia, e.Actor, e.Entidad, e.IdEntidad, e.Accion,
            e.Resultado, e.ValorAnterior, e.ValorNuevo, e.Procedencia,
            Sobre = sobre.RootElement.Clone(),
        };
    }
}
