using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace ConsultaDanos.Api.Tablero;

public sealed record DesastreTablero(Guid Id, string Nombre, string? Estado, DateTimeOffset? DatoAl);

/// <summary>
/// Los desastres del selector. Consulta de Daños solo guarda su identificador; el nombre es del servicio Consulta (AD-01).
/// Este puerto vive en el adaptador porque solo lo necesita la pantalla: los casos de uso no cambian.
/// </summary>
public interface IDirectorioDesastres
{
    Task<IReadOnlyList<DesastreTablero>> ListarAsync(CancellationToken ct);
}

/// <summary>Lee el tablero de desastres del servicio Consulta por la red interna, con la cuenta de servicio del tablero.</summary>
public sealed class DirectorioDesastresConsulta : IDirectorioDesastres
{
    private sealed record FilaConsulta(Guid Id, string? Nombre, string? Estado, DateTimeOffset? DatoAl);
    private sealed record RespuestaConsulta(IReadOnlyList<FilaConsulta> Datos);

    private readonly HttpClient _http;
    private readonly TokenServicioTablero _token;

    public DirectorioDesastresConsulta(HttpClient http, TokenServicioTablero token)
    {
        _http = http;
        _token = token;
    }

    public async Task<IReadOnlyList<DesastreTablero>> ListarAsync(CancellationToken ct)
    {
        using HttpRequestMessage peticion = new(HttpMethod.Get, "v1/consulta/desastres");
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _token.ObtenerAsync(ct));
        using HttpResponseMessage respuesta = await _http.SendAsync(peticion, ct);
        respuesta.EnsureSuccessStatusCode();
        RespuestaConsulta? cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaConsulta>(ct);
        return (cuerpo?.Datos ?? [])
            .Select(d => new DesastreTablero(d.Id, d.Nombre ?? "Desastre sin nombre", d.Estado, d.DatoAl))
            // Como en el mapa: primero los que siguen en curso y, entre ellos, los que cambiaron hace menos.
            .OrderBy(d => d.Estado == "CERRADO")
            .ThenByDescending(d => d.DatoAl)
            .ToList();
    }
}

/// <summary>Token de la cuenta de servicio del cliente del tablero, guardado hasta poco antes de vencer.</summary>
public sealed class TokenServicioTablero
{
    private sealed record RespuestaToken(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiraEn);

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<OpenIdConnectOptions> _oidc;
    private readonly OpcionesTablero _opciones;
    private readonly SemaphoreSlim _candado = new(1, 1);
    private string? _token;
    private DateTimeOffset _vence;

    public TokenServicioTablero(HttpClient http, IOptionsMonitor<OpenIdConnectOptions> oidc, OpcionesTablero opciones)
    {
        _http = http;
        _oidc = oidc;
        _opciones = opciones;
    }

    public async Task<string> ObtenerAsync(CancellationToken ct)
    {
        await _candado.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _vence)
            {
                return _token;
            }
            OpenIdConnectOptions oidc = _oidc.Get(RegistroTablero.EsquemaOidc);
            var configuracion = await oidc.ConfigurationManager!.GetConfigurationAsync(ct);
            using HttpResponseMessage respuesta = await _http.PostAsync(configuracion.TokenEndpoint, new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = _opciones.ClienteId,
                    ["client_secret"] = _opciones.ClaveCliente,
                }), ct);
            respuesta.EnsureSuccessStatusCode();
            RespuestaToken token = await respuesta.Content.ReadFromJsonAsync<RespuestaToken>(ct)
                ?? throw new InvalidOperationException("Keycloak no devolvió un token.");
            _token = token.AccessToken;
            _vence = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, token.ExpiraEn - 30));
            return _token;
        }
        finally
        {
            _candado.Release();
        }
    }
}
