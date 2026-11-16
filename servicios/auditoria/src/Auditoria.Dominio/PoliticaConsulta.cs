namespace Auditoria.Dominio;

/// <summary>Quien consulta el rastro: su usuario y sus roles de la plataforma (DA-15).</summary>
public sealed record Solicitante(string Usuario, IReadOnlySet<string> Roles);

/// <summary>
/// Quién puede leer el registro de auditoría (DA-17): el auditor (ente de control, fuente del estímulo de QA-13) y la
/// autoridad (responsable del programa, fuente de QA-15). Nadie puede escribir: el registro solo se alimenta del bus
/// (AD-21), así que no hay regla de escritura que evaluar.
/// </summary>
public static class PoliticaConsulta
{
    public const string RolAuditor = "auditor";
    public const string RolAutoridad = "autoridad";

    public static IReadOnlyList<string> RolesPermitidos { get; } = [RolAuditor, RolAutoridad];

    public static void ExigirLectura(Solicitante solicitante)
    {
        if (!RolesPermitidos.Any(solicitante.Roles.Contains))
        {
            throw new NoAutorizadoException("Solo los roles auditor y autoridad pueden consultar el registro de auditoría.");
        }
    }
}
