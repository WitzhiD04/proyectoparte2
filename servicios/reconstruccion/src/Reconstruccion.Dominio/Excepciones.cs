namespace Reconstruccion.Dominio;

/// <summary>Una regla de negocio no se cumple. El código identifica la regla.</summary>
public class ReglaDeNegocioException : Exception
{
    public string Codigo { get; }

    public ReglaDeNegocioException(string codigo, string mensaje) : base(mensaje)
    {
        Codigo = codigo;
    }
}

/// <summary>El actor no tiene permiso para la operación.</summary>
public class NoAutorizadoException : Exception
{
    public NoAutorizadoException(string mensaje) : base(mensaje) { }
}

/// <summary>
/// El proyecto no es de la jurisdicción de la autoridad. No es un 403: la aplicación lo responde igual que un proyecto
/// que no existe, para no revelar que existe (Cuadro 3 del SRS, E-39).
/// </summary>
public class FueraDeJurisdiccionException : Exception
{
    public FueraDeJurisdiccionException() : base("El proyecto no es de la jurisdicción de la autoridad.") { }
}
