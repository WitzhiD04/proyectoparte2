using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio;

namespace Reconstruccion.Aplicacion.CasosDeUso;

// Lo que llega de otros módulos, ya traducido del contrato por el adaptador de entrada (DA-01, E-01). La aplicación no
// conoce el formato del bus.

/// <summary>EV-03: un desastre registrado en Gestión de Desastres.</summary>
public sealed record DesastreRecibido(Guid Id, string Nombre, string Tipo, string Estado, int Version, DateTimeOffset Instante);

/// <summary>EV-06: el desastre cambió de estado.</summary>
public sealed record EstadoDesastreRecibido(Guid Id, string Estado, int Version, DateTimeOffset Instante);

/// <summary>EV-08: una evaluación cambió de validación (validada, rechazada o devuelta a pendiente).</summary>
public sealed record EvaluacionRecibida(Guid Id, Guid IdDesastre, Guid IdActivo, string ReferenciaActivo, string TipoActivo,
    NivelDano NivelDano, string CodigoMunicipio, string Estado, int Version, DateTimeOffset Instante);

/// <summary>EV-21: nombres de los municipios de la capa de SE-2 (Reconstrucción solo guarda el nombre).</summary>
public sealed record MunicipioRecibido(string Codigo, string Nombre, string? CodigoDepartamento);

/// <summary>EV-24: avance o contratista reportado por el sistema de proyectos externo (E-04).</summary>
public sealed record ReporteExternoRecibido(Guid IdProyecto, string Sistema, decimal? Avance, string? Contratista, DateTimeOffset Instante);

/// <summary>
/// Escritura de las copias locales. Cada aplicación solo acepta una versión más nueva de lo que cambia: los eventos
/// pueden llegar repetidos o en otro orden (dos réplicas consumen la misma cola), y la copia queda igual.
/// </summary>
public interface IEscrituraCopiasLocales
{
    Task AplicarAsync(DesastreRecibido desastre, CancellationToken ct = default);
    Task AplicarAsync(EstadoDesastreRecibido estado, CancellationToken ct = default);
    Task AplicarAsync(EvaluacionRecibida evaluacion, CancellationToken ct = default);
    Task AplicarAsync(IReadOnlyList<MunicipioRecibido> municipios, int versionCapa, CancellationToken ct = default);
}

/// <summary>Actualiza las copias locales con lo que publican Gestión de Desastres, Evaluación de Daños y SE-2.</summary>
public sealed class ActualizarCopiasLocales
{
    private readonly IEscrituraCopiasLocales _copias;
    public ActualizarCopiasLocales(IEscrituraCopiasLocales copias) { _copias = copias; }

    public Task AplicarAsync(DesastreRecibido d, CancellationToken ct) => _copias.AplicarAsync(d, ct);
    public Task AplicarAsync(EstadoDesastreRecibido e, CancellationToken ct) => _copias.AplicarAsync(e, ct);
    public Task AplicarAsync(EvaluacionRecibida e, CancellationToken ct) => _copias.AplicarAsync(e, ct);
    public Task AplicarAsync(IReadOnlyList<MunicipioRecibido> m, int version, CancellationToken ct) => _copias.AplicarAsync(m, version, ct);
}

public enum ResultadoReporteExterno { Aplicado, SinProyecto, NoAplica }

/// <summary>
/// Registra en el proyecto el avance o el contratista que reporta SE-7 (E-04, AD-14). El cambio sigue el camino normal:
/// historial, bandeja de salida y EV-20, con la procedencia del sistema externo. Un reporte que el proyecto no admite
/// (por ejemplo, avance de una obra que aún no está en ejecución) no se reintenta: se devuelve como "no aplica" para que
/// quede registrado en el log sin bloquear la cola.
/// </summary>
public sealed class RegistrarReporteExterno
{
    private readonly IRepositorioProyectos _proyectos;
    private readonly GuardadoDeProyectos _guardado;

    public RegistrarReporteExterno(IRepositorioProyectos proyectos, GuardadoDeProyectos guardado)
    {
        _proyectos = proyectos;
        _guardado = guardado;
    }

    public async Task<(ResultadoReporteExterno Resultado, string? Motivo)> EjecutarAsync(ReporteExternoRecibido reporte, CancellationToken ct)
    {
        ProyectoReconstruccion? proyecto = await _proyectos.BuscarAsync(reporte.IdProyecto, ct);
        if (proyecto is null)
        {
            return (ResultadoReporteExterno.SinProyecto, $"No existe el proyecto {reporte.IdProyecto}.");
        }
        try
        {
            if (reporte.Contratista is not null)
            {
                proyecto.RegistrarContratistaExterno(reporte.Sistema, reporte.Contratista, reporte.Instante);
            }
            if (reporte.Avance is decimal avance)
            {
                proyecto.RegistrarAvanceExterno(reporte.Sistema, avance, reporte.Instante);
            }
        }
        catch (ReglaDeNegocioException e)
        {
            return (ResultadoReporteExterno.NoAplica, $"{e.Codigo}: {e.Message}");
        }
        if (proyecto.EntradasNuevas.Count > 0)
        {
            await _guardado.GuardarAsync(proyecto, ct);
        }
        return (ResultadoReporteExterno.Aplicado, null);
    }
}
