namespace ConsultaDanos.Dominio;

/// <summary>Una zona de la evolución (RF-ED-15, E-40): un municipio o un departamento.</summary>
public enum TipoZona { MUNICIPIO, DEPARTAMENTO }

/// <summary>Código DIVIPOLA de una zona: 5 dígitos el municipio, 2 el departamento.</summary>
public static class CodigoZona
{
    /// <summary>El tipo de zona que indica el código; nulo si no es un código DIVIPOLA.</summary>
    public static TipoZona? TipoDe(string? codigo)
    {
        if (codigo is null || !codigo.All(char.IsAsciiDigit))
        {
            return null;
        }
        return codigo.Length switch
        {
            5 => TipoZona.MUNICIPIO,
            2 => TipoZona.DEPARTAMENTO,
            _ => null,
        };
    }
}

/// <summary>
/// Nombres de los 33 departamentos, tal como están en <c>datos/geo/municipios-colombia.geojson</c>. EV-21 trae el código
/// del departamento de cada municipio pero no su nombre, y <c>p_municipios</c> tampoco lo guarda (E-40).
/// </summary>
public static class Departamentos
{
    private static readonly Dictionary<string, string> Nombres = new(StringComparer.Ordinal)
    {
        ["05"] = "ANTIOQUIA",
        ["08"] = "ATLÁNTICO",
        ["11"] = "BOGOTÁ, D.C.",
        ["13"] = "BOLÍVAR",
        ["15"] = "BOYACÁ",
        ["17"] = "CALDAS",
        ["18"] = "CAQUETÁ",
        ["19"] = "CAUCA",
        ["20"] = "CESAR",
        ["23"] = "CÓRDOBA",
        ["25"] = "CUNDINAMARCA",
        ["27"] = "CHOCÓ",
        ["41"] = "HUILA",
        ["44"] = "LA GUAJIRA",
        ["47"] = "MAGDALENA",
        ["50"] = "META",
        ["52"] = "NARIÑO",
        ["54"] = "NORTE DE SANTANDER",
        ["63"] = "QUINDÍO",
        ["66"] = "RISARALDA",
        ["68"] = "SANTANDER",
        ["70"] = "SUCRE",
        ["73"] = "TOLIMA",
        ["76"] = "VALLE DEL CAUCA",
        ["81"] = "ARAUCA",
        ["85"] = "CASANARE",
        ["86"] = "PUTUMAYO",
        ["88"] = "ARCHIPIÉLAGO DE SAN ANDRÉS, PROVIDENCIA Y SANTA CATALINA",
        ["91"] = "AMAZONAS",
        ["94"] = "GUAINÍA",
        ["95"] = "GUAVIARE",
        ["97"] = "VAUPÉS",
        ["99"] = "VICHADA",
    };

    public static string? NombreDe(string codigo) => Nombres.GetValueOrDefault(codigo);
}
