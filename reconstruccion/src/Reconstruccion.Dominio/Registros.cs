namespace Reconstruccion.Dominio;

/// <summary>Entrada del historial. No se modifica ni se borra: un error se corrige con otra entrada.</summary>
public sealed record EntradaHistorial(
    Guid Id,
    Guid IdProyecto,
    int Version,
    TipoCambio TipoCambio,
    string? ValorAnterior,
    string? ValorNuevo,
    string Autor,
    string Justificacion,
    DateTimeOffset Instante,
    string Procedencia);

public sealed record RegistroAvance(Guid Id, Guid IdProyecto, decimal Porcentaje, string Autor, DateTimeOffset Instante, string Procedencia);

public sealed record RegistroCosto(Guid Id, Guid IdProyecto, Dinero Monto, string Concepto, string Autor, DateTimeOffset Instante);

public sealed record Documento(Guid Id, Guid IdProyecto, string Nombre, string Tipo, string Ubicacion, string Hash, string Autor, DateTimeOffset Instante);

/// <summary>Lote enviado por la autoridad. La clave permite reconocer un reenvío.</summary>
public sealed record LoteCreacion(string ClaveIdempotencia, Guid IdDesastre, string Autor, DateTimeOffset Instante, IReadOnlyList<Guid> IdsProyectos);

/// <summary>Copia local del estado de un desastre (dato de otro módulo).</summary>
public sealed record DesastreLocal(Guid Id, string Nombre, string Estado);

/// <summary>Copia local de una evaluación de daño (dato de otro módulo).</summary>
public sealed record EvaluacionValidada(
    Guid Id,
    Guid IdDesastre,
    Guid IdActivo,
    string NombreActivo,
    string TipoActivo,
    NivelDano NivelDano,
    string Municipio,
    string Estado);
