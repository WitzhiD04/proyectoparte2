namespace ConsultaDanos.Dominio;

/// <summary>
/// Categorías de daño (SRS RN-5). Son las mismas para todos los tipos de activo, y por eso el impacto se puede consolidar
/// (RF-ED-10). Es la enumeración del dominio: la traducción desde el contrato del bus está en la infraestructura.
/// </summary>
public enum NivelDano { SIN_DANO, LEVE, MODERADO, GRAVE, DESTRUCCION_TOTAL }

/// <summary>Resultado de la validación de una evaluación (RF-ED-08).</summary>
public enum EstadoValidacion { VALIDADA, RECHAZADA, PENDIENTE }

/// <summary>Cantidad de activos por nivel de daño: la distribución que acompaña al índice (DA-08).</summary>
public sealed class Distribucion
{
    private readonly int[] _cantidades = new int[5];

    public static IReadOnlyList<NivelDano> Niveles { get; } = Enum.GetValues<NivelDano>();

    public int this[NivelDano nivel] => _cantidades[(int)nivel];

    public int Total => _cantidades.Sum();

    public Distribucion Sumar(NivelDano nivel, int cantidad)
    {
        _cantidades[(int)nivel] += cantidad;
        return this;
    }

    public Distribucion Sumar(Distribucion otra)
    {
        foreach (NivelDano n in Niveles)
        {
            _cantidades[(int)n] += otra[n];
        }
        return this;
    }

    /// <summary>Índice de daño de este conjunto de activos (ver <see cref="IndiceDano"/>); nulo si no hay activos.</summary>
    public double? Indice => IndiceDano.Calcular(this);

    /// <summary>Activos en nivel 4 o 5 (grave o destrucción total), que son los que cuentan en la priorización.</summary>
    public int Graves => this[NivelDano.GRAVE] + this[NivelDano.DESTRUCCION_TOTAL];
}

/// <summary>
/// Índice de daño de una zona (DA-08, confirmado en P-05): el promedio de los pesos del mapa de calor del prototipo
/// (0, 0,25, 0,5, 0,75 y 1), ponderado por la cantidad de activos en cada nivel. Va de 0 (sin daño) a 1 (todo destruido).
/// </summary>
public static class IndiceDano
{
    public static double Peso(NivelDano nivel) => nivel switch
    {
        NivelDano.SIN_DANO => 0,
        NivelDano.LEVE => 0.25,
        NivelDano.MODERADO => 0.5,
        NivelDano.GRAVE => 0.75,
        NivelDano.DESTRUCCION_TOTAL => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(nivel)),
    };

    public static double? Calcular(Distribucion distribucion)
    {
        int total = distribucion.Total;
        if (total == 0)
        {
            return null;
        }
        double suma = Distribucion.Niveles.Sum(n => Peso(n) * distribucion[n]);
        return Math.Round(suma / total, 4);
    }
}
