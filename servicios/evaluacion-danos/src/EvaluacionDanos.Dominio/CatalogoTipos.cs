namespace EvaluacionDanos.Dominio;

/// <summary>
/// Reglas del catálogo de tipos de activo (RF-ED-04, RF-ED-05, AD-28): las cinco categorías de daño son fijas y
/// cada tipo declara un criterio por categoría. El catálogo cambia en ejecución, sin redespliegue (QA-10).
/// </summary>
public static class CatalogoTipos
{
    public const int LargoNombre = 100;
    public const int LargoCriterio = 300;

    /// <summary>
    /// El código va en minúsculas (como los del catálogo inicial y los que guarda la app). Su forma canónica en los
    /// eventos es la misma en mayúsculas, así la correspondencia es de uno a uno.
    /// </summary>
    public static bool CodigoValido(string? codigo)
    {
        if (string.IsNullOrEmpty(codigo) || codigo.Length > Limites.CodigoTipo || codigo[0] is < 'a' or > 'z')
        {
            return false;
        }
        foreach (char c in codigo)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
            {
                return false;
            }
        }
        return true;
    }

    public static string? ValidarNombre(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return "Falta el nombre del tipo de activo";
        }
        return nombre.Trim().Length > LargoNombre ? $"El nombre supera los {LargoNombre} caracteres" : null;
    }

    /// <summary>Exactamente un criterio no vacío por cada nivel de daño.</summary>
    public static string? ValidarCriterios(IReadOnlyCollection<(string? Nivel, string? Criterio)>? criterios)
    {
        NivelDano[] niveles = Enum.GetValues<NivelDano>();
        if (criterios is null || criterios.Count != niveles.Length)
        {
            return $"Se necesita un criterio por cada uno de los {niveles.Length} niveles de daño";
        }
        var vistos = new HashSet<string>();
        foreach ((string? nivel, string? criterio) in criterios)
        {
            if (nivel is null || !ValidadorEvaluacion.EsNombreDeEnum<NivelDano>(nivel))
            {
                return $"El nivel de daño '{nivel}' no es válido";
            }
            if (!vistos.Add(nivel))
            {
                return $"El nivel de daño '{nivel}' está repetido";
            }
            if (string.IsNullOrWhiteSpace(criterio))
            {
                return $"Falta el criterio del nivel {nivel}";
            }
            if (criterio.Trim().Length > LargoCriterio)
            {
                return $"El criterio del nivel {nivel} supera los {LargoCriterio} caracteres";
            }
        }
        return null;
    }
}
