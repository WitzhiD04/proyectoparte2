using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionDesastres.Infraestructura.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;

namespace GestionDesastres.Integracion;

public class PruebasServicio : IClassFixture<Entorno>
{
    private readonly Entorno _e;
    public PruebasServicio(Entorno entorno) { _e = entorno; }

    // Polígono pequeño dentro de Armenia (centro de la ciudad).
    private static readonly object PoligonoArmenia = new
    {
        type = "Polygon",
        coordinates = new[] { new[] { new[] { -75.69, 4.53 }, new[] { -75.67, 4.53 }, new[] { -75.67, 4.55 }, new[] { -75.69, 4.55 }, new[] { -75.69, 4.53 } } },
    };

    private static object Registro(string tipo = "TERREMOTO", string severidad = "NIVEL_3", object? zona = null) => new
    {
        tipo, nombre = "Sismo del Eje Cafetero", descripcion = "Sismo con daño estructural", instanteInicio = DateTimeOffset.UtcNow.AddHours(-2),
        lat = 4.5339, lon = -75.6811, severidad, responsable = "ungrd", participantes = Array.Empty<object>(),
        zona = zona ?? new { geometria = PoligonoArmenia, codigosMunicipio = Array.Empty<string>(), fuente = "DECISION_AUTORIDAD" },
    };

    private async Task<JsonElement> RegistrarAsync(object cuerpo)
    {
        HttpResponseMessage r = await _e.Cliente.SendAsync(_e.Peticion(HttpMethod.Post, "/v1/desastres", cuerpo));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpResponseMessage> TransicionAsync(Guid id, string estado, string? justificacion = null, string organizacion = "ungrd") =>
        await _e.Cliente.SendAsync(_e.Peticion(HttpMethod.Post, $"/v1/desastres/{id}/transiciones", new { estado, justificacion }, organizacion));

    [Fact]
    public async Task El_catalogo_inicial_trae_los_tipos_de_RN1_y_sus_perfiles()
    {
        JsonElement tipos = await (await _e.Cliente.SendAsync(_e.Peticion(HttpMethod.Get, "/v1/catalogos/tipos-desastre"))).Content.ReadFromJsonAsync<JsonElement>();
        string[] codigos = tipos.EnumerateArray().Select(t => t.GetProperty("codigo").GetString()!).ToArray();
        Assert.Equal(["DESLIZAMIENTO", "ERUPCION_VOLCANICA", "HURACAN", "INCENDIO", "INUNDACION", "OTRO", "TERREMOTO"], codigos.Order());

        JsonElement perfil = await (await _e.Cliente.SendAsync(_e.Peticion(HttpMethod.Get, "/v1/catalogos/tipos-desastre/TERREMOTO/ciclo-vida")))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("EXTENDIDO", perfil.GetProperty("codigo").GetString());
        Assert.Equal(14, perfil.GetProperty("transiciones").GetArrayLength());
    }

    [Fact]
    public async Task Un_tipo_nuevo_se_usa_sin_redesplegar_QA10()
    {
        object tipo = new { codigo = "TSUNAMI", nombre = "Tsunami", perfil = "EXTENDIDO", criteriosPorNivel = new Dictionary<string, string> { ["NIVEL_4"] = "Olas > 3 m" } };
        HttpResponseMessage sinPermiso = await _e.Cliente.SendAsync(_e.Peticion(HttpMethod.Post, "/v1/catalogos/tipos-desastre", tipo));
        Assert.Equal(HttpStatusCode.Forbidden, sinPermiso.StatusCode);
        HttpResponseMessage creado = await _e.Cliente.SendAsync(_e.Peticion(HttpMethod.Post, "/v1/catalogos/tipos-desastre", tipo, roles: "administrador"));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);

        JsonElement desastre = await RegistrarAsync(Registro("TSUNAMI", "NIVEL_4"));
        Assert.Equal("TSUNAMI", desastre.GetProperty("tipo").GetString());
    }

    [Fact]
    public async Task Una_zona_por_poligono_conoce_sus_municipios_y_una_por_municipios_su_geometria()
    {
        JsonElement porPoligono = await RegistrarAsync(Registro());
        Assert.Contains("63001", porPoligono.GetProperty("zonaVigente").GetProperty("codigosMunicipio").EnumerateArray().Select(c => c.GetString()));

        JsonElement porMunicipios = await RegistrarAsync(Registro("INUNDACION", "NIVEL_2",
            new { codigosMunicipio = new[] { "66400", "66001" }, fuente = "SISTEMA_GIS" }));
        JsonElement geometria = porMunicipios.GetProperty("zonaVigente").GetProperty("geometria");
        Assert.Equal(JsonValueKind.String, geometria.ValueKind);
        Assert.Contains("Polygon", geometria.GetString());

        HttpResponseMessage desconocido = await _e.Cliente.SendAsync(_e.Peticion(HttpMethod.Post, "/v1/desastres",
            Registro("INUNDACION", "NIVEL_2", new { codigosMunicipio = new[] { "99999" } })));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, desconocido.StatusCode);
    }

    [Fact]
    public async Task Un_organismo_no_responsable_recibe_403_en_cada_ruta_de_modificacion_QA11()
    {
        Guid id = (await RegistrarAsync(Registro())).GetProperty("id").GetGuid();
        (HttpMethod, string, object)[] rutas =
        [
            (HttpMethod.Patch, $"/v1/desastres/{id}/severidad", new { severidad = "NIVEL_4", justificacion = "x" }),
            (HttpMethod.Put, $"/v1/desastres/{id}/zona", new { codigosMunicipio = new[] { "63130" }, justificacion = "x" }),
            (HttpMethod.Post, $"/v1/desastres/{id}/transiciones", new { estado = "EN_ATENCION" }),
            (HttpMethod.Put, $"/v1/desastres/{id}/organismos", new { responsable = "bomberos", participantes = Array.Empty<object>(), justificacion = "x" }),
        ];
        foreach ((HttpMethod metodo, string ruta, object cuerpo) in rutas)
        {
            HttpResponseMessage r = await _e.Cliente.SendAsync(_e.Peticion(metodo, ruta, cuerpo, organizacion: "bomberos", roles: "organismo"));
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        HttpResponseMessage sinIdentidad = await _e.Cliente.PatchAsJsonAsync($"/v1/desastres/{id}/severidad", new { severidad = "NIVEL_4", justificacion = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, sinIdentidad.StatusCode);
    }

    [Fact]
    public async Task El_cierre_espera_a_que_todos_los_proyectos_esten_en_estado_final()
    {
        Guid id = (await RegistrarAsync(Registro())).GetProperty("id").GetGuid();
        foreach (string estado in new[] { "EN_ATENCION", "EN_RECUPERACION", "EN_RECONSTRUCCION" })
        {
            Assert.Equal(HttpStatusCode.OK, (await TransicionAsync(id, estado)).StatusCode);
        }

        // Llega EV-18: Reconstrucción creó un proyecto para este desastre.
        Guid proyecto = Guid.NewGuid();
        ResumenProyecto resumen = new(id, "Reconstrucción de Escuela", Plataforma.Comun.Eventos.EstadoProyecto.FORMULADO, null, 0, 0, 1);
        await AplicarAsync(Sobre.Crear(CatalogoEventos.ProyectoCreado, "reconstruccion", $"proyecto:{proyecto}:v1",
            new ProyectoCreadoV1(proyecto, [Guid.NewGuid()], id, null, null, null, null, ["63001"], AlcanceProyecto.MUNICIPAL, resumen.Nombre, resumen),
            DateTimeOffset.UtcNow));

        HttpResponseMessage abierto = await TransicionAsync(id, "CERRADO", "Fin de la reconstrucción");
        Assert.Equal(HttpStatusCode.Conflict, abierto.StatusCode);
        Assert.Contains("PROYECTOS_ABIERTOS", await abierto.Content.ReadAsStringAsync());

        // Llega EV-20: el proyecto quedó finalizado.
        ResumenProyecto final = resumen with { Estado = Plataforma.Comun.Eventos.EstadoProyecto.FINALIZADO, Avance = 100, Version = 9 };
        await AplicarAsync(Sobre.Crear(CatalogoEventos.EstadoOAvanceCambiado, "reconstruccion", $"proyecto:{proyecto}:v9",
            new EstadoOAvanceCambiadoV1(proyecto, TipoCambioProyecto.ESTADO, Plataforma.Comun.Eventos.EstadoProyecto.EN_EJECUCION,
                Plataforma.Comun.Eventos.EstadoProyecto.FINALIZADO, 100, 0, null, "EnEjecucion", "Finalizado", 9, "ing.perez", "Obra terminada", final),
            DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.OK, (await TransicionAsync(id, "CERRADO", "Fin de la reconstrucción")).StatusCode);
    }

    [Fact]
    public async Task Los_eventos_salen_por_la_bandeja_con_el_contrato_y_llegan_al_bus()
    {
        Guid id = (await RegistrarAsync(Registro())).GetProperty("id").GetGuid();
        await _e.Cliente.SendAsync(_e.Peticion(HttpMethod.Patch, $"/v1/desastres/{id}/severidad", new { severidad = "NIVEL_4", justificacion = "Réplicas" }));

        await using GestionDesastresDbContext db = _e.ContextoApp();
        List<FilaBandeja> filas = await db.Set<FilaBandeja>().AsNoTracking().Where(f => f.ClaveIdempotencia.StartsWith("desastre:" + id + ":")).ToListAsync();
        Assert.Equal(["EV-03", "EV-04", "EV-05", "EV-27"], filas.Select(f => f.Tipo).Order());
        foreach (FilaBandeja fila in filas)
        {
            ValidadorContrato.Instancia.Validar(Sobre.Leer(fila.Sobre));
        }
        // El relevo del servicio los publica en el bus (la cola de Consulta y la de Auditoría los reciben).
        await Entorno.EsperarAsync(async () =>
        {
            await using GestionDesastresDbContext lectura = _e.ContextoApp();
            return await lectura.Set<FilaBandeja>().Where(f => f.ClaveIdempotencia.StartsWith("desastre:" + id + ":")).AllAsync(f => f.PublicadoEn != null);
        }, "eventos publicados");
    }

    [Theory]
    [InlineData("UPDATE historial SET autor = 'otro'")]
    [InlineData("DELETE FROM historial")]
    [InlineData("UPDATE zonas SET autor = 'otro'")]
    [InlineData("DELETE FROM zonas")]
    [InlineData("UPDATE desastres SET nombre = 'otro'")]
    [InlineData("DELETE FROM desastres")]
    [InlineData("CREATE TABLE intrusa (id int)")]
    public async Task El_historial_y_las_zonas_son_de_solo_insercion(string sql)
    {
        await using NpgsqlConnection conexion = new(_e.App);
        await conexion.OpenAsync();
        await using NpgsqlCommand comando = new(sql, conexion);
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    /// <summary>Entrega un sobre al adaptador de entrada como lo haría el consumidor del bus, en su transacción idempotente.</summary>
    private async Task AplicarAsync(Sobre sobre)
    {
        using IServiceScope ambito = _e.Servicios.CreateScope();
        GestionDesastresDbContext db = ambito.ServiceProvider.GetRequiredService<GestionDesastresDbContext>();
        IManejadorEvento manejador = ambito.ServiceProvider.GetRequiredService<IManejadorEvento>();
        await using var transaccion = await db.Database.BeginTransactionAsync();
        await manejador.ManejarAsync(sobre with { SecuenciaProductor = 1 }, default);
        await db.SaveChangesAsync();
        await transaccion.CommitAsync();
    }
}
