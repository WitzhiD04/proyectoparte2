using Microsoft.AspNetCore.Mvc;
using Reconstruccion.Api.Presenters;
using Reconstruccion.Aplicacion;
using Reconstruccion.Aplicacion.CasosDeUso;
using Reconstruccion.Dominio;

namespace Reconstruccion.Api.Controllers;

/// <summary>Recibe las peticiones sobre proyectos y llama al puerto de entrada de cada caso de uso.</summary>
[ApiController]
[Route("v1")]
public sealed class ProyectosController : ControladorBase
{
    [HttpGet("reconstruccion/desastres")]
    public async Task<IActionResult> Desastres([FromServices] IConsultarDesastresEntrada casoDeUso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<IReadOnlyList<DesastreDto>> presentador = new();
        await casoDeUso.EjecutarAsync(new SolicitudDesastres(actor), presentador, ct);
        return presentador.Resultado;
    }

    /// <summary>CU-01</summary>
    [HttpGet("reconstruccion/candidatas")]
    public async Task<IActionResult> Candidatas([FromQuery] Guid desastre, [FromServices] IConsultarCandidatasEntrada casoDeUso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<IReadOnlyList<CandidataDto>> presentador = new();
        await casoDeUso.EjecutarAsync(new SolicitudCandidatas(actor, desastre), presentador, ct);
        return presentador.Resultado;
    }

    /// <summary>CU-02</summary>
    [HttpPost("proyectos:lote")]
    public async Task<IActionResult> CrearLote([FromBody] CuerpoLote cuerpo, [FromServices] ICrearProyectosPorLoteEntrada casoDeUso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<ResultadoLoteDto> presentador = new(StatusCodes.Status201Created);
        await casoDeUso.EjecutarAsync(new SolicitudLote(actor, cuerpo.IdDesastre, cuerpo.IdsEvaluaciones, cuerpo.ClaveIdempotencia), presentador, ct);
        return presentador.Resultado;
    }

    [HttpGet("proyectos")]
    public async Task<IActionResult> Listar([FromQuery] Guid desastre, [FromServices] IConsultarProyectosEntrada casoDeUso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<IReadOnlyList<ProyectoDto>> presentador = new();
        await casoDeUso.EjecutarAsync(new SolicitudProyectos(actor, desastre), presentador, ct);
        return presentador.Resultado;
    }

    [HttpGet("proyectos/{id:guid}")]
    public async Task<IActionResult> Ver(Guid id, [FromServices] IConsultarProyectoEntrada casoDeUso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<ProyectoDto> presentador = new();
        await casoDeUso.EjecutarAsync(new SolicitudProyecto(actor, id), presentador, ct);
        return presentador.Resultado;
    }

    /// <summary>CU-03, presupuesto, fuente y cronograma</summary>
    [HttpPatch("proyectos/{id:guid}/presupuesto")]
    public async Task<IActionResult> AsignarPresupuesto(Guid id, [FromBody] CuerpoPresupuesto cuerpo, [FromServices] IAsignarPresupuestoEntrada casoDeUso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<ProyectoDto> presentador = new();
        await casoDeUso.EjecutarAsync(new SolicitudAsignarPresupuesto(actor, id, cuerpo.Presupuesto, cuerpo.FuenteFinanciacion,
            cuerpo.Inicio, cuerpo.Fin, cuerpo.Justificacion), presentador, ct);
        return presentador.Resultado;
    }

    /// <summary>CU-03, responsable</summary>
    [HttpPatch("proyectos/{id:guid}/responsable")]
    public async Task<IActionResult> AsignarResponsable(Guid id, [FromBody] CuerpoResponsable cuerpo, [FromServices] IAsignarResponsableEntrada casoDeUso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<ProyectoDto> presentador = new();
        await casoDeUso.EjecutarAsync(new SolicitudAsignarResponsable(actor, id, cuerpo.Responsable, cuerpo.Justificacion), presentador, ct);
        return presentador.Resultado;
    }

    /// <summary>CU-04, CU-05, CU-08 y CU-09: el cuerpo dice qué transición se pide.</summary>
    [HttpPost("proyectos/{id:guid}/transiciones")]
    public async Task<IActionResult> Transicion(
        Guid id,
        [FromBody] CuerpoTransicion cuerpo,
        [FromServices] IAprobarProyectoEntrada aprobar,
        [FromServices] IIniciarEjecucionEntrada iniciar,
        [FromServices] IFinalizarProyectoEntrada finalizar,
        [FromServices] ICancelarProyectoEntrada cancelar,
        CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<ProyectoDto> presentador = new();
        switch ((cuerpo.Accion ?? "").Trim().ToUpperInvariant())
        {
            case "APROBAR":
                await aprobar.EjecutarAsync(new SolicitudAprobar(actor, id), presentador, ct);
                break;
            case "INICIAR":
                await iniciar.EjecutarAsync(new SolicitudIniciar(actor, id, cuerpo.Contratista), presentador, ct);
                break;
            case "FINALIZAR":
                await finalizar.EjecutarAsync(new SolicitudFinalizar(actor, id), presentador, ct);
                break;
            case "CANCELAR":
                await cancelar.EjecutarAsync(new SolicitudCancelar(actor, id, cuerpo.Justificacion ?? ""), presentador, ct);
                break;
            default:
                return BadRequest(new ErrorHttp("ACCION_DESCONOCIDA", "La acción debe ser APROBAR, INICIAR, FINALIZAR o CANCELAR."));
        }
        return presentador.Resultado;
    }

    /// <summary>CU-06</summary>
    [HttpPost("proyectos/{id:guid}/avance")]
    public async Task<IActionResult> RegistrarAvance(Guid id, [FromBody] CuerpoAvance cuerpo, [FromServices] IRegistrarAvanceYCostoEntrada casoDeUso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<ProyectoDto> presentador = new();
        await casoDeUso.EjecutarAsync(new SolicitudAvanceYCosto(actor, id, cuerpo.Avance, cuerpo.Costo, cuerpo.Concepto, cuerpo.Justificacion), presentador, ct);
        return presentador.Resultado;
    }

    /// <summary>CU-07</summary>
    [HttpPost("proyectos/{id:guid}/documentos")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> AdjuntarDocumento(Guid id, IFormFile archivo, [FromServices] IAdjuntarDocumentoEntrada casoDeUso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<DocumentoDto> presentador = new(StatusCodes.Status201Created);
        await using Stream contenido = archivo.OpenReadStream();
        string tipo = string.IsNullOrWhiteSpace(archivo.ContentType) ? "application/octet-stream" : archivo.ContentType;
        await casoDeUso.EjecutarAsync(new SolicitudDocumento(actor, id, archivo.FileName, tipo, contenido), presentador, ct);
        return presentador.Resultado;
    }

    /// <summary>CU-10</summary>
    [HttpGet("proyectos/{id:guid}/historial")]
    public async Task<IActionResult> Historial(Guid id, [FromQuery] DateTimeOffset? fecha, [FromServices] IConsultarHistorialEntrada casoDeUso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<HistorialDto> presentador = new();
        await casoDeUso.EjecutarAsync(new SolicitudHistorial(actor, id, fecha), presentador, ct);
        return presentador.Resultado;
    }
}
