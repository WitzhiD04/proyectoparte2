namespace Plataforma.Comun.Seguridad;

/// <summary>Roles de la plataforma (DA-15). En Keycloak son roles del realm "plataforma".</summary>
public static class RolesPlataforma
{
    public const string Autoridad = "autoridad";
    public const string ResponsableProyecto = "responsable-proyecto";
    public const string Auditor = "auditor";
    public const string Evaluador = "evaluador";
    public const string Operador = "operador";
    public const string Organismo = "organismo";
    public const string Administrador = "administrador";

    public static IReadOnlySet<string> Todos { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Autoridad, ResponsableProyecto, Auditor, Evaluador, Operador, Organismo, Administrador,
    };
}

/// <summary>Ámbito territorial de una autoridad (DA-14).</summary>
public static class AmbitosPlataforma
{
    public const string Local = "LOCAL";
    public const string Regional = "REGIONAL";
    public const string Nacional = "NACIONAL";
}

/// <summary>
/// Quién hace la petición. Con el gateway, cada campo sale de un claim del JWT: sub/preferred_username, roles del
/// realm, organizacion, ambito y territorio (DIVIPOLA del municipio o del departamento).
/// </summary>
/// <param name="Usuario">Identificador estable del usuario.</param>
/// <param name="Roles">Roles de la plataforma (ver <see cref="RolesPlataforma"/>).</param>
/// <param name="Organizacion">Organismo al que pertenece (por ejemplo "defensa-civil", "alcaldia.armenia").</param>
/// <param name="Ambito">LOCAL, REGIONAL o NACIONAL, si es una autoridad.</param>
/// <param name="Territorio">Código DIVIPOLA del territorio de la autoridad (5 dígitos municipio, 2 departamento).</param>
/// <param name="IdDispositivo">Dispositivo del evaluador de campo, si la petición viene del móvil.</param>
public sealed record Identidad(
    string Usuario,
    IReadOnlySet<string> Roles,
    string? Organizacion,
    string? Ambito,
    string? Territorio,
    string? IdDispositivo)
{
    public bool Tiene(string rol) => Roles.Contains(rol);

    public bool TieneAlguno(params string[] roles) => roles.Any(Roles.Contains);
}

/// <summary>Puerto: la identidad de la petición en curso, o nula si la petición no trae identidad válida.</summary>
public interface IIdentidadActual
{
    Identidad? Actual { get; }
}
