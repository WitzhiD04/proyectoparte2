namespace Consulta.Dominio;

/// <summary>Un cambio en la evolución de un desastre (RF-GD-17): nivel, estado, delimitación u organismos.</summary>
public sealed record CambioEvolucion(Guid IdDesastre, DateTimeOffset Instante, string Tipo, string? Anterior, string? Nuevo, string Autor, int Version);

/// <summary>Una versión de la zona en el modelo de lectura, con su geometría simplificada para el mapa (AD-17).</summary>
public sealed record ZonaLeida(Guid IdDesastre, int Version, DateTimeOffset VigenteDesde, string? GeometriaGeoJson, IReadOnlyList<string> CodigosMunicipio,
    string Fuente);

/// <summary>Cómo estaba un desastre en un instante pasado, reconstruido desde su evolución (RF-GD-09, FP-03).</summary>
public sealed record EstadoEnInstante(DateTimeOffset Instante, string? Severidad, string? Estado, ZonaLeida? Zona);

/// <summary>Reglas del modelo de lectura que no dependen de la base de datos.</summary>
public static class Evolucion
{
    /// <summary>
    /// Reconstruye el nivel, el estado y la zona vigentes en un instante: en cada aspecto, el último cambio anterior o igual
    /// a ese instante. Una zona sucede a la anterior desde su "vigente desde" (RN-4).
    /// </summary>
    public static EstadoEnInstante En(DateTimeOffset instante, IEnumerable<CambioEvolucion> cambios, IEnumerable<ZonaLeida> zonas)
    {
        List<CambioEvolucion> hasta = cambios.Where(c => c.Instante <= instante).OrderBy(c => c.Instante).ThenBy(c => c.Version).ToList();
        string? severidad = hasta.LastOrDefault(c => c.Tipo is "REGISTRO" or "SEVERIDAD")?.Nuevo is { } s ? SeveridadDe(s) : null;
        string? estado = hasta.LastOrDefault(c => c.Tipo is "REGISTRO" or "ESTADO")?.Nuevo is { } e ? EstadoDe(e) : null;
        ZonaLeida? zona = zonas.Where(z => z.VigenteDesde <= instante).OrderBy(z => z.Version).LastOrDefault();
        return new EstadoEnInstante(instante, severidad, estado, zona);
    }

    // En el registro, el valor nuevo es "severidad|estado"; en un cambio de nivel o de estado, solo el valor.
    private static string SeveridadDe(string valor) => valor.Split('|')[0];
    private static string EstadoDe(string valor) => valor.Contains('|') ? valor.Split('|')[1] : valor;
}
