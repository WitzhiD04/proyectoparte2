using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;
using Reconstruccion.Aplicacion.CasosDeUso;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio;
using Reconstruccion.Infraestructura;
using Reconstruccion.Infraestructura.Persistencia;
using Cronograma = Reconstruccion.Dominio.Cronograma;
using NivelCanonico = Plataforma.Comun.Eventos.NivelDano;
using NivelDano = Reconstruccion.Dominio.NivelDano;

namespace Reconstruccion.Integracion;

/// <summary>
/// Las copias locales se alimentan con los eventos de los otros módulos (DA-01, paso 4) y los reportes de SE-7 quedan en
/// el proyecto (E-04). Cada evento pasa por el consumidor idempotente real, en su transacción, contra la base migrada.
/// </summary>
public class PruebasCopiasLocales : IClassFixture<BaseMigrada>, IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
    private readonly BaseMigrada _base;
    private ServiceProvider _servicios = null!;
    private ConsumidorIdempotente<ReconstruccionDbContext> _consumidor = null!;

    public PruebasCopiasLocales(BaseMigrada baseMigrada) { _base = baseMigrada; }

    public Task InitializeAsync()
    {
        IConfiguration configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Reconstruccion"] = _base.App,
            ["Bus:Cola"] = "q.reconstruccion",
            ["Bus:Productor"] = "reconstruccion",
        }).Build();
        ServiceCollection s = new();
        s.AddLogging();
        s.AgregarInfraestructura(configuracion, Registro.RolEscritura);
        s.AddScoped<GuardadoDeProyectos>();
        s.AddScoped<ActualizarCopiasLocales>();
        s.AddScoped<RegistrarReporteExterno>();
        _servicios = s.BuildServiceProvider();
        _consumidor = new(_servicios.GetRequiredService<IServiceScopeFactory>(), _servicios.GetRequiredService<ConexionBus>(),
            _servicios.GetRequiredService<OpcionesBus>(), NullLogger<ConsumidorIdempotente<ReconstruccionDbContext>>.Instance);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _servicios.DisposeAsync();

    private ReconstruccionDbContext ComoApp() => new(new DbContextOptionsBuilder<ReconstruccionDbContext>().UseNpgsql(_base.App).Options);

    private Task<bool> EntregarAsync(Sobre sobre) => _consumidor.AplicarAsync(sobre with { SecuenciaProductor = 1 }, default);

    private static Sobre S<T>(DefinicionEvento def, string productor, string clave, T datos, DateTimeOffset t) =>
        Sobre.Crear(def, productor, clave, datos, t);

    private static Sobre RegistroDesastre(Guid id, int version = 1) => S(CatalogoEventos.DesastreCreado, CatalogoEventos.GestionDesastres,
        $"desastre:{id}:v{version}", new DesastreCreadoV1(id, "TERREMOTO", Severidad.NIVEL_3, 1, EstadoDesastre.REGISTRADO, "ungrd", "ungrd",
            OrigenDesastre.MANUAL, "Sismo del Eje Cafetero", "Sismo", T0, new Ubicacion(4.53, -75.68), version), T0);

    private static Sobre Estado(Guid id, EstadoDesastre anterior, EstadoDesastre nuevo, int version) => S(CatalogoEventos.EstadoDesastreCambiado,
        CatalogoEventos.GestionDesastres, $"desastre:{id}:v{version}", new EstadoDesastreCambiadoV1(id, anterior, nuevo, "ungrd.operador", version),
        T0.AddMinutes(version));

    private static Sobre Validacion(Guid id, Guid desastre, EstadoValidacion estado, int version, string municipio = "63001") =>
        S(CatalogoEventos.EvaluacionValidada, CatalogoEventos.EvaluacionDanos, $"evaluacion:{id}:v{version}:validacion",
            new EvaluacionValidadaV1(id, desastre, "ESCUELA", NivelCanonico.GRAVE, new Ubicacion(4.53, -75.68), "evaluador.armenia",
                "alcaldia-armenia", version, Guid.NewGuid(), "Escuela La Esperanza", municipio, municipio[..2], estado, "bomberos.operador",
                T0.AddMinutes(version)), T0.AddMinutes(version));

    [Fact]
    public async Task Los_desastres_llegan_en_desorden_y_repetidos_y_la_copia_queda_en_el_estado_mas_nuevo()
    {
        Guid id = Guid.NewGuid();
        Assert.True(await EntregarAsync(Estado(id, EstadoDesastre.EN_RECUPERACION, EstadoDesastre.EN_RECONSTRUCCION, 6)));
        // Antes del registro, la copia no ofrece el desastre.
        await using (ReconstruccionDbContext db = ComoApp())
        {
            Assert.Null(await new CopiasLocalesEf(db).DesastreAsync(id));
        }
        Sobre registro = RegistroDesastre(id);
        Assert.True(await EntregarAsync(registro));
        Assert.False(await EntregarAsync(registro));
        Assert.True(await EntregarAsync(Estado(id, EstadoDesastre.REGISTRADO, EstadoDesastre.EN_ATENCION, 3)));

        await using ReconstruccionDbContext lectura = ComoApp();
        DesastreLocal? desastre = await new CopiasLocalesEf(lectura).DesastreAsync(id);
        Assert.NotNull(desastre);
        Assert.Equal("Sismo del Eje Cafetero", desastre.Nombre);
        Assert.Equal(EstadosDesastre.EnReconstruccion, desastre.Estado);
    }

    [Fact]
    public async Task Una_evaluacion_es_candidata_solo_mientras_su_ultima_validacion_la_deja_validada()
    {
        Guid desastre = Guid.NewGuid();
        Guid evaluacion = Guid.NewGuid();
        await EntregarAsync(RegistroDesastre(desastre));
        await EntregarAsync(Estado(desastre, EstadoDesastre.EN_RECUPERACION, EstadoDesastre.EN_RECONSTRUCCION, 6));
        await EntregarAsync(Validacion(evaluacion, desastre, EstadoValidacion.VALIDADA, 2));

        async Task<IReadOnlyList<EvaluacionValidada>> CandidatasAsync()
        {
            using IServiceScope ambito = _servicios.CreateScope();
            IReadOnlyList<EvaluacionValidada> copia = await ambito.ServiceProvider.GetRequiredService<ICopiasLocales>().EvaluacionesDeDesastreAsync(desastre);
            return CreacionDeProyectos.Candidatas(copia, new HashSet<Guid>());
        }
        EvaluacionValidada candidata = Assert.Single(await CandidatasAsync());
        Assert.Equal(evaluacion, candidata.Id);
        Assert.Equal("63001", candidata.Municipio); // la capa de municipios aún no llegó

        // Vuelve a pendiente (Evaluación de Daños la cambió); una validación vieja que llega tarde no la revive.
        await EntregarAsync(Validacion(evaluacion, desastre, EstadoValidacion.PENDIENTE, 4));
        await EntregarAsync(Validacion(evaluacion, desastre, EstadoValidacion.VALIDADA, 3));
        Assert.Empty(await CandidatasAsync());

        // Con la capa de SE-2, el municipio se muestra por su nombre.
        await EntregarAsync(Validacion(evaluacion, desastre, EstadoValidacion.VALIDADA, 5));
        await EntregarAsync(S(CatalogoEventos.CapaGeograficaActualizada, CatalogoEventos.AdaptadorSe2, $"capa:MUNICIPIOS:v7:{Guid.NewGuid()}",
            new CapaGeograficaActualizadaV1(CapaGeografica.MUNICIPIOS, 7,
                [new Division("63001", "Armenia", "63", JsonSerializer.SerializeToElement(new { type = "Point", coordinates = new[] { -75.68, 4.53 } }))],
                "DANE", 1, 1), T0));
        Assert.Equal("Armenia", Assert.Single(await CandidatasAsync()).Municipio);
    }

    /// <summary>Un proyecto en ejecución, guardado por el camino normal.</summary>
    private async Task<Guid> ProyectoEnEjecucionAsync(bool iniciar = true)
    {
        Actor autoridad = new("alcaldia.armenia", Rol.Autoridad, Ambito.Local);
        Actor responsable = new("ing.perez", Rol.Responsable);
        ProyectoReconstruccion p = ProyectoReconstruccion.Crear(Guid.NewGuid(), Guid.NewGuid(), "Reconstrucción de Escuela",
            AlcanceTerritorial.Municipal, [new EvaluacionOrigen(Guid.NewGuid(), Guid.NewGuid(), "ESCUELA", NivelDano.Grave, "63001")], autoridad, T0);
        if (iniciar)
        {
            p.AsignarPresupuesto(autoridad, new Dinero(500_000_000), "Fondo", new Cronograma(new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 1)),
                "Asignación", T0);
            p.AsignarResponsable(autoridad, responsable.Usuario, "Asignación", T0);
            p.Aprobar(autoridad, T0);
            p.IniciarEjecucion(responsable, "Constructora Andina", T0);
            p.RegistrarAvance(responsable, 50, null, T0);
        }
        using IServiceScope ambito = _servicios.CreateScope();
        await ambito.ServiceProvider.GetRequiredService<GuardadoDeProyectos>().GuardarAsync(p, default);
        return p.Id;
    }

    private static Sobre Reporte(Guid proyecto, TipoReporteExterno tipo, decimal? avance, string? contratista, int marca) =>
        S(CatalogoEventos.ReporteExternoProyecto, CatalogoEventos.AdaptadorSe7, $"se7:{proyecto}:{marca}",
            new ReporteExternoProyectoV1(proyecto, tipo, avance, contratista, T0.AddHours(marca), $"obra-{marca}", marca.ToString()), T0.AddHours(marca));

    [Fact]
    public async Task El_avance_y_el_contratista_de_SE7_quedan_con_su_procedencia_y_un_valor_menor_es_una_correccion_E04()
    {
        Guid id = await ProyectoEnEjecucionAsync();
        await EntregarAsync(Reporte(id, TipoReporteExterno.AVANCE, 70, null, 1));
        await EntregarAsync(Reporte(id, TipoReporteExterno.AVANCE, 65, null, 2));
        await EntregarAsync(Reporte(id, TipoReporteExterno.CONTRATISTA, null, "Obras del Quindío", 3));

        await using ReconstruccionDbContext db = ComoApp();
        ProyectoFila proyecto = await db.Proyectos.AsNoTracking().SingleAsync(p => p.Id == id);
        Assert.Equal(65, proyecto.Avance);
        Assert.Equal("Obras del Quindío", proyecto.Contratista);
        List<EntradaHistorialFila> externas = await db.Historial.AsNoTracking()
            .Where(h => h.IdProyecto == id && h.Procedencia == "SE-7").OrderBy(h => h.Version).ToListAsync();
        Assert.Equal(["Avance reportado por SE-7", "Corrección reportada por SE-7", "Contratista reportado por SE-7"],
            externas.Select(h => h.Justificacion));
        Assert.Equal(3, await db.Avances.CountAsync(a => a.IdProyecto == id));

        // Los cambios salen como EV-20 con la procedencia del sistema externo.
        List<FilaBandeja> filas = await db.Set<FilaBandeja>().AsNoTracking()
            .Where(f => f.ClaveIdempotencia.StartsWith("proyecto:" + id + ":")).ToListAsync();
        List<Sobre> externos = filas.Select(f => Sobre.Leer(f.Sobre)).Where(s => s.Procedencia.SistemaOrigen == "SE-7").ToList();
        Assert.Equal(3, externos.Count);
        Assert.All(externos, s => { Assert.Equal("EV-20", s.Tipo); ValidadorContrato.Instancia.Validar(s); });
    }

    [Fact]
    public async Task Un_reporte_que_el_proyecto_no_admite_se_registra_como_procesado_sin_bloquear_la_cola()
    {
        Guid formulado = await ProyectoEnEjecucionAsync(iniciar: false);
        Assert.True(await EntregarAsync(Reporte(formulado, TipoReporteExterno.AVANCE, 10, null, 1)));
        Assert.True(await EntregarAsync(Reporte(Guid.NewGuid(), TipoReporteExterno.AVANCE, 10, null, 1)));

        await using ReconstruccionDbContext db = ComoApp();
        Assert.Equal(0, (await db.Proyectos.AsNoTracking().SingleAsync(p => p.Id == formulado)).Avance);
    }

    [Theory]
    [InlineData("DELETE FROM desastres_locales")]
    [InlineData("DELETE FROM evaluaciones_validadas")]
    [InlineData("DELETE FROM mensajes_procesados")]
    [InlineData("UPDATE mensajes_procesados SET tipo = 'x'")]
    public async Task El_servicio_mantiene_las_copias_pero_no_las_borra(string sql)
    {
        await using NpgsqlConnection conexion = new(_base.App);
        await conexion.OpenAsync();
        await using NpgsqlCommand comando = new(sql, conexion);
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }
}
