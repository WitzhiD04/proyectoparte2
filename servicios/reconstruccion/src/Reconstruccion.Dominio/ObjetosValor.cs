using System.Globalization;

namespace Reconstruccion.Dominio;

/// <summary>Monto en pesos colombianos. Nunca es negativo.</summary>
public readonly record struct Dinero
{
    public decimal Monto { get; }

    public Dinero(decimal monto)
    {
        if (monto < 0)
        {
            throw new ReglaDeNegocioException("MONTO_NEGATIVO", "Un monto no puede ser negativo.");
        }
        Monto = monto;
    }

    public static Dinero Cero => new Dinero(0);

    public static Dinero operator +(Dinero a, Dinero b) => new Dinero(a.Monto + b.Monto);

    public override string ToString() => Monto.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>Fechas de inicio y fin de un proyecto. El fin es posterior al inicio.</summary>
public sealed record Cronograma
{
    public DateOnly Inicio { get; }
    public DateOnly Fin { get; }

    public Cronograma(DateOnly inicio, DateOnly fin)
    {
        if (fin <= inicio)
        {
            throw new ReglaDeNegocioException("CRONOGRAMA_INVALIDO", "La fecha de fin debe ser posterior a la de inicio.");
        }
        Inicio = inicio;
        Fin = fin;
    }

    public override string ToString() => $"{Inicio:yyyy-MM-dd}/{Fin:yyyy-MM-dd}";

    public static Cronograma DesdeTexto(string texto)
    {
        string[] partes = texto.Split('/');
        return new Cronograma(
            DateOnly.ParseExact(partes[0], "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateOnly.ParseExact(partes[1], "yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}

/// <summary>Evaluación de daño que originó un proyecto. El vínculo nunca se elimina:
/// al cancelar el proyecto deja de estar vigente y la evaluación queda libre.</summary>
public sealed class EvaluacionOrigen
{
    public Guid IdEvaluacion { get; }
    public Guid IdActivo { get; }
    public string TipoActivo { get; }
    public NivelDano NivelDano { get; }
    /// <summary>Municipio del activo evaluado (DIVIPOLA). Define el alcance territorial y la jurisdicción.</summary>
    public string CodigoMunicipio { get; }
    public bool Vigente { get; private set; }

    public EvaluacionOrigen(Guid idEvaluacion, Guid idActivo, string tipoActivo, NivelDano nivelDano, string codigoMunicipio,
        bool vigente = true)
    {
        IdEvaluacion = idEvaluacion;
        IdActivo = idActivo;
        TipoActivo = tipoActivo;
        NivelDano = nivelDano;
        CodigoMunicipio = codigoMunicipio;
        Vigente = vigente;
    }

    internal void Liberar() => Vigente = false;
}
