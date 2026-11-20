using EvaluacionDanos.Dominio;

namespace EvaluacionDanos.Aplicacion;

// Respuestas de los casos de uso. Las que lee la app móvil (descarga, sincronización y evidencias) conservan el
// nombre y el tipo de cada campo del prototipo; lo nuevo es aditivo, porque la app ignora los campos que no conoce.

public record ErrorDto(string Error);

public record CriterioDto(NivelDano Nivel, string Criterio);

/// <param name="Version">Aditivo: versión del tipo en el catálogo (AD-28).</param>
public record TipoActivoDto(string Codigo, string Nombre, List<CriterioDto> Criterios, int Version = 1);

/// <param name="Estado">Aditivo: estado del desastre en la copia local (E-29).</param>
/// <param name="CodigosMunicipio">Aditivo: municipios de la delimitación vigente (EV-05).</param>
public record DesastreDto(Guid Id, string Nombre, string Tipo, EstadoDesastre? Estado = null, List<string>? CodigosMunicipio = null);

/// <param name="CodigoMunicipio">Aditivo: municipio asignado en el servidor (E-12).</param>
public record ActivoDto(Guid Id, string TipoCodigo, double Lat, double Lon, string Referencia, int Version,
    string? CodigoMunicipio = null);

/// <param name="Incremental">Aditivo: verdadero si <c>Activos</c> trae solo los cambios desde <c>Desde</c> (DA-19).</param>
public record DescargaDto(
    DateTime InstanteServidor,
    List<TipoActivoDto> TiposActivo,
    List<DesastreDto> Desastres,
    List<ActivoDto> Activos,
    bool Incremental = false,
    DateTime? Desde = null);

// ---------- Sincronización ----------

public record ResultadoOperacionDto(
    Guid IdOperacion,
    ResultadoOperacion Resultado,
    EstadoEvaluacion? EstadoEvaluacion,
    string? Motivo,
    int? NuevaVersionActivo,
    DateTime InstanteServidor);

public record RespuestaSincronizacionDto(List<ResultadoOperacionDto> Resultados);

// ---------- Evidencias ----------

public record EvidenciaSubidaDto(Guid Id, Guid EvaluacionId, bool Subida, DateTime? InstanteSubida, long TamanoBytes);

// ---------- Revisión ----------

/// <summary>Una versión del historial. <c>ContenidoJson</c> es el contenido tal como se guardó; la capa de entrada lo presenta.</summary>
public record VersionEvaluacionDto(
    Guid VersionId,
    Guid EvaluacionId,
    int NumeroVersion,
    string Autor,
    string IdDispositivo,
    int? VersionActivoBase,
    bool EnConflicto,
    DateTime Instante,
    string ContenidoJson,
    MotivoVersion Motivo,
    string? Detalle);

public record ConflictoDto(
    Guid Id,
    Guid ActivoId,
    TipoConflicto Tipo,
    EstadoConflicto Estado,
    string Regla,
    DateTime Instante,
    VersionEvaluacionDto? Vigente,
    VersionEvaluacionDto Entrante,
    DecisionConflicto? Decision,
    string? DecididoPor,
    string? Justificacion,
    DateTime? InstanteResolucion);

public record EvaluacionResumenDto(
    Guid Id,
    Guid ActivoId,
    Guid? DesastreId,
    string IdDispositivo,
    EstadoEvaluacion Estado,
    NivelDano NivelDano,
    DateTime InstanteCampo,
    DateTime InstanteServidor,
    int NumeroVersiones,
    int EvidenciasDeclaradas,
    int EvidenciasSubidas);

/// <summary>Estado de una evaluación después de un cambio hecho en el servidor.</summary>
public record EvaluacionDetalleDto(
    Guid Id,
    Guid ActivoId,
    Guid? DesastreId,
    string IdDispositivo,
    EstadoEvaluacion Estado,
    NivelDano NivelDano,
    string TipoActivoCodigo,
    string Descripcion,
    double Lat,
    double Lon,
    double PrecisionM,
    string Evaluador,
    string Organizacion,
    DateTime InstanteCampo,
    DateTime InstanteServidor,
    int NumeroVersion,
    int VersionActivo,
    string CodigoMunicipio,
    string? ValidadoPor,
    DateTime? InstanteValidacion,
    Guid? DuplicadoDe);

public record ResolucionDto(Guid IdConflicto, DecisionConflicto Decision, EvaluacionDetalleDto Vigente);

