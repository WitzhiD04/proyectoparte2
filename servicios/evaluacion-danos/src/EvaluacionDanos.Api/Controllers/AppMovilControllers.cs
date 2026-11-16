using EvaluacionDanos.Api.Seguridad;
using EvaluacionDanos.Aplicacion;
using EvaluacionDanos.Aplicacion.CasosDeUso;
using EvaluacionDanos.Dominio;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Comun.Seguridad;

namespace EvaluacionDanos.Api.Controllers;

// Rutas que usa la app móvil (SAD, Cuadro 15). Su contrato no cambia: mismos nombres y tipos; lo nuevo es aditivo.

/// <summary>Descarga previa (RF-ED-12): lo que la app necesita para trabajar sin red.</summary>
[ApiController]
[Route("v1/descarga")]
[RequiereRol(RolesPlataforma.Evaluador)]
public class DescargaController(ICasoDeUso<SolicitudDescarga, DescargaDto> descarga, IIdentidadActual identidad)
    : ControladorBase(identidad)
{
    /// <param name="desastreId">Solo ese desastre y los activos de su delimitación.</param>
    /// <param name="zona">Código DIVIPOLA de municipio (5 dígitos) o de departamento (2).</param>
    /// <param name="desde">El <c>instanteServidor</c> de la descarga anterior: solo los activos que cambiaron después.</param>
    [HttpGet]
    public async Task<IActionResult> Obtener([FromQuery] Guid? desastreId, [FromQuery] string? zona,
        [FromQuery] DateTimeOffset? desde, CancellationToken cancelar) =>
        Presentar(await descarga.EjecutarAsync(new SolicitudDescarga(desastreId, zona, desde?.UtcDateTime), cancelar));
}

/// <summary>Recibe el lote de operaciones de la app y devuelve un resultado por operación, en el mismo orden.</summary>
[ApiController]
[Route("v1/sincronizacion")]
[RequiereRol(RolesPlataforma.Evaluador)]
public class SincronizacionController(SincronizarLote sincronizacion, Parametros parametros, IIdentidadActual identidad)
    : ControladorBase(identidad)
{
    [HttpPost("evaluaciones")]
    public async Task<ActionResult<RespuestaSincronizacionDto>> Evaluaciones(LoteSincronizacion lote, CancellationToken cancelar)
    {
        // Se toma al llegar, antes de cualquier trabajo, para medir bien el desfase de reloj del celular.
        var instanteRecepcion = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(lote.IdDispositivo))
        {
            return BadRequest(new ErrorDto("Falta idDispositivo en el lote"));
        }
        if (lote.IdDispositivo.Length > Limites.IdDispositivo)
        {
            return BadRequest(new ErrorDto($"idDispositivo supera los {Limites.IdDispositivo} caracteres"));
        }
        // La dueña de cada evaluación es el dispositivo del cuerpo: debe ser el mismo que se identificó.
        if (lote.IdDispositivo != Identidad.IdDispositivo)
        {
            return BadRequest(new ErrorDto($"idDispositivo del cuerpo no coincide con el encabezado {IdentidadPorEncabezados.Dispositivo}"));
        }
        if (lote.Operaciones is null)
        {
            return BadRequest(new ErrorDto("Falta la lista de operaciones"));
        }
        int maximo = parametros.MaxOperacionesPorLote;
        if (lote.Operaciones.Count > maximo)
        {
            return BadRequest(new ErrorDto($"El lote trae {lote.Operaciones.Count} operaciones; el máximo es {maximo}"));
        }

        try
        {
            var resultados = await sincronizacion.ProcesarLoteAsync(lote, instanteRecepcion, cancelar);
            return new RespuestaSincronizacionDto(resultados);
        }
        catch (Exception ex) when (ex is LoteIncompletoException or CapaNoDisponibleException)
        {
            // Sin 2xx la app conserva todo el lote en su cola y lo reenvía; lo ya procesado es idempotente.
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorDto(ex.Message));
        }
    }
}

/// <summary>Subida de evidencias (multipart, parte "archivo"). Idempotente por id.</summary>
[ApiController]
[Route("v1/evidencias")]
[RequiereRol(RolesPlataforma.Evaluador)]
public class EvidenciasController(SubirEvidencia evidencias) : ControllerBase
{
    [HttpPut("{id:guid}")]
    [RequestSizeLimit(64 * 1024 * 1024)]   // el tope real (TamanoMaximoMb) lo aplica el caso de uso con 413
    [RequestFormLimits(MultipartBodyLengthLimit = 64 * 1024 * 1024)]
    public async Task<IActionResult> Subir(Guid id, IFormFile? archivo, CancellationToken cancelar)
    {
        if (archivo is null)
        {
            return BadRequest(new ErrorDto("Falta la parte 'archivo' en el multipart"));
        }

        await using var contenido = archivo.OpenReadStream();
        var resultado = await evidencias.SubirAsync(id, contenido, archivo.Length, cancelar);

        return resultado.Tipo switch
        {
            TipoResultadoSubida.Subida => Ok(resultado.Evidencia),
            TipoResultadoSubida.NoDeclarada => NotFound(new ErrorDto($"La evidencia {id} no fue declarada en ningún lote")),
            TipoResultadoSubida.MuyGrande => StatusCode(StatusCodes.Status413PayloadTooLarge,
                new ErrorDto(resultado.Mensaje ?? "Archivo demasiado grande")),
            TipoResultadoSubida.HashDistinto => UnprocessableEntity(new ErrorDto(resultado.Mensaje ?? "El hash no coincide")),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}

/// <summary>
/// Puntos [lat, lon, peso] de la evaluación vigente de cada activo (RF-ED-11, AD-16). Se conserva igual que en el
/// prototipo, junto con la página /mapa, hasta que lo reemplace el servicio de Consulta de Daños
/// (<c>/v1/consulta-danos/mapa</c>, DA-08, E-09).
/// </summary>
[Obsolete("Lo reemplaza Consulta de Daños (/v1/consulta-danos/mapa, DA-08) en el paso 5.")]
[ApiController]
[Route("v1/mapa-calor")]
[RequiereRol]
public class MapaCalorController(ICasoDeUso<SolicitudMapaCalor, MapaCalorDto> mapa, IIdentidadActual identidad)
    : ControladorBase(identidad)
{
    [HttpGet]
    public async Task<IActionResult> Obtener([FromQuery] Guid? desastreId, [FromQuery] string? nivelMinimo, CancellationToken cancelar) =>
        Presentar(await mapa.EjecutarAsync(new SolicitudMapaCalor(desastreId, nivelMinimo), cancelar));
}
