using System.Text.Json;
using EvaluacionDanos.Api.Seguridad;
using EvaluacionDanos.Aplicacion;
using EvaluacionDanos.Aplicacion.CasosDeUso;
using EvaluacionDanos.Dominio;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Comun.Seguridad;

namespace EvaluacionDanos.Api.Controllers;

// Cuerpos de las peticiones de la consola. Todo acepta null para responder con un error claro en vez de un 400 genérico.

public sealed record SolicitudValidacionHttp(string? Estado, string? Motivo);

public sealed record SolicitudAsociacionHttp(Guid? DesastreId);

public sealed record SolicitudResolucionHttp(string? Decision, string? Justificacion);

public sealed record SolicitudTipoActivoHttp(string? Codigo, string? Nombre, List<CriterioSolicitado>? Criterios);

/// <summary>Una versión con su contenido como objeto JSON (así lo leía la revisión del prototipo).</summary>
public sealed record VersionEvaluacionHttp(Guid VersionId, Guid EvaluacionId, int NumeroVersion, string Autor,
    string IdDispositivo, int? VersionActivoBase, bool EnConflicto, DateTime Instante, JsonElement Contenido,
    MotivoVersion Motivo, string? Detalle)
{
    public static VersionEvaluacionHttp De(VersionEvaluacionDto v)
    {
        using var documento = JsonDocument.Parse(v.ContenidoJson);
        return new VersionEvaluacionHttp(v.VersionId, v.EvaluacionId, v.NumeroVersion, v.Autor, v.IdDispositivo,
            v.VersionActivoBase, v.EnConflicto, v.Instante, documento.RootElement.Clone(), v.Motivo, v.Detalle);
    }
}

public sealed record ConflictoHttp(Guid Id, Guid ActivoId, TipoConflicto Tipo, EstadoConflicto Estado, string Regla,
    DateTime Instante, VersionEvaluacionHttp? Vigente, VersionEvaluacionHttp Entrante, DecisionConflicto? Decision,
    string? DecididoPor, string? Justificacion, DateTime? InstanteResolucion)
{
    public static ConflictoHttp De(ConflictoDto c) => new(c.Id, c.ActivoId, c.Tipo, c.Estado, c.Regla, c.Instante,
        c.Vigente is null ? null : VersionEvaluacionHttp.De(c.Vigente), VersionEvaluacionHttp.De(c.Entrante),
        c.Decision, c.DecididoPor, c.Justificacion, c.InstanteResolucion);
}

[ApiController]
[Route("v1/evaluaciones")]
public class EvaluacionesController(
    ICasoDeUso<string?, List<EvaluacionResumenDto>> consultar,
    ICasoDeUso<SolicitudRegistro, ResultadoOperacionDto> registrar,
    ICasoDeUso<SolicitudActualizacion, EvaluacionDetalleDto> actualizar,
    ICasoDeUso<SolicitudValidacion, EvaluacionDetalleDto> validar,
    ICasoDeUso<SolicitudAsociacion, EvaluacionDetalleDto> asociar,
    IIdentidadActual identidad) : ControladorBase(identidad)
{
    /// <summary>Evaluaciones de un dispositivo con su estado y número de versiones (E-09). Verifica QA-14.</summary>
    [HttpGet]
    [RequiereRol(RolesPlataforma.Evaluador, RolesPlataforma.Operador, RolesPlataforma.Autoridad, RolesPlataforma.Organismo)]
    public async Task<IActionResult> Listar([FromQuery] string? idDispositivo, [FromQuery] string? dispositivo, CancellationToken cancelar) =>
        Presentar(await consultar.EjecutarAsync(idDispositivo ?? dispositivo, cancelar));

    /// <summary>
    /// Registro con conexión (RF-ED-01, AD-29). El cuerpo es una operación como las de la sincronización; sin
    /// <c>tipo</c> es CREAR. 201 si quedó guardada (aplicada, en conflicto o posible duplicado), 422 si es inválida.
    /// </summary>
    [HttpPost]
    [RequiereRol(RolesPlataforma.Evaluador)]
    public async Task<IActionResult> Registrar(Operacion operacion, CancellationToken cancelar) =>
        Presentar(await registrar.EjecutarAsync(new SolicitudRegistro(operacion, Actor), cancelar), resultado =>
            resultado.Resultado == ResultadoOperacion.INVALIDA
                ? UnprocessableEntity(resultado)
                : StatusCode(StatusCodes.Status201Created, resultado));

    /// <summary>Actualización que conserva la versión anterior, con autor e instante (RF-ED-07).</summary>
    [HttpPatch("{id:guid}")]
    [RequiereRol(RolesPlataforma.Evaluador, RolesPlataforma.Operador)]
    public async Task<IActionResult> Actualizar(Guid id, CambiosEvaluacion cambios, CancellationToken cancelar) =>
        Presentar(await actualizar.EjecutarAsync(new SolicitudActualizacion(id, cambios, Actor), cancelar));

    /// <summary>Marca la evaluación como validada, rechazada o pendiente (RF-ED-08). Publica EV-08.</summary>
    [HttpPost("{id:guid}/validacion")]
    [RequiereRol(RolesPlataforma.Autoridad, RolesPlataforma.Organismo)]
    public async Task<IActionResult> Validar(Guid id, SolicitudValidacionHttp solicitud, CancellationToken cancelar) =>
        Presentar(await validar.EjecutarAsync(new SolicitudValidacion(id, solicitud.Estado, solicitud.Motivo, Actor), cancelar));

    /// <summary>Vincula una evaluación preliminar a un desastre (AD-29).</summary>
    [HttpPost("{id:guid}/asociacion")]
    [RequiereRol(RolesPlataforma.Operador, RolesPlataforma.Evaluador)]
    public async Task<IActionResult> Asociar(Guid id, SolicitudAsociacionHttp solicitud, CancellationToken cancelar) =>
        Presentar(await asociar.EjecutarAsync(new SolicitudAsociacion(id, solicitud.DesastreId, Actor), cancelar));
}

/// <summary>Bandeja de revisión: la lee y la resuelve Evaluación de Daños (DA-09, E-13).</summary>
[ApiController]
[Route("v1/conflictos")]
[RequiereRol(RolesPlataforma.Operador)]
public class ConflictosController(
    ICasoDeUso<string, List<ConflictoDto>> consultar,
    ICasoDeUso<SolicitudResolucion, ResolucionDto> resolver,
    IIdentidadActual identidad) : ControladorBase(identidad)
{
    /// <summary>Conflictos con ambas versiones completas, para la revisión humana.</summary>
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken cancelar, [FromQuery] string estado = "PENDIENTE") =>
        Presentar(await consultar.EjecutarAsync(estado, cancelar), lista => Ok(lista.Select(ConflictoHttp.De).ToList()));

    /// <summary>Decisión humana (AD-32, AD-33): prevalece una versión, se fusiona o se rechaza el duplicado. Publica EV-22.</summary>
    [HttpPost("{id:guid}/resolucion")]
    public async Task<IActionResult> Resolver(Guid id, SolicitudResolucionHttp solicitud, CancellationToken cancelar) =>
        Presentar(await resolver.EjecutarAsync(new SolicitudResolucion(id, solicitud.Decision, solicitud.Justificacion, Actor), cancelar));
}

/// <summary>Administración del catálogo de tipos de activo en ejecución (AD-28, DA-18, QA-10).</summary>
[ApiController]
[Route("v1/catalogos/tipos-activo")]
[RequiereRol(RolesPlataforma.Administrador)]
public class CatalogosController(
    ICasoDeUso<bool, List<TipoActivoDto>> consultar,
    CrearTipoActivo crear,
    ModificarTipoActivo modificar,
    IIdentidadActual identidad) : ControladorBase(identidad)
{
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken cancelar) => Presentar(await consultar.EjecutarAsync(true, cancelar));

    [HttpPost]
    public async Task<IActionResult> Crear(SolicitudTipoActivoHttp solicitud, CancellationToken cancelar) =>
        Presentar(await crear.EjecutarAsync(new SolicitudTipoActivo(solicitud.Codigo, solicitud.Nombre, solicitud.Criterios), cancelar),
            tipo => StatusCode(StatusCodes.Status201Created, tipo));

    [HttpPatch("{codigo}")]
    public async Task<IActionResult> Modificar(string codigo, SolicitudTipoActivoHttp solicitud, CancellationToken cancelar) =>
        Presentar(await modificar.EjecutarAsync(new SolicitudTipoActivo(codigo, solicitud.Nombre, solicitud.Criterios), cancelar));
}
