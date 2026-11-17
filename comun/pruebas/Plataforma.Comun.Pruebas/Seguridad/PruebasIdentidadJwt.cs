using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Plataforma.Comun.Seguridad;

namespace Plataforma.Comun.Pruebas.Seguridad;

/// <summary>
/// Pruebas unitarias del adaptador JWT (<see cref="IdentidadPorJwt"/> y <see cref="ValidadorJwt"/>, DA-15)
/// con tokens firmados por llaves RSA de prueba.
/// </summary>
public sealed class PruebasIdentidadJwt : IDisposable
{
    private const string Emisor = "http://localhost:8180/realms/plataforma";
    private const string Audiencia = "gateway";

    private readonly RSA _rsaValida = RSA.Create(2048);
    private readonly RSA _rsaOtra = RSA.Create(2048);
    private readonly RsaSecurityKey _llaveValida;
    private readonly RsaSecurityKey _llaveOtra;

    public PruebasIdentidadJwt()
    {
        _llaveValida = new RsaSecurityKey(_rsaValida) { KeyId = "llave-valida-1" };
        _llaveOtra = new RsaSecurityKey(_rsaOtra) { KeyId = "llave-otra-2" };
    }

    public void Dispose()
    {
        _rsaValida.Dispose();
        _rsaOtra.Dispose();
    }

    private string EmitirToken(
        string usuario = "bomberos.operador",
        string[]? roles = null,
        string? organizacion = "bomberos",
        string? ambito = null,
        string? territorio = null,
        string emisor = Emisor,
        string audiencia = Audiencia,
        DateTime? inicioVigencia = null,
        DateTime? vencimiento = null,
        SecurityKey? llaveFirma = null)
    {
        DateTime ahora = DateTime.UtcNow;
        var reclamos = new Dictionary<string, object>
        {
            ["preferred_username"] = usuario,
            ["realm_access"] = new Dictionary<string, object>
            {
                ["roles"] = roles ?? [RolesPlataforma.Organismo, "default-roles-plataforma"],
            },
        };
        if (organizacion is not null)
        {
            reclamos["organizacion"] = organizacion;
        }
        if (ambito is not null)
        {
            reclamos["ambito"] = ambito;
        }
        if (territorio is not null)
        {
            reclamos["territorio"] = territorio;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = emisor,
            Audience = audiencia,
            NotBefore = inicioVigencia ?? ahora.AddMinutes(-1),
            IssuedAt = inicioVigencia ?? ahora.AddMinutes(-1),
            Expires = vencimiento ?? ahora.AddMinutes(10),
            Claims = reclamos,
            SigningCredentials = new SigningCredentials(llaveFirma ?? _llaveValida, SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private IdentidadPorJwt CrearAdaptador(HttpContext contexto, IConfigurationManager<OpenIdConnectConfiguration>? gestor = null)
    {
        var opciones = new OpcionesIdentidadJwt
        {
            Emisor = Emisor,
            Audiencia = Audiencia,
            Metadatos = null,
            LlavesFirma = gestor is null ? [_llaveValida] : null,
            AdministradorConfiguracion = gestor,
            ToleranciaReloj = TimeSpan.Zero,
        };
        var validador = new ValidadorJwt(opciones);
        var accesor = new HttpContextAccessor { HttpContext = contexto };
        return new IdentidadPorJwt(accesor, validador);
    }

    [Fact]
    public void Token_valido_construye_identidad_con_usuario_roles_atributos_y_dispositivo()
    {
        string token = EmitirToken(
            usuario: "ungrd.operador",
            roles: [RolesPlataforma.Autoridad, "offline_access", "uma_authorization"],
            organizacion: "ungrd",
            ambito: "NACIONAL",
            territorio: "63001");

        var contexto = new DefaultHttpContext();
        contexto.Request.Headers.Authorization = $"Bearer {token}";
        contexto.Request.Headers["X-Dispositivo"] = "movil-campo-01";

        Identidad? identidad = CrearAdaptador(contexto).Actual;

        Assert.NotNull(identidad);
        Assert.Equal("ungrd.operador", identidad.Usuario);
        Assert.Single(identidad.Roles, RolesPlataforma.Autoridad);
        Assert.Equal("ungrd", identidad.Organizacion);
        Assert.Equal(AmbitosPlataforma.Nacional, identidad.Ambito);
        Assert.Equal("63001", identidad.Territorio);
        Assert.Equal("movil-campo-01", identidad.IdDispositivo);
    }

    [Fact]
    public void Token_vencido_devuelve_nulo()
    {
        DateTime haceDiezMinutos = DateTime.UtcNow.AddMinutes(-10);
        string token = EmitirToken(
            inicioVigencia: haceDiezMinutos.AddMinutes(-5),
            vencimiento: haceDiezMinutos);

        var contexto = new DefaultHttpContext();
        contexto.Request.Headers.Authorization = $"Bearer {token}";

        Assert.Null(CrearAdaptador(contexto).Actual);
    }

    [Fact]
    public void Token_con_otra_audiencia_devuelve_nulo()
    {
        string token = EmitirToken(audiencia: "otra-audiencia");

        var contexto = new DefaultHttpContext();
        contexto.Request.Headers.Authorization = $"Bearer {token}";

        Assert.Null(CrearAdaptador(contexto).Actual);
    }

    [Fact]
    public void Token_con_otro_emisor_devuelve_nulo()
    {
        string token = EmitirToken(emisor: "http://otro-keycloak:8180/realms/plataforma");

        var contexto = new DefaultHttpContext();
        contexto.Request.Headers.Authorization = $"Bearer {token}";

        Assert.Null(CrearAdaptador(contexto).Actual);
    }

    [Fact]
    public void Token_con_firma_invalida_devuelve_nulo()
    {
        string token = EmitirToken(llaveFirma: _llaveOtra);

        var contexto = new DefaultHttpContext();
        contexto.Request.Headers.Authorization = $"Bearer {token}";

        Assert.Null(CrearAdaptador(contexto).Actual);
    }

    [Fact]
    public void Con_token_valido_se_ignora_X_Usuario_y_cualquier_encabezado_de_identidad_distinto()
    {
        string token = EmitirToken(
            usuario: "bomberos.operador",
            roles: [RolesPlataforma.Organismo],
            organizacion: "bomberos",
            ambito: null,
            territorio: null);

        var contexto = new DefaultHttpContext();
        contexto.Request.Headers.Authorization = $"Bearer {token}";
        contexto.Request.Headers["X-Usuario"] = "usuario.impostor";
        contexto.Request.Headers["X-Roles"] = "autoridad,administrador";
        contexto.Request.Headers["X-Organizacion"] = "organizacion-falsa";
        contexto.Request.Headers["X-Ambito"] = "NACIONAL";
        contexto.Request.Headers["X-Territorio"] = "99999";
        contexto.Request.Headers["X-Dispositivo"] = "android-07";

        Identidad? identidad = CrearAdaptador(contexto).Actual;

        Assert.NotNull(identidad);
        Assert.Equal("bomberos.operador", identidad.Usuario);
        Assert.Single(identidad.Roles, RolesPlataforma.Organismo);
        Assert.False(identidad.Tiene(RolesPlataforma.Autoridad));
        Assert.False(identidad.Tiene(RolesPlataforma.Administrador));
        Assert.Equal("bomberos", identidad.Organizacion);
        Assert.Null(identidad.Ambito);
        Assert.Null(identidad.Territorio);
        Assert.Equal("android-07", identidad.IdDispositivo);
    }

    [Fact]
    public void Peticion_solo_con_encabezados_X_Usuario_en_modo_Jwt_devuelve_nulo()
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Headers["X-Usuario"] = "ungrd.operador";
        contexto.Request.Headers["X-Roles"] = "autoridad";

        Assert.Null(CrearAdaptador(contexto).Actual);
    }

    [Fact]
    public void Caida_corta_de_Keycloak_usa_las_llaves_cacheadas_sin_invalidar_tokens_emitidos_DA15()
    {
        var configuracionOidc = new OpenIdConnectConfiguration();
        configuracionOidc.SigningKeys.Add(_llaveValida);
        var gestor = new GestorConfiguracionSimulado(configuracionOidc);

        var opciones = new OpcionesIdentidadJwt
        {
            Emisor = Emisor,
            Audiencia = Audiencia,
            Metadatos = null,
            AdministradorConfiguracion = gestor,
            ToleranciaReloj = TimeSpan.Zero,
        };
        var validador = new ValidadorJwt(opciones);

        string token = EmitirToken(usuario: "evaluador.armenia", roles: [RolesPlataforma.Evaluador], organizacion: "alcaldia-armenia");

        // Primera validación con Keycloak disponible: descarga y guarda las llaves en caché.
        Identidad? primera = validador.Validar(token);
        Assert.NotNull(primera);
        Assert.Equal("evaluador.armenia", primera.Usuario);

        // Keycloak cae: el gestor lanza excepción, pero el validador conserva las llaves en caché.
        gestor.SimularCaida = true;
        Identidad? duranteCaida = validador.Validar(token);
        Assert.NotNull(duranteCaida);
        Assert.Equal("evaluador.armenia", duranteCaida.Usuario);
    }

    [Fact]
    public void AgregarIdentidad_usa_Encabezados_por_omision_y_Jwt_cuando_se_configura()
    {
        IConfiguration configPorOmision = new ConfigurationBuilder().Build();
        using ServiceProvider provEncabezados = new ServiceCollection()
            .AgregarIdentidad(configPorOmision)
            .BuildServiceProvider();
        using (IServiceScope scope = provEncabezados.CreateScope())
        {
            Assert.IsType<IdentidadPorEncabezados>(scope.ServiceProvider.GetRequiredService<IIdentidadActual>());
        }

        IConfiguration configJwt = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Identidad:Modo"] = "Jwt" })
            .Build();
        using ServiceProvider provJwt = new ServiceCollection()
            .AgregarIdentidad(configJwt)
            .BuildServiceProvider();
        using (IServiceScope scope = provJwt.CreateScope())
        {
            Assert.IsType<IdentidadPorJwt>(scope.ServiceProvider.GetRequiredService<IIdentidadActual>());
        }
    }

    private sealed class GestorConfiguracionSimulado : IConfigurationManager<OpenIdConnectConfiguration>
    {
        private readonly OpenIdConnectConfiguration _configuracion;
        public bool SimularCaida { get; set; }

        public GestorConfiguracionSimulado(OpenIdConnectConfiguration configuracion)
        {
            _configuracion = configuracion;
        }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            if (SimularCaida)
            {
                throw new HttpRequestException("Keycloak no responde temporalmente.");
            }
            return Task.FromResult(_configuracion);
        }

        public void RequestRefresh() { }
    }
}
