using EvaluacionDanos.Dominio;

namespace EvaluacionDanos.Aplicacion.Puertos;

/// <summary>Otro cambio modificó lo mismo entre la lectura y el guardado (token de concurrencia o llave repetida).</summary>
public sealed class ConcurrenciaException(string mensaje, Exception interna) : Exception(mensaje, interna);

/// <summary>Municipio que contiene un punto (o el más cercano) y la versión de la capa con que se calculó (DA-07).</summary>
public sealed record MunicipioAsignado(string Codigo, int VersionCapa);

/// <summary>
/// Puerto de salida hacia el almacén de evaluaciones. Las entidades que devuelve quedan bajo seguimiento de la unidad
/// de trabajo: lo que se les cambie y lo que se agregue se guarda junto, con los hechos registrados (AD-45).
/// </summary>
public interface IAlmacenEvaluaciones
{
    Task<Evaluacion?> EvaluacionAsync(Guid id, CancellationToken cancelar);
    Task<Activo?> ActivoAsync(Guid id, CancellationToken cancelar);
    Task<Desastre?> DesastreAsync(Guid id, CancellationToken cancelar);
    Task<Conflicto?> ConflictoAsync(Guid id, CancellationToken cancelar);
    Task<EvaluacionVersion?> VersionAsync(Guid id, CancellationToken cancelar);
    Task<Evidencia?> EvidenciaAsync(Guid id, CancellationToken cancelar);
    Task<TipoActivo?> TipoActivoAsync(string codigo, CancellationToken cancelar);
    Task<List<TipoActivo>> TiposActivoAsync(CancellationToken cancelar);

    /// <summary>Códigos de los tipos de activo y de los desastres registrados: lo que revisa el validador.</summary>
    Task<(IReadOnlySet<string> Tipos, IReadOnlySet<Guid> Desastres)> CatalogoAsync(CancellationToken cancelar);

    /// <summary>Mayor número de versión guardado de la evaluación; 0 si no tiene ninguna.</summary>
    Task<int> UltimoNumeroVersionAsync(Guid evaluacionId, CancellationToken cancelar);

    /// <summary>Evaluación de la última versión del activo que tiene evaluación: la vigente.</summary>
    Task<Guid?> EvaluacionVigenteDelActivoAsync(Guid activoId, CancellationToken cancelar);

    /// <summary>Conflictos pendientes donde la evaluación es la entrante, sin contar <paramref name="excepto"/>.</summary>
    Task<bool> TieneConflictoPendienteComoEntranteAsync(Guid evaluacionId, Guid? excepto, CancellationToken cancelar);

    /// <summary>Conflictos pendientes donde la evaluación es la entrante o la vigente.</summary>
    Task<bool> TieneConflictoPendienteAsync(Guid evaluacionId, CancellationToken cancelar);

    /// <summary>Si alguna versión de la evaluación se aplicó sin conflicto (es decir, no nació en conflicto).</summary>
    Task<bool> TieneVersionAplicadaAsync(Guid evaluacionId, CancellationToken cancelar);

    Task<List<Guid>> IdsEvidenciasAsync(Guid evaluacionId, CancellationToken cancelar);

    void Agregar<T>(T entidad) where T : class;

    /// <summary>Guarda todo en una transacción. Lanza <see cref="ConcurrenciaException"/> si otro cambio llegó primero.</summary>
    Task GuardarAsync(CancellationToken cancelar);
}

/// <summary>Una operación de sincronización en curso: sus lecturas y escrituras van en una sola transacción.</summary>
public interface ITransaccionOperacion : IAsyncDisposable
{
    /// <summary>Guarda los cambios pendientes y confirma. Si no se llama, al liberar se revierte todo.</summary>
    Task GuardarYConfirmarAsync(CancellationToken cancelar);
}

/// <summary>Lo que la sincronización por lotes necesita además del almacén (FP-06, AD-43, AD-44, AD-33).</summary>
public interface IAlmacenSincronizacion : IAlmacenEvaluaciones
{
    /// <summary>Olvida las entidades en seguimiento: cada intento de una operación empieza limpio.</summary>
    void Limpiar();

    Task<ITransaccionOperacion> IniciarOperacionAsync(CancellationToken cancelar);

    /// <summary>
    /// Verdadero si el error es un choque con otro lote en paralelo (token de concurrencia, llave duplicada,
    /// serialización o interbloqueo). Un error permanente no se reintenta.
    /// </summary>
    bool EsCarrera(Exception error);

    /// <summary>Resultado guardado de una operación ya procesada, o null (reenvío idempotente, AD-44).</summary>
    Task<string?> ResultadoGuardadoAsync(Guid idOperacion, CancellationToken cancelar);

    /// <summary>Alguna de esas evidencias que ya pertenezca a otra evaluación, o null.</summary>
    Task<Guid?> EvidenciaDeOtraEvaluacionAsync(IReadOnlyCollection<Guid> ids, Guid evaluacionId, CancellationToken cancelar);

    /// <summary>
    /// Candado por tipo de activo hasta el fin de la transacción: dos lotes que crean a la vez activos cercanos del
    /// mismo tipo se ven entre sí.
    /// </summary>
    Task BloquearTipoAsync(string tipoCodigo, CancellationToken cancelar);

    /// <summary>Activos del tipo dentro del recuadro, con sus evaluaciones dentro de la ventana de tiempo.</summary>
    Task<List<CandidatoDuplicado>> CandidatosDuplicadoAsync(string tipoCodigo, Guid activoPropio,
        double minLat, double maxLat, double minLon, double maxLon, DateTime desde, DateTime hasta, CancellationToken cancelar);

    Task<List<VersionRegistrada>> VersionesDelActivoAsync(Guid activoId, CancellationToken cancelar);

    Task<HashSet<Guid>> EvidenciasDeclaradasAsync(Guid evaluacionId, CancellationToken cancelar);

    /// <summary>Número de la última versión no conflictiva de la evaluación (o de la última, si todas lo son).</summary>
    Task<int?> NumeroVersionVigenteAsync(Guid evaluacionId, CancellationToken cancelar);

    /// <summary>Municipio que contiene el punto o, si cae fuera de todos, el más cercano. Null si no hay capa cargada.</summary>
    Task<MunicipioAsignado?> MunicipioDeAsync(double lat, double lon, CancellationToken cancelar);

    /// <summary>Si ya se recibió completa la capa de municipios (todas las partes de alguna versión, EV-21).</summary>
    Task<bool> CapaDeMunicipiosCargadaAsync(CancellationToken cancelar);
}

/// <summary>Filtros de la descarga previa (DA-19, E-26).</summary>
public sealed record FiltroDescarga(Guid? DesastreId, string? Zona, DateTime? Desde);

public sealed record FilaVigente(Guid ActivoId, string TipoActivoCodigo, double Lat, double Lon, NivelDano NivelDano);

public sealed record DatosMapaCalor(List<FilaVigente> Vigentes, IReadOnlySet<Guid> ActivosEnConflicto,
    DateTime? InstanteDato, int ConflictosPendientes);

/// <summary>Puerto de salida de solo lectura: las consultas del servicio.</summary>
public interface IConsultasEvaluaciones
{
    Task<List<TipoActivoDto>> TiposActivoAsync(CancellationToken cancelar);

    /// <summary>Desastres registrados; sin los cerrados si <paramref name="soloAbiertos"/>.</summary>
    Task<List<DesastreDto>> DesastresAsync(bool soloAbiertos, Guid? soloEste, CancellationToken cancelar);

    /// <summary>Activos no fusionados, filtrados por municipios (null: todos) y por instante de cambio.</summary>
    Task<List<ActivoDto>> ActivosAsync(IReadOnlyCollection<string>? municipios, string? departamento, DateTime? desde,
        CancellationToken cancelar);

    Task<List<ConflictoDto>> ConflictosAsync(EstadoConflicto estado, CancellationToken cancelar);

    Task<List<EvaluacionResumenDto>> EvaluacionesDeDispositivoAsync(string idDispositivo, CancellationToken cancelar);

    /// <summary>Datos del mapa de calor de un desastre, o null si el desastre no existe.</summary>
    Task<DatosMapaCalor?> MapaCalorAsync(Guid desastreId, CancellationToken cancelar);
}
