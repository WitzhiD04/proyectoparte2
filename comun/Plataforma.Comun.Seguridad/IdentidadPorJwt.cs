using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Plataforma.Comun.Seguridad;

/// <summary>
/// Parámetros de validación del JWT emitido por Keycloak (DA-15).
/// </summary>
public sealed class OpcionesIdentidadJwt
{
    public const string EmisorPorOmision = "http://localhost:8180/realms/plataforma";
    public const string AudienciaPorOmision = "gateway";
    public const string MetadatosPorOmision = "http://keycloak:8080/realms/plataforma/.well-known/openid-configuration";

    /// <summary>Emisor esperado en el claim <c>iss</c>.</summary>
    public string Emisor { get; init; } = EmisorPorOmision;

    /// <summary>Audiencia esperada en el claim <c>aud</c>.</summary>
    public string Audiencia { get; init; } = AudienciaPorOmision;

    /// <summary>URL del documento de descubrimiento OIDC desde donde se obtienen y cachean las llaves públicas (JWKS).</summary>
    public string? Metadatos { get; init; } = MetadatosPorOmision;

    /// <summary>Llaves de firma fijas opcionales (usadas en pruebas unitarias sin servidor OIDC).</summary>
    public IReadOnlyCollection<SecurityKey>? LlavesFirma { get; init; }

    /// <summary>Administrador de configuración OIDC opcional (permite inyectar un doble en pruebas).</summary>
    public IConfigurationManager<OpenIdConnectConfiguration>? AdministradorConfiguracion { get; init; }

    /// <summary>Margen de tolerancia del reloj al validar <c>exp</c> y <c>nbf</c>.</summary>
    public TimeSpan ToleranciaReloj { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Valida tokens JWT y mantiene en caché las llaves públicas del emisor para que una caída corta de Keycloak
/// no invalide los tokens ya emitidos (DA-15). Se registra como singleton.
/// </summary>
public sealed class ValidadorJwt
{
    private readonly OpcionesIdentidadJwt _opciones;
    private readonly IConfigurationManager<OpenIdConnectConfiguration>? _administradorConfiguracion;
    private readonly JsonWebTokenHandler _manejador = new() { MapInboundClaims = false };
    private IReadOnlyCollection<SecurityKey> _llavesEnCache = [];

    public ValidadorJwt(OpcionesIdentidadJwt opciones)
    {
        _opciones = opciones ?? throw new ArgumentNullException(nameof(opciones));
        if (_opciones.LlavesFirma is { Count: > 0 })
        {
            _llavesEnCache = _opciones.LlavesFirma;
        }
        else if (_opciones.AdministradorConfiguracion is not null)
        {
            _administradorConfiguracion = _opciones.AdministradorConfiguracion;
        }
        else if (!string.IsNullOrWhiteSpace(_opciones.Metadatos))
        {
            var recuperadorHttp = new HttpDocumentRetriever
            {
                RequireHttps = _opciones.Metadatos.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
            };
            _administradorConfiguracion = new ConfigurationManager<OpenIdConnectConfiguration>(
                _opciones.Metadatos,
                new OpenIdConnectConfigurationRetriever(),
                recuperadorHttp)
            {
                AutomaticRefreshInterval = TimeSpan.FromHours(1),
                RefreshInterval = TimeSpan.FromSeconds(30),
            };
        }
    }

    public Identidad? Validar(string? token, string? idDispositivo = null) =>
        ValidarAsync(token, idDispositivo).GetAwaiter().GetResult();

    public async Task<Identidad?> ValidarAsync(string? token, string? idDispositivo = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        IReadOnlyCollection<SecurityKey> llaves = await ObtenerLlavesAsync(ct).ConfigureAwait(false);
        if (llaves.Count == 0)
        {
            return null;
        }

        var parametros = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = llaves,
            ValidateIssuer = true,
            ValidIssuer = _opciones.Emisor,
            ValidateAudience = true,
            ValidAudience = _opciones.Audiencia,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = _opciones.ToleranciaReloj,
        };

        TokenValidationResult resultado = await _manejador.ValidateTokenAsync(token.Trim(), parametros).ConfigureAwait(false);
        if (!resultado.IsValid || resultado.SecurityToken is not JsonWebToken jwt)
        {
            return null;
        }

        return ExtraerIdentidad(jwt, idDispositivo);
    }

    private async Task<IReadOnlyCollection<SecurityKey>> ObtenerLlavesAsync(CancellationToken ct)
    {
        if (_opciones.LlavesFirma is { Count: > 0 })
        {
            return _opciones.LlavesFirma;
        }
        if (_administradorConfiguracion is null)
        {
            return _llavesEnCache;
        }

        try
        {
            OpenIdConnectConfiguration configuracion = await _administradorConfiguracion.GetConfigurationAsync(ct).ConfigureAwait(false);
            if (configuracion.SigningKeys is { Count: > 0 })
            {
                _llavesEnCache = configuracion.SigningKeys.ToArray();
            }
        }
        catch
        {
            // DA-15: si Keycloak sufre una caída corta al refrescar los metadatos, se conservan las llaves ya cacheadas
            // para no invalidar los tokens vigentes ya emitidos.
        }

        return _llavesEnCache;
    }

    private static Identidad? ExtraerIdentidad(JsonWebToken jwt, string? idDispositivo)
    {
        try
        {
            byte[] bytesPayload = Base64UrlEncoder.DecodeBytes(jwt.EncodedPayload);
            using JsonDocument doc = JsonDocument.Parse(bytesPayload);
            JsonElement raiz = doc.RootElement;

            string? usuario = LeerCadena(raiz, "preferred_username");
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return null;
            }

            var roles = new HashSet<string>(StringComparer.Ordinal);
            if (raiz.TryGetProperty("realm_access", out JsonElement realmAccess)
                && realmAccess.ValueKind == JsonValueKind.Object
                && realmAccess.TryGetProperty("roles", out JsonElement arregloRoles)
                && arregloRoles.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in arregloRoles.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        string? rol = item.GetString()?.Trim();
                        if (!string.IsNullOrEmpty(rol) && RolesPlataforma.Todos.Contains(rol))
                        {
                            roles.Add(rol);
                        }
                    }
                }
            }

            string? organizacion = LeerCadena(raiz, "organizacion");
            string? ambito = LeerCadena(raiz, "ambito")?.ToUpperInvariant();
            if (ambito is not null and not (AmbitosPlataforma.Local or AmbitosPlataforma.Regional or AmbitosPlataforma.Nacional))
            {
                return null;
            }

            string? territorio = LeerCadena(raiz, "territorio");

            return new Identidad(
                usuario,
                roles,
                organizacion,
                ambito,
                territorio,
                 string.IsNullOrWhiteSpace(idDispositivo) ? null : idDispositivo.Trim());
        }
        catch
        {
            return null;
        }
    }

    private static string? LeerCadena(JsonElement objeto, string propiedad)
    {
        if (!objeto.TryGetProperty(propiedad, out JsonElement elemento) || elemento.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        string? valor = elemento.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(valor) ? null : valor;
    }
}

/// <summary>
/// Adaptador de identidad en modo JWT (DA-15): valida el token de <c>Authorization: Bearer</c> contra el emisor,
/// la audiencia y las llaves públicas en caché de Keycloak, e ignora cualquier encabezado <c>X-Usuario</c>,
/// <c>X-Roles</c>, <c>X-Organizacion</c>, <c>X-Ambito</c> o <c>X-Territorio</c>.
/// Solo conserva la lectura de <c>X-Dispositivo</c>, que identifica el dispositivo móvil y no la identidad del actor.
/// </summary>
public sealed class IdentidadPorJwt : IIdentidadActual
{
    private const string PrefijoBearer = "Bearer ";
    private readonly IHttpContextAccessor _http;
    private readonly ValidadorJwt _validador;
    private bool _evaluado;
    private Identidad? _cachePeticion;

    public IdentidadPorJwt(IHttpContextAccessor http, ValidadorJwt validador)
    {
        _http = http;
        _validador = validador;
    }

    public Identidad? Actual
    {
        get
        {
            if (_evaluado)
            {
                return _cachePeticion;
            }
            _evaluado = true;

            IHeaderDictionary? encabezados = _http.HttpContext?.Request.Headers;
            if (encabezados is null)
            {
                return null;
            }

            string? autorizacion = encabezados.Authorization.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(autorizacion)
                || !autorizacion.StartsWith(PrefijoBearer, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string token = autorizacion[PrefijoBearer.Length..].Trim();
            if (token.Length == 0)
            {
                return null;
            }

            string? dispositivo = encabezados[IdentidadPorEncabezados.Dispositivo].FirstOrDefault();
            _cachePeticion = _validador.Validar(token, dispositivo);
            return _cachePeticion;
        }
    }
}
