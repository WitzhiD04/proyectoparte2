using System.Text.Json;

namespace Plataforma.Comun.Eventos.Datos;

/// <summary>Punto geográfico en grados decimales (WGS 84).</summary>
public sealed record Ubicacion(double Lat, double Lon);

/// <summary>Fechas de inicio y fin de un proyecto. El fin es posterior al inicio (Diseño RC).</summary>
public sealed record Cronograma(DateOnly Inicio, DateOnly Fin);

/// <summary>Una división administrativa con su geometría GeoJSON (Polygon o MultiPolygon).</summary>
/// <param name="Codigo">Código DIVIPOLA del DANE (5 dígitos el municipio, 2 el departamento).</param>
/// <param name="CodigoPadre">El departamento de un municipio; nulo para un departamento.</param>
public sealed record Division(string Codigo, string Nombre, string? CodigoPadre, JsonElement Geometria);
