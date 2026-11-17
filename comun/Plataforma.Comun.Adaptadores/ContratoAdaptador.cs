using Plataforma.Comun.Eventos;

namespace Plataforma.Comun.Adaptadores;

/// <summary>
/// Operaciones de entrada de un adaptador (DA-11): recuperar los cambios del sistema externo desde una marca de avance
/// (AD-14) y traducir cada uno al evento canónico (AD-09). El resto de la plataforma solo ve el evento.
/// </summary>
/// <typeparam name="TExterno">Un cambio en el formato del sistema externo.</typeparam>
public interface IEntradaAdaptador<TExterno>
{
    /// <summary>Sistema externo, como lo nombra el SAD ("SE-7").</summary>
    string SistemaExterno { get; }

    /// <summary>
    /// Cambios posteriores a <paramref name="marcaAvance"/> (nula: desde el principio). La consulta es idempotente: la
    /// misma marca devuelve los mismos cambios, de modo que un reinicio a mitad solo repite lo que no se confirmó.
    /// </summary>
    Task<LoteExterno<TExterno>> RecuperarDesde(string? marcaAvance, CancellationToken ct);

    /// <summary>El evento canónico de un cambio externo, o nulo si el cambio no le interesa a la plataforma.</summary>
    Sobre? Traducir(TExterno externo);
}

/// <summary>Operación de salida de un adaptador (DA-11): entregar un evento canónico al sistema externo.</summary>
public interface ISalidaAdaptador
{
    string SistemaExterno { get; }

    /// <summary>
    /// Entrega el evento, traducido al formato del sistema externo. Debe ser idempotente (Cuadro 14: clave del proyecto y
    /// número de versión), porque un mensaje se puede entregar más de una vez. Lanza
    /// <see cref="SistemaExternoNoDisponibleException"/> (o una excepción de la resiliencia) si el sistema no responde, y
    /// <see cref="EntregaRechazadaException"/> si el sistema rechaza el contenido.
    /// </summary>
    Task<ResultadoEntrega> Entregar(Sobre eventoCanonico, CancellationToken ct);
}

/// <summary>Contrato completo de un adaptador bidireccional (DA-11). El bus solo usa esta interfaz.</summary>
public interface IContratoAdaptador<TExterno> : IEntradaAdaptador<TExterno>, ISalidaAdaptador
{
    new string SistemaExterno { get; }
}

/// <summary>Una página de cambios del sistema externo y la marca que la cierra.</summary>
/// <param name="Cambios">Cambios en el orden del sistema externo.</param>
/// <param name="MarcaSiguiente">Marca del último cambio de la página; nula si no hay cambios.</param>
/// <param name="HayMas">El sistema externo tiene más cambios después de esta página.</param>
public sealed record LoteExterno<TExterno>(IReadOnlyList<TExterno> Cambios, string? MarcaSiguiente, bool HayMas);

public enum ResultadoEntrega
{
    /// <summary>El sistema externo registró el evento.</summary>
    Entregado,
    /// <summary>El sistema externo ya lo tenía (misma clave de idempotencia): no hay efecto nuevo.</summary>
    Repetido,
    /// <summary>El evento no se envía a este sistema (tipo que no le corresponde, o un eco de lo que él mismo reportó).</summary>
    Omitido,
}

/// <summary>El sistema externo no respondió o respondió con un error transitorio (5xx, 408, 429).</summary>
public sealed class SistemaExternoNoDisponibleException : Exception
{
    public SistemaExternoNoDisponibleException(string mensaje) : base(mensaje) { }
    public SistemaExternoNoDisponibleException(string mensaje, Exception interna) : base(mensaje, interna) { }
}

/// <summary>El sistema externo rechazó el contenido (4xx). Es permanente: reintentarlo no lo arregla.</summary>
public sealed class EntregaRechazadaException : Exception
{
    public EntregaRechazadaException(string mensaje) : base(mensaje) { }
}
