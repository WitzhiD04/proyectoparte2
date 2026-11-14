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
