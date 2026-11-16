namespace Auditoria.Dominio;

/// <summary>Una regla del dominio no se cumple. El código viaja hasta la respuesta para que el cliente lo distinga.</summary>
public class ReglaDeNegocioException : Exception
{
    public ReglaDeNegocioException(string codigo, string mensaje) : base(mensaje)
    {
        Codigo = codigo;
    }

    public string Codigo { get; }
}

/// <summary>Quien pide la operación no tiene permiso para hacerla (autorización de nivel 2, AD-22).</summary>
public class NoAutorizadoException : Exception
{
    public NoAutorizadoException(string mensaje) : base(mensaje) { }
}
