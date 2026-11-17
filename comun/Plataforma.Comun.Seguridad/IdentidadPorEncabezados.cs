using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Plataforma.Comun.Seguridad;

/// <summary>
/// Adaptador de transición: lee la identidad de encabezados mientras no exista el gateway (simplificación declarada,
/// la misma del Anexo A del SAD). Los encabezados se pueden falsificar; por eso se reemplaza por el adaptador JWT en
/// el paso del gateway (DA-15), y ningún servicio cambia porque todos usan <see cref="IIdentidadActual"/>.
/// </summary>
public sealed class IdentidadPorEncabezados : IIdentidadActual
{
    public const string Usuario = "X-Usuario";
    /// <summary>Uno o varios roles separados por comas, con los nombres de <see cref="RolesPlataforma"/>.</summary>
    public const string Roles = "X-Roles";
    public const string Organizacion = "X-Organizacion";
    public const string Ambito = "X-Ambito";
    public const string Territorio = "X-Territorio";
    public const string Dispositivo = "X-Dispositivo";

    private readonly IHttpContextAccessor _http;

    public IdentidadPorEncabezados(IHttpContextAccessor http)
    {
        _http = http;
    }

    public Identidad? Actual
    {
        get
        {
            IHeaderDictionary? encabezados = _http.HttpContext?.Request.Headers;
            string? usuario = Leer(encabezados, Usuario);
            if (encabezados is null || usuario is null)
            {
                return null;
            }
            HashSet<string> roles = (Leer(encabezados, Roles) ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(RolesPlataforma.Todos.Contains)
                .ToHashSet(StringComparer.Ordinal);
            string? ambito = Leer(encabezados, Ambito)?.ToUpperInvariant();
            if (ambito is not null and not (AmbitosPlataforma.Local or AmbitosPlataforma.Regional or AmbitosPlataforma.Nacional))
            {
                return null;
            }
            return new Identidad(usuario, roles, Leer(encabezados, Organizacion), ambito, Leer(encabezados, Territorio),
                Leer(encabezados, Dispositivo));
        }
    }

    private static string? Leer(IHeaderDictionary? encabezados, string nombre)
    {
        string? valor = encabezados?[nombre].FirstOrDefault();
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }
}

public static class RegistroSeguridad
{
    public const string Seccion = "Identidad";

    /// <summary>
    /// Registra <see cref="IIdentidadActual"/>. "Identidad:Modo" elige el adaptador: "Encabezados" (por omisión) o "Jwt" (DA-15).
    /// </summary>
    public static IServiceCollection AgregarIdentidad(this IServiceCollection servicios, IConfiguration configuracion)
    {
        string modo = (configuracion[$"{Seccion}:Modo"] ?? "Encabezados").Trim();
        servicios.AddHttpContextAccessor();
        if (modo.Equals("Encabezados", StringComparison.OrdinalIgnoreCase))
        {
            servicios.AddScoped<IIdentidadActual, IdentidadPorEncabezados>();
        }
        else if (modo.Equals("Jwt", StringComparison.OrdinalIgnoreCase))
        {
            var opciones = new OpcionesIdentidadJwt
            {
                Emisor = configuracion[$"{Seccion}:Emisor"] ?? OpcionesIdentidadJwt.EmisorPorOmision,
                Audiencia = configuracion[$"{Seccion}:Audiencia"] ?? OpcionesIdentidadJwt.AudienciaPorOmision,
                Metadatos = configuracion[$"{Seccion}:Metadatos"] ?? OpcionesIdentidadJwt.MetadatosPorOmision,
            };
            servicios.AddSingleton(opciones);
            servicios.AddSingleton<ValidadorJwt>();
            servicios.AddScoped<IIdentidadActual, IdentidadPorJwt>();
        }
        else
        {
            throw new InvalidOperationException($"Identidad:Modo desconocido: {modo}.");
        }
        return servicios;
    }
}
