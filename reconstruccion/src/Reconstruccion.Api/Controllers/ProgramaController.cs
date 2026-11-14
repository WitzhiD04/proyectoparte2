using Microsoft.AspNetCore.Mvc;
using Reconstruccion.Api.Presenters;
using Reconstruccion.Aplicacion;
using Reconstruccion.Aplicacion.CasosDeUso;
using Reconstruccion.Dominio;

namespace Reconstruccion.Api.Controllers;

/// <summary>CU-11. Lo atiende el servicio de Consulta del Programa (rol "consulta").</summary>
[ApiController]
[Route("v1/programa")]
public sealed class ProgramaController : ControladorBase
{
    [HttpGet("consolidado")]
    public async Task<IActionResult> Consolidado([FromQuery] Guid desastre, [FromServices] IConsultarConsolidadoEntrada casoDeUso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<ConsolidadoDto> presentador = new();
        await casoDeUso.EjecutarAsync(new SolicitudConsolidado(actor, desastre), presentador, ct);
        return presentador.Resultado;
    }
}
