using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using ConsultaDanos.Dominio;
using Plataforma.Comun.Seguridad;

namespace ConsultaDanos.Api.Tablero;

// La pantalla lee la misma respuesta que ConsultarConsolidado entrega a la API, venga de la base, de la caché o degradada.
public sealed record ZonaVista(int Prioridad, string CodigoMunicipio, string? Nombre, string? CodigoDepartamento, int Activos,
    double? IndiceDano, int CriticosGraves, Dictionary<string, int> Distribucion);

public sealed record TipoVista(string TipoActivo, string? Nombre, bool Critico, int Activos, double? IndiceDano,
    Dictionary<string, int> Distribucion);

public sealed record IntegracionVista(string Sistema, string Estado, DateTimeOffset Desde, long MensajesEnCola);

public sealed record TotalVista(int Activos, double? IndiceDano, Dictionary<string, int> Distribucion);

public sealed record ConsolidadoVista(Guid IdDesastre, DateTimeOffset? DatoAl, bool Degradado, IReadOnlyList<string> TiposCriticos,
    TotalVista Total, IReadOnlyList<ZonaVista> Zonas, IReadOnlyList<TipoVista> PorTipo, IReadOnlyList<IntegracionVista> Integraciones)
{
    public static ConsolidadoVista Leer(string json) =>
        JsonSerializer.Deserialize<ConsolidadoVista>(json, JsonSerializerOptions.Web)
        ?? throw new JsonException("El consolidado llegó vacío.");
}

/// <summary>Textos de la pantalla, con la fecha en hora de Colombia y los decimales con coma.</summary>
public static class FormatoTablero
{
    private static readonly TimeSpan HoraColombia = TimeSpan.FromHours(-5);

    public static IReadOnlyList<(NivelDano Nivel, string Nombre)> Niveles { get; } =
    [
        (NivelDano.SIN_DANO, "Sin daño"), (NivelDano.LEVE, "Leve"), (NivelDano.MODERADO, "Moderado"),
        (NivelDano.GRAVE, "Grave"), (NivelDano.DESTRUCCION_TOTAL, "Destrucción total"),
    ];

    public static string Fecha(DateTimeOffset? instante)
    {
        if (instante is null)
        {
            return "sin dato";
        }
        DateTimeOffset local = instante.Value.ToOffset(HoraColombia);
        string periodo = local.Hour < 12 ? "a. m." : "p. m.";
        return $"{local.ToString("dd/MM/yyyy, h:mm", CultureInfo.InvariantCulture)} {periodo}";
    }

    public static string Numero(double? valor, string formato = "0.##") =>
        valor is null ? "sin dato" : valor.Value.ToString(formato, CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>La clase de color del índice: el nivel cuyo peso (0, 0,25, 0,5, 0,75 o 1) tiene más cerca, como en el mapa.</summary>
    public static string ClaseIndice(double? indice) =>
        indice is null ? "nivel-vacio" : $"nivel-{(int)Math.Round(Math.Clamp(indice.Value, 0, 1) * 4, MidpointRounding.AwayFromZero)}";

    public static string Legible(string codigo)
    {
        string texto = codigo.Replace('_', ' ').ToLowerInvariant();
        return texto.Length == 0 ? texto : char.ToUpperInvariant(texto[0]) + texto[1..];
    }

    public static string EstadoIntegracion(string estado) => estado switch
    {
        "CERRADO" => "En servicio",
        "ABIERTO" => "No responde",
        "SEMIABIERTO" => "En prueba",
        _ => Legible(estado),
    };

    public static string Rol(ClaimsPrincipal usuario)
    {
        string[] nombres = usuario.FindAll(RegistroTablero.ClaimRoles).Select(c => c.Value switch
        {
            RolesPlataforma.Autoridad => "Autoridad",
            RolesPlataforma.Organismo => "Organismo de respuesta",
            RolesPlataforma.Operador => "Operador",
            RolesPlataforma.Auditor => "Auditor",
            RolesPlataforma.ResponsableProyecto => "Responsable de proyecto",
            RolesPlataforma.Evaluador => "Evaluador",
            RolesPlataforma.Administrador => "Administrador",
            _ => "",
        }).Where(n => n.Length > 0).ToArray();
        string ambito = usuario.FindFirst("ambito")?.Value switch
        {
            AmbitosPlataforma.Local => " local",
            AmbitosPlataforma.Regional => " regional",
            AmbitosPlataforma.Nacional => " nacional",
            _ => "",
        };
        return string.Join(", ", nombres) + (usuario.HasClaim(RegistroTablero.ClaimRoles, RolesPlataforma.Autoridad) ? ambito : "");
    }
}
