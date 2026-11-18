using ConsultaDanos.Dominio;

namespace ConsultaDanos.Aplicacion;

// ------------------------------------------------------------------ lo que se consulta

/// <summary>Filtros del mapa (DA-08). <see cref="Clave"/> identifica la respuesta armada en la caché.</summary>
public sealed record FiltroMapa(Guid Desastre, NivelDano NivelMinimo, bool SoloValidadas, bool Puntos)
{
    public IReadOnlyList<NivelDano> Niveles => Distribucion.Niveles.Where(n => n >= NivelMinimo).ToList();

    public string Clave => $"n{(int)NivelMinimo}.s{(SoloValidadas ? 1 : 0)}.p{(Puntos ? 1 : 0)}";
}

/// <summary>Una zona de la coropleta: el municipio con la distribución de los activos que pasan el filtro.</summary>
public sealed record ZonaMapa(string Codigo, string? Nombre, string? CodigoDepartamento, int VersionCapa, DateTimeOffset? CapaAl,
    Distribucion Distribucion, int ConConflicto);

/// <summary>Un activo como punto del mapa (RF-ED-11, AD-32): nivel, si está validada y si tiene un conflicto pendiente.</summary>
public sealed record PuntoMapa(Guid IdActivo, Guid IdEvaluacion, string TipoActivo, NivelDano Nivel, double Lat, double Lon,
    EstadoValidacion? Validacion, bool ConflictoPendiente, string CodigoMunicipio, DateTimeOffset DatoAl);

public sealed record IntegracionLeida(string Sistema, string Estado, DateTimeOffset Desde, long MensajesEnCola);

public sealed record ResumenTipo(string TipoActivo, string? Nombre, bool Critico, Distribucion Distribucion);

/// <summary>El consolidado de RF-ED-10: impacto por zona y por tipo, y las zonas en el orden de la priorización.</summary>
public sealed record Consolidado(Guid IdDesastre, DateTimeOffset? DatoAl, Distribucion Total, IReadOnlyList<ZonaPriorizada> Zonas,
    IReadOnlyList<ResumenTipo> PorTipo, IReadOnlySet<string> TiposCriticos, IReadOnlyList<IntegracionLeida> Integraciones);

/// <summary>El estado vigente de un activo y la secuencia de sus evaluaciones (RF-ED-15).</summary>
public sealed record EvolucionActivo(Guid IdActivo, EstadoActivo Vigente, bool ConflictoPendiente, IReadOnlyList<EntradaHistorial> Entradas,
    DateTimeOffset? DatoAl);

/// <summary>La zona de una evolución (RF-ED-15, E-40): un municipio o un departamento de la capa EV-21.</summary>
public sealed record ZonaConsultada(string Codigo, TipoZona Tipo, string? Nombre);

/// <summary>Cuántos activos quedaron en cada nivel de daño en un día (UTC), para dibujar la curva de la zona.</summary>
public sealed record ConteoDia(DateOnly Fecha, Distribucion Distribucion);

/// <summary>La secuencia de evaluaciones de una zona, ordenada por instante, y su agregado por día (RF-ED-15, E-40).</summary>
public sealed record EvolucionZona(ZonaConsultada Zona, DateTimeOffset? DatoAl, IReadOnlyList<EntradaHistorial> Entradas,
    IReadOnlyList<ConteoDia> PorDia);

public sealed record BandejaLeida(int Pendientes, DateTimeOffset? MasViejo, DateTimeOffset? DatoAl);

/// <summary>Una respuesta ya serializada, y si es la última conocida porque la base no respondió (CON-6).</summary>
public sealed record RespuestaLectura(string Contenido, bool Degradado);

/// <summary>Ni la base ni la caché tienen con qué responder: la ruta responde 503.</summary>
public sealed class DatosNoDisponiblesException(string mensaje, Exception? causa = null) : Exception(mensaje, causa);

/// <summary>Parámetros de despliegue de las consultas (se cambian sin tocar el código).</summary>
public sealed record ParametrosConsulta(IReadOnlySet<string> TiposCriticos, UmbralesRevision Umbrales, TimeSpan VigenciaRespuesta)
{
    public static ParametrosConsulta PorOmision { get; } =
        new(Priorizacion.TiposCriticosPorOmision, UmbralesRevision.PorOmision, TimeSpan.FromSeconds(60));
}

// ------------------------------------------------------------------ puertos de salida

/// <summary>
/// Almacén de las proyecciones (DA-08). Se usa dentro de la transacción del consumidor: cada activo se bloquea antes de
/// leerlo, así dos réplicas que aplican eventos del mismo activo se esperan y ninguna pisa a la otra.
/// </summary>
public interface IAlmacenProyecciones
{
    /// <summary>Lee el activo bloqueándolo hasta el fin de la transacción. Si no existe, lo crea vacío.</summary>
    Task<EstadoActivo> BloquearActivoAsync(Guid idActivo, DateTimeOffset instante, CancellationToken ct);

    /// <summary>
    /// Guarda el estado nuevo de los activos que cambiaron (ya bloqueados), aplica los cambios del consolidado en el orden
    /// de las claves y deja la marca de tiempo de los desastres afectados. Todo en una llamada por evento, para tomar los
    /// bloqueos siempre en el mismo orden: activos, consolidado, desastres.
    /// </summary>
    Task GuardarActivosAsync(IReadOnlyList<EstadoActivo> cambiados, IReadOnlyList<(ClaveZona Clave, int Delta)> deltas,
        IReadOnlyCollection<Guid> desastres, DateTimeOffset instante, CancellationToken ct);

    /// <summary>La validación vigente de una evaluación (la de mayor versión que haya llegado).</summary>
    Task<EstadoValidacion?> ValidacionDeAsync(Guid idEvaluacion, CancellationToken ct);

    /// <summary>Guarda la validación si su versión es mayor que la guardada.</summary>
    Task GuardarValidacionAsync(ValidacionCambiada h, CancellationToken ct);

    /// <summary>Agrega una entrada al historial. Una entrada repetida no se duplica.</summary>
    Task RegistrarHistorialAsync(EntradaHistorial entrada, CancellationToken ct);

    /// <summary>Serializa los cambios de la bandeja entre réplicas, para que su tamaño y su antigüedad sean exactos.</summary>
    Task BloquearBandejaAsync(CancellationToken ct);

    Task<ConflictoLeido> GuardarConflictoDetectadoAsync(ConflictoDetectado h, CancellationToken ct);

    Task<ConflictoLeido> GuardarConflictoResueltoAsync(ConflictoResuelto h, CancellationToken ct);

    /// <summary>Recalcula <c>p_bandeja</c>: los conflictos pendientes y el más viejo (R-08).</summary>
    Task RecalcularBandejaAsync(DateTimeOffset instante, CancellationToken ct);

    Task<Guid?> DesastreDelActivoAsync(Guid idActivo, CancellationToken ct);

    /// <summary>Deja la marca de tiempo de un desastre cuyo mapa cambió sin cambiar sus activos (la marca de conflicto).</summary>
    Task MarcarDesastreAsync(Guid desastre, DateTimeOffset instante, CancellationToken ct);

    Task GuardarCapaAsync(CapaRecibida h, CancellationToken ct);

    Task GuardarTipoActivoAsync(TipoActivoActualizado h, CancellationToken ct);

    Task GuardarIntegracionAsync(EstadoIntegracion h, CancellationToken ct);
}

/// <summary>
/// Invalida las respuestas armadas de la caché. Las invalidaciones se programan durante el evento y se aplican cuando
/// la transacción se confirma: si se aplicaran antes, una lectura concurrente podría guardar el mapa viejo con la
/// versión nueva.
/// </summary>
public interface IInvalidacionMapa
{
    void Programar(IReadOnlyCollection<Guid> desastres);

    /// <summary>Un cambio que afecta a todos los desastres: la capa de municipios, los tipos de activo o las integraciones.</summary>
    void ProgramarGlobal();
}

/// <summary>Lecturas de las proyecciones, sin cálculo pesado: lo agregado ya está proyectado o se agrega por índice.</summary>
public interface ILecturasDanos
{
    Task<IReadOnlyList<ZonaMapa>> ZonasMapaAsync(FiltroMapa filtro, CancellationToken ct);
    Task<IReadOnlyList<PuntoMapa>> PuntosAsync(FiltroMapa filtro, CancellationToken ct);

    /// <summary>La geometría simplificada (GeoJSON) de cada municipio pedido que esté en la capa.</summary>
    Task<IReadOnlyDictionary<string, string>> GeometriasAsync(IReadOnlyCollection<string> codigos, CancellationToken ct);

    /// <summary>El instante del último evento aplicado que cambió los datos del desastre.</summary>
    Task<DateTimeOffset?> DatoAlAsync(Guid desastre, CancellationToken ct);

    Task<IReadOnlyList<ZonaConsolidada>> ConsolidadoAsync(Guid desastre, CancellationToken ct);
    Task<IReadOnlyList<IntegracionLeida>> IntegracionesAsync(CancellationToken ct);
    Task<EvolucionActivo?> EvolucionAsync(Guid idActivo, CancellationToken ct);

    /// <summary>El nombre del municipio en la capa EV-21; nulo si la capa no lo tiene.</summary>
    Task<string?> NombreMunicipioAsync(string codigo, CancellationToken ct);

    /// <summary>Si la capa EV-21 tiene algún municipio de ese departamento.</summary>
    Task<bool> DepartamentoEnCapaAsync(string codigo, CancellationToken ct);

    /// <summary>Las entradas de <c>p_historial_activo</c> de la zona, ordenadas por instante.</summary>
    Task<IReadOnlyList<EntradaHistorial>> HistorialZonaAsync(string codigo, TipoZona tipo, Guid? desastre, CancellationToken ct);

    Task<BandejaLeida> BandejaAsync(CancellationToken ct);
}

/// <summary>
/// Caché de geometrías y de respuestas (AD-17, E-21). El adaptador antepone el espacio de claves del servicio. Las
/// respuestas llevan en la clave la versión global y la del desastre: subirlas invalida sin borrar nada.
/// </summary>
public interface ICacheDanos
{
    Task<string?> LeerAsync(string clave, CancellationToken ct);
    Task<IReadOnlyList<string?>> LeerVariasAsync(IReadOnlyList<string> claves, CancellationToken ct);
    Task GuardarAsync(string clave, string valor, TimeSpan? vigencia, CancellationToken ct);
    Task<(long Global, long Desastre)> VersionesAsync(Guid desastre, CancellationToken ct);
    Task InvalidarAsync(IReadOnlyCollection<Guid> desastres, bool global, CancellationToken ct);
}

/// <summary>Serializa las respuestas. Es un adaptador: el formato (GeoJSON, JSON) no es asunto de la aplicación.</summary>
public interface IFormatoDanos
{
    string Mapa(FiltroMapa filtro, IReadOnlyList<ZonaMapa> zonas, IReadOnlyDictionary<string, string> geometrias,
        IReadOnlyList<PuntoMapa> puntos, DateTimeOffset? datoAl);

    string Consolidado(Consolidado consolidado);

    /// <summary>La misma respuesta con <c>degradado: true</c>: es la última conocida y su <c>datoAl</c> dice de cuándo es.</summary>
    string MarcarDegradado(string respuesta);
}
