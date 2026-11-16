using System.Reflection;

namespace Plataforma.Comun.Eventos;

/// <summary>Acceso a los esquemas JSON del contrato, embebidos en el ensamblado.</summary>
public static class EsquemasEmbebidos
{
    /// <summary>URI base de los <c>$id</c> de los esquemas. Las referencias entre esquemas son relativas a ella.</summary>
    public static Uri UriBase { get; } = new("https://plataforma.local/esquemas/");

    public const string Comun = "comun.v1.json";
    public const string Sobre = "sobre.v1.json";

    private static readonly Assembly Ensamblado = typeof(EsquemasEmbebidos).Assembly;

    /// <summary>Nombres de todos los esquemas embebidos, por ejemplo "EV-18.v1.json".</summary>
    public static IReadOnlyList<string> Nombres { get; } = Ensamblado.GetManifestResourceNames()
        .Where(n => n.StartsWith("Esquemas/", StringComparison.Ordinal))
        .Select(n => n["Esquemas/".Length..])
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToList();

    public static string Leer(string nombre)
    {
        using Stream flujo = Ensamblado.GetManifestResourceStream("Esquemas/" + nombre)
            ?? throw new FileNotFoundException($"El esquema {nombre} no está en el contrato.");
        using StreamReader lector = new(flujo);
        return lector.ReadToEnd();
    }

    public static Uri UriDe(string nombre) => new(UriBase, nombre);
}
