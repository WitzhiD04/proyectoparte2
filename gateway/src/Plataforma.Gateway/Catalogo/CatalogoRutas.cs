using Microsoft.Extensions.Configuration;
using Plataforma.Comun.Seguridad;

namespace Plataforma.Gateway.Catalogo;

/// <summary>
/// Catálogo de rutas del gateway (DA-12): cada ruta de YARP (<c>ReverseProxy:Routes</c>) declara en sus metadatos el
/// servicio, los roles de nivel 1, la clase de tráfico y la regla de dominio de nivel 2. Al arrancar se valida, y el
/// gateway no arranca si una ruta de modificación no declara su regla de dominio (mitigación de R-07 del ATAM).
/// </summary>
public static class CatalogoRutas
{
    public const string Seccion = "ReverseProxy";

    public const string Servicio = "servicio";
    public const string Roles = "roles";
    public const string ClaseTrafico = "claseTrafico";
    public const string ReglaDominio = "reglaDominio";

    /// <summary>Cualquier usuario autenticado, sin importar su rol.</summary>
    public const string CualquierRol = "*";
    /// <summary>Sin autenticación: solo los archivos de la consola (GET y HEAD).</summary>
    public const string Publico = "publico";
    /// <summary>La ruta no tiene regla de nivel 2: basta el rol.</summary>
    public const string SinRegla = "ninguna";

    public static IReadOnlySet<string> Clases { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ClasesTrafico.Consola, ClasesTrafico.Movil, ClasesTrafico.Portal,
    };

    private static readonly HashSet<string> MetodosDeModificacion = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH", "DELETE",
    };

    /// <summary>Errores del catálogo; vacío si es válido.</summary>
    public static IReadOnlyList<string> Validar(IConfiguration configuracion)
    {
        List<string> errores = new();
        IConfigurationSection rutas = configuracion.GetSection($"{Seccion}:Routes");
        IConfigurationSection clusters = configuracion.GetSection($"{Seccion}:Clusters");
        if (!rutas.GetChildren().Any())
        {
            errores.Add("El catálogo no tiene rutas (ReverseProxy:Routes).");
            return errores;
        }
        foreach (IConfigurationSection ruta in rutas.GetChildren())
        {
            string id = ruta.Key;
            string? cluster = ruta["ClusterId"];
            if (string.IsNullOrWhiteSpace(cluster) || !clusters.GetSection(cluster).Exists())
            {
                errores.Add($"Ruta {id}: el clúster '{cluster}' no existe.");
            }
            string[] metodos = ruta.GetSection("Match:Methods").GetChildren().Select(m => m.Value ?? "").ToArray();
            // Una ruta sin métodos acepta todos, incluidos los de modificación.
            bool modifica = metodos.Length == 0 || metodos.Any(MetodosDeModificacion.Contains);
            IConfigurationSection metadatos = ruta.GetSection("Metadata");

            if (string.IsNullOrWhiteSpace(metadatos[Servicio]))
            {
                errores.Add($"Ruta {id}: falta el metadato '{Servicio}'.");
            }
            string? clase = metadatos[ClaseTrafico];
            if (clase is null || !Clases.Contains(clase))
            {
                errores.Add($"Ruta {id}: '{ClaseTrafico}' debe ser consola, movil o portal (llegó '{clase}').");
            }
            string? roles = metadatos[Roles];
            if (string.IsNullOrWhiteSpace(roles))
            {
                errores.Add($"Ruta {id}: falta el metadato '{Roles}'.");
            }
            else if (roles != CualquierRol && roles != Publico)
            {
                foreach (string rol in LeerRoles(roles).Where(r => !RolesPlataforma.Todos.Contains(r)))
                {
                    errores.Add($"Ruta {id}: el rol '{rol}' no existe en el realm.");
                }
            }
            if (modifica)
            {
                if (string.IsNullOrWhiteSpace(metadatos[ReglaDominio]))
                {
                    // R-07: una ruta de modificación sin regla de dominio declarada no entra al catálogo.
                    errores.Add($"Ruta {id}: es de modificación ({DescribirMetodos(metodos)}) y no declara '{ReglaDominio}'.");
                }
                if (roles == Publico)
                {
                    errores.Add($"Ruta {id}: una ruta de modificación no puede ser pública.");
                }
            }
        }
        return errores;
    }

    /// <summary>Lanza si el catálogo no es válido: el gateway no debe arrancar con una ruta sin regla (R-07).</summary>
    public static void ExigirValido(IConfiguration configuracion)
    {
        IReadOnlyList<string> errores = Validar(configuracion);
        if (errores.Count > 0)
        {
            throw new CatalogoInvalidoException(errores);
        }
    }

    public static IReadOnlyList<string> LeerRoles(string roles) =>
        roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string DescribirMetodos(string[] metodos) => metodos.Length == 0 ? "todos los métodos" : string.Join(", ", metodos);
}

public static class ClasesTrafico
{
    public const string Consola = "consola";
    public const string Movil = "movil";
    public const string Portal = "portal";
}

public sealed class CatalogoInvalidoException : Exception
{
    public IReadOnlyList<string> Errores { get; }

    public CatalogoInvalidoException(IReadOnlyList<string> errores)
        : base("El catálogo de rutas del gateway no es válido (DA-12, R-07):" + Environment.NewLine + string.Join(Environment.NewLine, errores))
    {
        Errores = errores;
    }
}
