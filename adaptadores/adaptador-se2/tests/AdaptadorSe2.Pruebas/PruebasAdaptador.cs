using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;
using Testcontainers.PostgreSql;

namespace AdaptadorSe2.Pruebas;

/// <summary>La capa real de municipios se traduce a EV-21 por partes (E-11). Sin base ni bus.</summary>
public class PruebasCapas
{
    public static byte[] CapaReal()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Plataforma.sln")))
        {
            dir = dir.Parent;
        }
        return File.ReadAllBytes(Path.Combine(dir!.FullName, "datos", "geo", "municipios-colombia.geojson"));
    }

    private static readonly DateTimeOffset Instante = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void La_capa_real_sale_en_una_parte_por_departamento_y_cada_una_cumple_el_contrato()
    {
        CapaLeida capa = Capas.Leer(CapaReal());
        IReadOnlyList<Sobre> sobres = Capas.ASobres(capa, Instante);

        Assert.Equal(1122, capa.Municipios.Count);
        Assert.Equal(33, sobres.Count);
        Assert.True(capa.Version > 0);
        HashSet<string> codigos = [];
        for (int i = 0; i < sobres.Count; i++)
        {
            ValidadorContrato.Instancia.Validar(sobres[i]);
            Assert.Equal("EV-21", sobres[i].Tipo);
            Assert.Equal("adaptador-se2", sobres[i].Productor);
            Assert.Equal($"capa:MUNICIPIOS:v{capa.Version}:{i + 1}", sobres[i].ClaveIdempotencia);
            Assert.Equal("SE-2", sobres[i].Procedencia.SistemaOrigen);
            var datos = sobres[i].LeerDatos<CapaGeograficaActualizadaV1>();
            Assert.Equal(i + 1, datos.Parte);
            Assert.Equal(33, datos.Total);
            Assert.Equal(capa.Version, datos.VersionCapa);
            Assert.Single(datos.Divisiones.Select(d => d.CodigoPadre).Distinct());   // un departamento por parte
            Assert.All(datos.Divisiones, d => Assert.True(codigos.Add(d.Codigo)));
            // Cada parte cabe con holgura en un mensaje del bus.
            Assert.True(Encoding.UTF8.GetByteCount(sobres[i].Serializar()) < 200_000);
        }
        Assert.Equal(1122, codigos.Count);
        Assert.Contains("63001", codigos);
        Assert.Contains("63130", codigos);
    }

    [Fact]
    public void La_version_sale_del_contenido_el_mismo_archivo_da_las_mismas_claves()
    {
        byte[] contenido = CapaReal();
        CapaLeida una = Capas.Leer(contenido);
        CapaLeida otra = Capas.Leer(contenido);
        Assert.Equal(una.Version, otra.Version);
        Assert.Equal(Capas.ASobres(una, Instante).Select(s => s.ClaveIdempotencia), Capas.ASobres(otra, Instante.AddDays(1)).Select(s => s.ClaveIdempotencia));

        byte[] cambiada = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(contenido).Replace("\"ARMENIA\"", "\"ARMENIA (QUINDÍO)\""));
        Assert.NotEqual(una.Version, Capas.Leer(cambiada).Version);
    }

    [Fact]
    public void Una_capa_con_un_municipio_invalido_se_rechaza_completa()
    {
        string capa = """
            {"type":"FeatureCollection","features":[{"type":"Feature",
             "properties":{"codigo":"6300","nombre":"ARMENIA","codigoDepartamento":"63"},
             "geometry":{"type":"Polygon","coordinates":[[[-75.6,4.5],[-75.7,4.4],[-75.8,4.5],[-75.6,4.5]]]}}]}
            """;
        Assert.Throws<InvalidDataException>(() => Capas.Leer(Encoding.UTF8.GetBytes(capa)));
        Assert.Throws<InvalidDataException>(() => Capas.Leer(Encoding.UTF8.GetBytes("""{"type":"FeatureCollection","features":[]}""")));
    }
}

/// <summary>Un PostgreSQL 18 con la base del adaptador migrada por su migrador, como en el despliegue (DT-19).</summary>
public sealed class BaseAdaptador : IAsyncLifetime
{
    public const string ClaveApp = "adaptador_se2_prueba";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.4").Build();

    public string Dueno => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = "adaptador_se2" }.ConnectionString;
    public string App => new NpgsqlConnectionStringBuilder(Dueno) { Username = MigracionAdaptadorSe2.RolApp, Password = ClaveApp }.ConnectionString;

    public IConfiguration Configuracion => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:AdaptadorSe2Dueno"] = Dueno,
        ["Migracion:ClaveAdaptadorSe2App"] = ClaveApp,
    }).Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await MigracionAdaptadorSe2.EjecutarAsync(Configuracion, NullLogger.Instance);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    public AdaptadorSe2DbContext ComoApp() => new(MigracionAdaptadorSe2.Opciones(App));
}

public class PruebasPublicacion(BaseAdaptador baseAdaptador) : IClassFixture<BaseAdaptador>
{
    private static async Task<ResultadoPublicacionCapa> PublicarAsync(AdaptadorSe2DbContext db, byte[] contenido) =>
        await new PublicacionDeCapas(db, new EscritorBandeja<AdaptadorSe2DbContext>(db)).PublicarSiEsNuevaAsync(contenido, CancellationToken.None);

    [Fact]
    public async Task La_capa_se_deja_en_la_bandeja_una_sola_vez_por_version()
    {
        byte[] contenido = PruebasCapas.CapaReal();
        await MigracionAdaptadorSe2.EjecutarAsync(baseAdaptador.Configuracion, NullLogger.Instance);   // migrar otra vez no falla

        ResultadoPublicacionCapa primera;
        await using (var db = baseAdaptador.ComoApp())
        {
            primera = await PublicarAsync(db, contenido);
        }
        ResultadoPublicacionCapa segunda;
        await using (var db = baseAdaptador.ComoApp())
        {
            segunda = await PublicarAsync(db, contenido);   // otra réplica o un reinicio: la versión ya salió
        }

        Assert.True(primera.Publicada);
        Assert.Equal(33, primera.Partes);
        Assert.False(segunda.Publicada);
        await using var consulta = baseAdaptador.ComoApp();
        Assert.Equal(33, await consulta.Bandeja.CountAsync(b => b.Tipo == "EV-21"));
        var registro = await consulta.CapasPublicadas.SingleAsync();
        Assert.Equal(primera.Version, registro.VersionCapa);
        Assert.Equal(1122, registro.Divisiones);
        // Lo que quedó en la bandeja es lo que va al bus: cumple el contrato.
        foreach (string sobre in await consulta.Bandeja.Select(b => b.Sobre).ToListAsync())
        {
            ValidadorContrato.Instancia.Validar(Sobre.Leer(sobre));
        }
    }

    [Theory]
    [InlineData("UPDATE capas_publicadas SET hash = 'x'")]
    [InlineData("DELETE FROM capas_publicadas")]
    [InlineData("DELETE FROM bandeja_salida")]
    [InlineData("CREATE TABLE intrusa (id int)")]
    public async Task El_rol_del_adaptador_tiene_minimo_privilegio(string sql)
    {
        await using NpgsqlConnection conexion = new(baseAdaptador.App);
        await conexion.OpenAsync();
        await using NpgsqlCommand comando = new(sql, conexion);
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }
}
