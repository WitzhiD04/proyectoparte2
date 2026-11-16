using GestionDesastres.Dominio;

namespace GestionDesastres.Aplicacion;

/// <summary>Puerto de entrada: lo que un controller conoce de un caso de uso.</summary>
public interface IPuertoEntrada<TSolicitud, TRespuesta>
{
    Task EjecutarAsync(TSolicitud solicitud, IPuertoSalida<TRespuesta> salida, CancellationToken ct = default);
}

/// <summary>Puerto de salida: por aquí el caso de uso entrega su resultado. Lo implementa un presenter.</summary>
public interface IPuertoSalida<TRespuesta>
{
    void Exito(TRespuesta respuesta);
    void NoEncontrado(string mensaje);
    void NoAutorizado(string mensaje);
    void ReglaIncumplida(string codigo, string mensaje);
}

public interface IRepositorioDesastres
{
    Task<Desastre?> BuscarAsync(Guid id, CancellationToken ct = default);
    /// <summary>Guarda un desastre nuevo o los cambios de uno existente, con su historial y su zona nueva, sin confirmar.</summary>
    Task GuardarAsync(Desastre desastre, CancellationToken ct = default);
    Task<IReadOnlyList<CambioDesastre>> HistorialAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Catálogo de tipos, perfiles de ciclo de vida y perfil mínimo por nivel (AD-07, RN-1, RN-2).</summary>
public interface ICatalogo
{
    Task<TipoDesastre?> TipoAsync(string codigo, CancellationToken ct = default);
    Task<IReadOnlyList<TipoDesastre>> TiposAsync(CancellationToken ct = default);
    Task GuardarTipoAsync(TipoDesastre tipo, bool nuevo, CancellationToken ct = default);
    Task<PerfilCicloVida?> PerfilAsync(string codigo, CancellationToken ct = default);
    Task<IReadOnlyList<PerfilCicloVida>> PerfilesAsync(CancellationToken ct = default);
    Task GuardarPerfilAsync(PerfilCicloVida perfil, bool nuevo, CancellationToken ct = default);
    Task<PerfilPorNivel> PerfilPorNivelAsync(CancellationToken ct = default);
    Task GuardarPerfilPorNivelAsync(PerfilPorNivel valor, CancellationToken ct = default);
}

/// <summary>
/// Copia local de los proyectos de reconstrucción de cada desastre, alimentada por EV-18 y EV-20 (E-07). Permite aplicar la
/// regla "En reconstrucción → Cerrado: todos los proyectos asociados alcanzan un estado final" (SRS Cuadro 16) sin
/// consultar a Reconstrucción de forma síncrona (AD-01).
/// </summary>
public interface IProyectosDelDesastre
{
    Task<int> NoFinalesAsync(Guid idDesastre, CancellationToken ct = default);
    /// <summary>Aplica el estado de un proyecto si su versión es más nueva que la guardada.</summary>
    Task AplicarAsync(Guid idProyecto, Guid idDesastre, bool final, int version, CancellationToken ct = default);
}

/// <summary>
/// Capa de municipios recibida de SE-2 (EV-21, DA-07). Completa una delimitación: si viene un polígono, calcula los
/// municipios que interseca; si vienen solo municipios, calcula su geometría unida, para que el mapa la dibuje (AD-17).
/// </summary>
public interface ICapaMunicipios
{
    Task<(string? Geometria, IReadOnlyList<string> Municipios)> CompletarAsync(string? geometria, IReadOnlyList<string> municipios,
        CancellationToken ct = default);

    Task AplicarAsync(IReadOnlyList<DivisionGeografica> divisiones, int versionCapa, CancellationToken ct = default);
}

public sealed record DivisionGeografica(string Codigo, string Nombre, string? CodigoPadre, string GeometriaGeoJson);

/// <summary>Puerto de salida para avisar a los demás módulos. El adaptador lo deja en la bandeja de la misma transacción (AD-45).</summary>
public interface IPublicadorHechos
{
    Task RegistrarAsync(IEnumerable<HechoDesastre> hechos, CancellationToken ct = default);
}

/// <summary>Confirma en una sola transacción todo lo pendiente: el cambio, su historial y sus eventos.</summary>
public interface IUnidadDeTrabajo
{
    Task ConfirmarAsync(CancellationToken ct = default);
}

public interface IReloj
{
    DateTimeOffset Ahora { get; }
}

public class ConflictoDeConcurrenciaException : Exception
{
    public ConflictoDeConcurrenciaException(string mensaje) : base(mensaje) { }
}
