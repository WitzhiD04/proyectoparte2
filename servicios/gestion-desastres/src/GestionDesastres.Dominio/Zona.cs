namespace GestionDesastres.Dominio;

/// <summary>
/// Una versión de la delimitación geográfica (SRS RN-4, Cuadro 19). La delimitación nueva sucede a la anterior y no la
/// sobrescribe: el historial de versiones es el dato principal y permite reconstruir el área en cualquier instante pasado
/// (RF-GD-09, FP-03).
/// </summary>
/// <param name="GeometriaGeoJson">Polígono o multipolígono GeoJSON (WGS 84), o nulo si la zona son solo municipios. El
/// dominio lo trata como dato; su validez geométrica la verifica el adaptador de PostGIS.</param>
/// <param name="CodigosMunicipio">Municipios DIVIPOLA que forman la zona o que interseca el polígono (DA-07).</param>
public sealed record VersionZona(
    int Version,
    DateTimeOffset VigenteDesde,
    string? GeometriaGeoJson,
    IReadOnlyList<string> CodigosMunicipio,
    FuenteDelimitacion Fuente,
    string Autor,
    string Justificacion)
{
    public static void Validar(string? geometria, IReadOnlyList<string> municipios)
    {
        if (string.IsNullOrWhiteSpace(geometria) && municipios.Count == 0)
        {
            throw new ReglaDeNegocioException("ZONA_VACIA", "La delimitación necesita un polígono o al menos un municipio (RF-GD-08).");
        }
        foreach (string codigo in municipios)
        {
            if (codigo.Length != 5 || !codigo.All(char.IsAsciiDigit))
            {
                throw new ReglaDeNegocioException("MUNICIPIO_INVALIDO", $"'{codigo}' no es un código DIVIPOLA de municipio (5 dígitos).");
            }
        }
    }
}
