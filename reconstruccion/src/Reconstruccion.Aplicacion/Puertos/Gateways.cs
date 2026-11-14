using Reconstruccion.Dominio;

namespace Reconstruccion.Aplicacion.Puertos;

public interface IRepositorioProyectos
{
    Task<ProyectoReconstruccion?> BuscarAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ProyectoReconstruccion>> DeDesastreAsync(Guid idDesastre, CancellationToken ct = default);
    /// <summary>Evaluaciones del desastre vinculadas a un proyecto que no está cancelado.</summary>
    Task<IReadOnlySet<Guid>> EvaluacionesVinculadasAsync(Guid idDesastre, CancellationToken ct = default);
    /// <summary>Guarda un proyecto nuevo o los cambios de uno existente, con sus avances, costos y documentos nuevos.</summary>
    Task GuardarAsync(ProyectoReconstruccion proyecto, CancellationToken ct = default);
    Task<LoteCreacion?> BuscarLoteAsync(string claveIdempotencia, CancellationToken ct = default);
    Task GuardarLoteAsync(LoteCreacion lote, CancellationToken ct = default);
}

/// <summary>Historial de solo inserción: no ofrece modificar ni borrar.</summary>
public interface IHistorial
{
    Task AgregarAsync(IEnumerable<EntradaHistorial> entradas, CancellationToken ct = default);
    Task<IReadOnlyList<EntradaHistorial>> DeProyectoAsync(Guid idProyecto, DateTimeOffset? hasta = null, CancellationToken ct = default);
}

/// <summary>Copias locales de datos que pertenecen a otros módulos.</summary>
public interface ICopiasLocales
{
    Task<IReadOnlyList<DesastreLocal>> DesastresAsync(CancellationToken ct = default);
    Task<DesastreLocal?> DesastreAsync(Guid idDesastre, CancellationToken ct = default);
    Task<IReadOnlyList<EvaluacionValidada>> EvaluacionesDeDesastreAsync(Guid idDesastre, CancellationToken ct = default);
}

/// <summary>Evento por publicar. Se guarda en la misma transacción del cambio.</summary>
public sealed record MensajeSalida(Guid Id, string Tipo, string ClaveIdempotencia, string Contenido, DateTimeOffset Instante);

public interface IBandejaSalida
{
    Task AgregarAsync(IEnumerable<MensajeSalida> mensajes, CancellationToken ct = default);
}

public interface IAlmacenDocumentos
{
    /// <summary>Guarda el archivo y devuelve su ubicación.</summary>
    Task<string> GuardarAsync(string nombreObjeto, Stream contenido, long tamano, string tipoContenido, CancellationToken ct = default);
}

/// <summary>Modelo de lectura del programa, alimentado por los eventos de los proyectos.</summary>
public interface IProyeccionPrograma
{
    /// <summary>Aplica la fila si su versión es más nueva que la guardada. Repetir un evento no cambia nada.</summary>
    Task AplicarAsync(FilaPrograma fila, CancellationToken ct = default);
    Task<IReadOnlyList<FilaPrograma>> DeDesastreAsync(Guid idDesastre, CancellationToken ct = default);
}

/// <summary>Confirma en una sola transacción todo lo que los gateways tienen pendiente.</summary>
public interface IUnidadDeTrabajo
{
    Task ConfirmarAsync(CancellationToken ct = default);
}

public interface IReloj
{
    DateTimeOffset Ahora { get; }
}
