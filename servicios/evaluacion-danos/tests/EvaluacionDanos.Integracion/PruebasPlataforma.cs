using System.Net;
using System.Net.Http.Json;
using EvaluacionDanos.Aplicacion;
using EvaluacionDanos.Dominio;
using EvaluacionDanos.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;
using SimuladorQa14;
using static EvaluacionDanos.Integracion.Lotes;
using Canon = Plataforma.Comun.Eventos;
using DecisionConflicto = EvaluacionDanos.Dominio.DecisionConflicto;
using EstadoEvaluacion = EvaluacionDanos.Dominio.EstadoEvaluacion;
using NivelDano = EvaluacionDanos.Dominio.NivelDano;
using TipoConflicto = EvaluacionDanos.Dominio.TipoConflicto;

namespace EvaluacionDanos.Integracion;

/// <summary>
/// Lo que el servicio gana al entrar a la plataforma: dos réplicas, eventos por la bandeja contra el contrato,
/// municipio asignado en el servidor, permisos de la base, revisión humana, catálogo en ejecución y copias locales.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class PruebasPlataforma(Entorno api)
{
    // Puntos dentro de los polígonos (simplificados) de la capa: Armenia 63001 y Calarcá 63130.
    private const double LatArmenia = 4.5339, LonArmenia = -75.6811;
    private const double LatCalarca = 4.5297, LonCalarca = -75.6436;

    // ---------- QA-14 con dos réplicas ----------

    [Fact]
    public async Task QA14_con_dos_replicas_cero_perdidas_cero_sobrescrituras_cero_duplicados()
    {
        // El simulador del prototipo (120 evaluaciones, 5 conflictos, lote enviado dos veces), con cada petición
        // repartida entre dos instancias del servicio sobre la misma base y el mismo almacén de evidencias.
        var salida = new StringWriter();
        var informe = await new Simulador(api.ClienteAlternado, salida).EjecutarAsync();

        Assert.True(informe.Pasa, salida.ToString());
        Assert.Equal(0, informe.Perdidas);
        Assert.Equal(0, informe.SobrescriturasSilenciosas);
        Assert.Equal(0, informe.Duplicadas);
    }

    // ---------- Eventos por la bandeja (AD-45) y municipio (DA-07) ----------

    [Fact]
    public async Task Una_sincronizacion_deja_EV09_en_la_bandeja_y_cumple_el_contrato()
    {
        var op = CrearConActivoNuevo(LatArmenia + 0.01, LonArmenia, tipo: "puente");
        var r = await Uno("ev09-A", op);
        Assert.Equal(ResultadoOperacion.APLICADA, r.Resultado);

        var sobre = await EventoAsync("EV-09", op.Evaluacion!.Id!.Value.ToString());
        ValidadorContrato.Instancia.Validar(sobre);
        Assert.Equal(CatalogoEventos.EvaluacionDanos, sobre.Productor);
        Assert.Null(sobre.SecuenciaProductor);   // la asigna el relevo al publicar
        var datos = sobre.LeerDatos<EvaluacionSincronizadaV1>();
        Assert.Equal("ev09-A", datos.IdDispositivo);
        Assert.Equal("63001", datos.CodigoMunicipio);
        Assert.Equal("PUENTE", datos.TipoActivo);   // código canónico (EV-25)
        Assert.Equal(Canon.EstadoEvaluacion.REGISTRADA, datos.EstadoEvaluacion);
        Assert.False(datos.EnConflicto);
        Assert.Equal(1, datos.VersionesConservadas);
        Assert.Equal(1, datos.VersionEvaluacion);
        Assert.Equal(1, datos.VersionActivo);
        Assert.Equal(Entorno.DesastreInundacion, datos.IdDesastre);

        await using var db = api.NuevoContexto();
        var activo = await db.Activos.SingleAsync(a => a.Id == op.Evaluacion.ActivoId);
        Assert.Equal("63001", activo.CodigoMunicipio);
        Assert.True(activo.VersionCapa > 0);
    }

    [Fact]
    public async Task Un_conflicto_deja_EV10_con_las_dos_versiones_y_cumple_el_contrato()
    {
        var activoId = Entorno.TomarActivoSembrado();
        var deB = Crear(activoId, 1, nivel: "LEVE");
        var deA = Crear(activoId, 1, nivel: "GRAVE");
        await Uno("ev10-B", deB);
        Assert.Equal(ResultadoOperacion.EN_CONFLICTO, (await Uno("ev10-A", deA, replica: 1)).Resultado);

        await using var db = api.NuevoContexto();
        var conflicto = await db.Conflictos.SingleAsync(c => c.EvaluacionEntranteId == deA.Evaluacion!.Id);
        var sobre = await EventoAsync("EV-10", conflicto.Id.ToString());
        ValidadorContrato.Instancia.Validar(sobre);
        var datos = sobre.LeerDatos<ConflictoDetectadoV1>();
        Assert.Equal(activoId, datos.IdActivo);
        Assert.Equal(Canon.TipoConflicto.VERSION, datos.TipoConflicto);
        Assert.Equal(EstadoRevision.PENDIENTE, datos.EstadoRevision);
        Assert.Equal([deB.Evaluacion!.Id!.Value, deA.Evaluacion!.Id!.Value], datos.IdsEvaluaciones);
        Assert.Contains(datos.Versiones, v => v.Vigente && v.IdEvaluacion == deB.Evaluacion.Id);
        Assert.Contains(datos.Versiones, v => !v.Vigente && v.IdEvaluacion == deA.Evaluacion.Id);
        ValidadorContrato.Instancia.Validar(await EventoAsync("EV-09", deA.Evaluacion.Id.Value.ToString()));
    }

    [Theory]
    [InlineData(LatArmenia, LonArmenia, "63001", "hospital")]
    [InlineData(LatCalarca, LonCalarca, "63130", "escuela")]
    public async Task Una_validacion_deja_EV08_con_el_municipio_del_activo(double lat, double lon, string municipio, string tipo)
    {
        var op = CrearConActivoNuevo(lat, lon, tipo);
        Assert.Equal(ResultadoOperacion.APLICADA, (await Uno("ev08-" + municipio, op)).Resultado);
        var id = op.Evaluacion!.Id!.Value;

        // La valida un organismo que participa en el desastre (EV-27), por la otra réplica.
        var respuesta = await Validador(replica: 1).PostAsJsonAsync($"/v1/evaluaciones/{id}/validacion",
            new { estado = "VALIDADA", motivo = "Verificada en sitio" });
        Assert.True(respuesta.IsSuccessStatusCode, await respuesta.Content.ReadAsStringAsync());
        var detalle = await respuesta.Content.ReadFromJsonAsync<EvaluacionDetalleDto>(JsonEvaluacion.Opciones);
        Assert.Equal(EstadoEvaluacion.VALIDADA, detalle!.Estado);
        Assert.Equal("validador.1", detalle.ValidadoPor);
        Assert.Equal(2, detalle.NumeroVersion);

        var sobre = await EventoAsync("EV-08", id.ToString());
        ValidadorContrato.Instancia.Validar(sobre);
        var datos = sobre.LeerDatos<EvaluacionValidadaV1>();
        Assert.Equal(municipio, datos.CodigoMunicipio);
        Assert.Equal("63", datos.CodigoDepartamento);
        Assert.Equal(Canon.EstadoValidacion.VALIDADA, datos.EstadoValidacion);
        Assert.Equal("validador.1", datos.ValidadoPor);
        Assert.Equal(Entorno.DesastreInundacion, datos.IdDesastre);
        Assert.Equal(tipo.ToUpperInvariant(), datos.TipoActivo);
        Assert.Equal(2, datos.Version);
        Assert.Equal("Activo creado en campo", datos.ReferenciaActivo);

        // La versión anterior se conserva (AD-30) y el activo sube de versión con autor del servidor.
        await using var db = api.NuevoContexto();
        Assert.Equal(2, await db.EvaluacionVersiones.CountAsync(v => v.EvaluacionId == id));
        var version = await db.ActivoVersiones.SingleAsync(v => v.ActivoId == op.Evaluacion.ActivoId && v.Numero == 2);
        Assert.Equal("servidor:validador.1", version.IdDispositivo);
    }

    // ---------- Autorización (AD-22) ----------

    [Fact]
    public async Task La_validacion_exige_rol_y_organizacion_participante_y_no_admite_preliminar_ni_conflicto()
    {
        var activoId = Entorno.TomarActivoSembrado();
        var registrada = Crear(activoId, 1);
        var enConflicto = Crear(activoId, 1);
        var preliminar = Crear(Entorno.TomarActivoSembrado(), 1, conDesastre: false);
        await Uno("val-A", registrada);
        await Uno("val-B", enConflicto);
        await Uno("val-A", preliminar);
        var limpia = Crear(Entorno.TomarActivoSembrado(), 1);
        await Uno("val-A", limpia);
        var cuerpo = new { estado = "VALIDADA" };
        string Ruta(Operacion op) => $"/v1/evaluaciones/{op.Evaluacion!.Id}/validacion";

        // Nivel 1: el rol. Un evaluador (la app) no valida.
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Cliente("val-A").PostAsJsonAsync(Ruta(limpia), cuerpo)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().PostAsJsonAsync(Ruta(limpia), cuerpo)).StatusCode);
        // Nivel 2: la organización no participa en el desastre.
        var ajeno = api.ConRol("autoridad", "alcalde.otro", "alcaldia.pereira");
        Assert.Equal(HttpStatusCode.Forbidden, (await ajeno.PostAsJsonAsync(Ruta(limpia), cuerpo)).StatusCode);
        // Rechazar exige motivo.
        Assert.Equal(HttpStatusCode.BadRequest, (await Validador().PostAsJsonAsync(Ruta(limpia), new { estado = "RECHAZADA" })).StatusCode);
        // Reglas: con conflicto pendiente (la vigente y la entrante) y preliminar sin desastre.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Validador().PostAsJsonAsync(Ruta(registrada), cuerpo)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Validador().PostAsJsonAsync(Ruta(enConflicto), cuerpo)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Validador().PostAsJsonAsync(Ruta(preliminar), cuerpo)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Validador().PostAsJsonAsync(Ruta(limpia), new { estado = "APROBADA" })).StatusCode);

        await using var db = api.NuevoContexto();
        Assert.Equal(EstadoEvaluacion.REGISTRADA, (await db.Evaluaciones.SingleAsync(e => e.Id == limpia.Evaluacion!.Id)).Estado);
        Assert.Equal(1, await db.EvaluacionVersiones.CountAsync(v => v.EvaluacionId == limpia.Evaluacion!.Id));
    }

    [Fact]
    public async Task Las_rutas_de_revision_y_catalogo_exigen_su_rol()
    {
        var evaluador = api.Cliente("rol-A");
        Assert.Equal(HttpStatusCode.Forbidden, (await evaluador.GetAsync("/v1/conflictos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await evaluador.GetAsync("/v1/catalogos/tipos-activo")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Operador().GetAsync("/v1/descarga")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await evaluador.PostAsJsonAsync($"/v1/conflictos/{Guid.NewGuid()}/resolucion",
            new { decision = "PREVALECE_VIGENTE", justificacion = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Operador().GetAsync("/v1/conflictos")).StatusCode);
    }

    // ---------- Permisos de la base (DT-02) ----------

    [Theory]
    [InlineData("UPDATE evaluacion_version SET autor = 'otro'")]
    [InlineData("DELETE FROM evaluacion_version")]
    [InlineData("UPDATE activo_version SET id_dispositivo = 'otro'")]
    [InlineData("DELETE FROM activo_version")]
    [InlineData("UPDATE operacion_procesada SET resultado_json = '{}'")]
    [InlineData("DELETE FROM evidencia_vinculada")]
    [InlineData("DELETE FROM evaluacion")]
    [InlineData("DELETE FROM conflicto")]
    [InlineData("CREATE TABLE intrusa (id int)")]
    [InlineData("DROP TABLE activo")]
    public async Task El_rol_de_la_aplicacion_no_puede_reescribir_el_historial_ni_cambiar_el_esquema(string sql)
    {
        // AD-30 y QA-13: la inmutabilidad del historial la garantiza la base, no solo el DbContext (corrige H-14).
        await using NpgsqlConnection conexion = new(api.CadenaApp);
        await conexion.OpenAsync();
        await using NpgsqlCommand comando = new(sql, conexion);
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task El_migrador_es_idempotente_y_siembra_los_11_tipos_una_sola_vez()
    {
        await EvaluacionDanos.Infraestructura.Persistencia.MigracionEvaluacionDanos.EjecutarAsync(api.ConfiguracionMigrador,
            sembrar: true, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        await using var db = api.NuevoContexto();
        var sembrados = EvaluacionDanos.Infraestructura.Persistencia.SiembraCatalogo.Tipos().Select(t => t.Codigo).ToList();
        Assert.Equal(11, sembrados.Count);
        Assert.Equal(11, await db.TiposActivo.CountAsync(t => sembrados.Contains(t.Codigo)));
        Assert.Equal(55, await db.CriteriosNivel.CountAsync(c => sembrados.Contains(c.TipoActivoCodigo)));
        // Un EV-25 por tipo sembrado, que cumple el contrato.
        var sobre = await EventoAsync("EV-25", "tipo-activo:VIVIENDA:v1");
        ValidadorContrato.Instancia.Validar(sobre);
        Assert.Equal(5, sobre.LeerDatos<TipoActivoActualizadoV1>().Criterios.Count);
    }

    // ---------- Revisión humana (AD-32, AD-33, E-14) ----------

    [Fact]
    public async Task Resolver_con_PREVALECE_ENTRANTE_no_borra_nada_y_deja_EV22()
    {
        var activoId = Entorno.TomarActivoSembrado();
        var deB = Crear(activoId, 1, nivel: "LEVE");
        var deA = Crear(activoId, 1, nivel: "DESTRUCCION_TOTAL");
        await Uno("res-B", deB);
        await Uno("res-A", deA);
        Guid idConflicto;
        int versionesAntes;
        await using (var db = api.NuevoContexto())
        {
            idConflicto = (await db.Conflictos.SingleAsync(c => c.EvaluacionEntranteId == deA.Evaluacion!.Id)).Id;
            versionesAntes = await db.EvaluacionVersiones.CountAsync(v => v.EvaluacionId == deA.Evaluacion!.Id || v.EvaluacionId == deB.Evaluacion!.Id);
        }

        var cuerpo = new { decision = "PREVALECE_ENTRANTE", justificacion = "La entrante tiene evidencia más reciente" };
        // Un posible duplicado no se resuelve así, y falta la justificación.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.Operador().PostAsJsonAsync($"/v1/conflictos/{idConflicto}/resolucion",
            new { decision = "FUSIONAR", justificacion = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Operador().PostAsJsonAsync($"/v1/conflictos/{idConflicto}/resolucion",
            new { decision = "PREVALECE_ENTRANTE" })).StatusCode);
        var respuesta = await api.Operador(replica: 1).PostAsJsonAsync($"/v1/conflictos/{idConflicto}/resolucion", cuerpo);
        Assert.True(respuesta.IsSuccessStatusCode, await respuesta.Content.ReadAsStringAsync());
        // Ya está resuelto: no se decide dos veces.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.Operador().PostAsJsonAsync($"/v1/conflictos/{idConflicto}/resolucion", cuerpo)).StatusCode);

        await using (var db = api.NuevoContexto())
        {
            var conflicto = await db.Conflictos.SingleAsync(c => c.Id == idConflicto);
            Assert.Equal(EstadoConflicto.RESUELTO, conflicto.Estado);
            Assert.Equal(DecisionConflicto.PREVALECE_ENTRANTE, conflicto.Decision);
            Assert.Equal("operador.1", conflicto.DecididoPor);
            Assert.Equal(EstadoEvaluacion.REGISTRADA, (await db.Evaluaciones.SingleAsync(e => e.Id == deA.Evaluacion!.Id)).Estado);
            // La que estaba vigente no se toca ni se borra; ninguna versión desaparece y hay una nueva.
            var anterior = await db.Evaluaciones.SingleAsync(e => e.Id == deB.Evaluacion!.Id);
            Assert.Equal(NivelDano.LEVE, anterior.NivelDano);
            Assert.Equal(versionesAntes + 1, await db.EvaluacionVersiones.CountAsync(v => v.EvaluacionId == deA.Evaluacion!.Id || v.EvaluacionId == deB.Evaluacion!.Id));
            Assert.Equal(3, (await db.Activos.SingleAsync(a => a.Id == activoId)).Version);
            var vigente = await db.ActivoVersiones.SingleAsync(v => v.ActivoId == activoId && v.Numero == 3);
            Assert.Equal(deA.Evaluacion!.Id, vigente.EvaluacionId);
            Assert.Equal("res-A", vigente.IdDispositivo);
        }
        var sobre = await EventoAsync("EV-22", idConflicto.ToString());
        ValidadorContrato.Instancia.Validar(sobre);
        var datos = sobre.LeerDatos<ConflictoResueltoV1>();
        Assert.Equal(Canon.DecisionConflicto.PREVALECE_ENTRANTE, datos.Decision);
        Assert.Equal(deA.Evaluacion!.Id, datos.IdEvaluacionVigente);
        Assert.Equal(Canon.NivelDano.DESTRUCCION_TOTAL, datos.Evaluacion.NivelDano);
        Assert.Equal("operador.1", datos.DecididoPor);
    }

    [Fact]
    public async Task Resolver_con_PREVALECE_VIGENTE_deja_rechazada_la_entrante_que_nacio_en_conflicto()
    {
        var activoId = Entorno.TomarActivoSembrado();
        var deB = Crear(activoId, 1, nivel: "LEVE");
        var deA = Crear(activoId, 1, nivel: "GRAVE");
        await Uno("vig-B", deB);
        await Uno("vig-A", deA);
        await using var db = api.NuevoContexto();
        var idConflicto = (await db.Conflictos.SingleAsync(c => c.EvaluacionEntranteId == deA.Evaluacion!.Id)).Id;

        var respuesta = await api.Operador().PostAsJsonAsync($"/v1/conflictos/{idConflicto}/resolucion",
            new { decision = "PREVALECE_VIGENTE", justificacion = "La vigente coincide con la visita de verificación" });
        Assert.True(respuesta.IsSuccessStatusCode, await respuesta.Content.ReadAsStringAsync());

        Assert.Equal(EstadoEvaluacion.RECHAZADA, (await db.Evaluaciones.AsNoTracking().SingleAsync(e => e.Id == deA.Evaluacion!.Id)).Estado);
        Assert.Equal(EstadoEvaluacion.REGISTRADA, (await db.Evaluaciones.AsNoTracking().SingleAsync(e => e.Id == deB.Evaluacion!.Id)).Estado);
        Assert.Equal(2, (await db.Activos.AsNoTracking().SingleAsync(a => a.Id == activoId)).Version);
        Assert.Equal(2, await db.EvaluacionVersiones.CountAsync(v => v.EvaluacionId == deA.Evaluacion!.Id));
        var datos = (await EventoAsync("EV-22", idConflicto.ToString())).LeerDatos<ConflictoResueltoV1>();
        Assert.Equal(deB.Evaluacion!.Id, datos.IdEvaluacionVigente);
        Assert.Equal(2, datos.VersionVigente);
    }

    [Fact]
    public async Task Fusionar_marca_la_descartada_como_duplicado_vincula_sus_evidencias_y_no_borra_nada()
    {
        var (lat, lon) = Entorno.TomarZonaLibre();
        var primero = CrearConActivoNuevo(lat, lon);
        var segundo = CrearConActivoNuevo(lat + 10 / 111_195.0, lon);
        var foto = FotoJpeg();
        segundo.Evaluacion!.Evidencias =
        [
            new EvidenciaDeclarada
            {
                Id = Guid.NewGuid(), Lat = lat, Lon = lon, InstanteCaptura = DateTimeOffset.UtcNow, Sha256 = Sha256(foto), TamanoBytes = foto.Length
            }
        ];
        await Uno("fus-A", primero);
        Assert.Equal(ResultadoOperacion.POSIBLE_DUPLICADO, (await Uno("fus-B", segundo)).Resultado);
        await using var db = api.NuevoContexto();
        var conflicto = await db.Conflictos.AsNoTracking().SingleAsync(c => c.EvaluacionEntranteId == segundo.Evaluacion.Id);
        Assert.Equal(TipoConflicto.DUPLICADO, conflicto.Tipo);

        var respuesta = await api.Operador().PostAsJsonAsync($"/v1/conflictos/{conflicto.Id}/resolucion",
            new { decision = "FUSIONAR", justificacion = "Es la misma vivienda, registrada por dos equipos" });
        Assert.True(respuesta.IsSuccessStatusCode, await respuesta.Content.ReadAsStringAsync());

        var descartada = await db.Evaluaciones.AsNoTracking().SingleAsync(e => e.Id == segundo.Evaluacion.Id);
        Assert.Equal(EstadoEvaluacion.DUPLICADO, descartada.Estado);
        Assert.Equal(primero.Evaluacion!.Id, descartada.DuplicadoDe);
        // Ninguna versión ni evidencia se elimina (E-14): la duplicada conserva las suyas y gana una versión.
        Assert.Equal(2, await db.EvaluacionVersiones.CountAsync(v => v.EvaluacionId == descartada.Id));
        Assert.Equal(1, await db.Evidencias.CountAsync(e => e.EvaluacionId == descartada.Id));
        var vinculo = await db.EvidenciasVinculadas.SingleAsync(v => v.EvaluacionId == primero.Evaluacion.Id);
        Assert.Equal(segundo.Evaluacion.Evidencias[0].Id, vinculo.EvidenciaId);
        Assert.Equal(primero.Evaluacion.ActivoId, (await db.Activos.AsNoTracking().SingleAsync(a => a.Id == segundo.Evaluacion.ActivoId)).FusionadoEn);

        var sobre = await EventoAsync("EV-22", conflicto.Id.ToString());
        ValidadorContrato.Instancia.Validar(sobre);
        var datos = sobre.LeerDatos<ConflictoResueltoV1>();
        Assert.Equal(Canon.DecisionConflicto.FUSIONAR, datos.Decision);
        Assert.Equal(primero.Evaluacion.Id, datos.IdEvaluacionVigente);
        // El activo fusionado deja de ofrecerse en la descarga; el que queda, sigue.
        var descarga = await api.Cliente("fus-A").GetFromJsonAsync<DescargaDto>("/v1/descarga", JsonEvaluacion.Opciones);
        Assert.DoesNotContain(descarga!.Activos, a => a.Id == segundo.Evaluacion.ActivoId);
        Assert.Contains(descarga.Activos, a => a.Id == primero.Evaluacion.ActivoId);
    }

    // ---------- Actualización, asociación y registro con conexión ----------

    [Fact]
    public async Task Asociar_y_actualizar_conservan_versiones_dejan_EV26_y_una_edicion_sin_conexion_posterior_queda_en_conflicto()
    {
        var activoId = Entorno.TomarActivoSembrado();
        var preliminar = Crear(activoId, 1, nivel: "LEVE", conDesastre: false);
        Assert.Equal(EstadoEvaluacion.PRELIMINAR, (await Uno("act-con-A", preliminar)).EstadoEvaluacion);   // activo v2
        var id = preliminar.Evaluacion!.Id!.Value;

        var asociacion = await api.Operador().PostAsJsonAsync($"/v1/evaluaciones/{id}/asociacion", new { desastreId = Entorno.DesastreSismo });
        Assert.True(asociacion.IsSuccessStatusCode, await asociacion.Content.ReadAsStringAsync());   // activo v3
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.Operador().PostAsJsonAsync($"/v1/evaluaciones/{id}/asociacion",
            new { desastreId = Entorno.DesastreSismo })).StatusCode);   // ya no es preliminar
        var asociada = (await EventoAsync("EV-26", $"evaluacion:{id}:v2:actualizada")).LeerDatos<EvaluacionActualizadaV1>();
        Assert.Equal(Canon.MotivoActualizacion.ASOCIACION, asociada.Motivo);
        Assert.Equal(Entorno.DesastreSismo, asociada.Evaluacion.IdDesastre);
        Assert.Equal(Canon.EstadoEvaluacion.REGISTRADA, asociada.Evaluacion.EstadoEvaluacion);

        // Otro dispositivo no puede actualizarla (nivel 2); su dueño sí, por la otra réplica.
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Cliente("act-con-intruso").PatchAsJsonAsync($"/v1/evaluaciones/{id}",
            new { nivelDano = "GRAVE" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Cliente("act-con-A").PatchAsJsonAsync($"/v1/evaluaciones/{id}",
            new { nivelDano = "CATASTROFICO" })).StatusCode);
        var actualizacion = await api.Cliente("act-con-A", replica: 1).PatchAsJsonAsync($"/v1/evaluaciones/{id}",
            new { nivelDano = "GRAVE", descripcion = "Se agrietó el muro de carga" });
        Assert.True(actualizacion.IsSuccessStatusCode, await actualizacion.Content.ReadAsStringAsync());   // activo v4
        var sobre = await EventoAsync("EV-26", $"evaluacion:{id}:v3:actualizada");
        ValidadorContrato.Instancia.Validar(sobre);
        Assert.Equal(Canon.NivelDano.GRAVE, sobre.LeerDatos<EvaluacionActualizadaV1>().Evaluacion.NivelDano);

        await using var db = api.NuevoContexto();
        Assert.Equal(3, await db.EvaluacionVersiones.CountAsync(v => v.EvaluacionId == id));
        Assert.Equal(4, (await db.Activos.SingleAsync(a => a.Id == activoId)).Version);

        // El dueño edita sin conexión sobre la base que tenía (v2): no sobrescribe en silencio lo que cambió el servidor.
        var r = await Uno("act-con-A", Actualizar(preliminar.Evaluacion, 2, "SIN_DANO"));
        Assert.Equal(ResultadoOperacion.EN_CONFLICTO, r.Resultado);
        Assert.Equal(NivelDano.GRAVE, (await db.Evaluaciones.AsNoTracking().SingleAsync(e => e.Id == id)).NivelDano);
    }

    [Fact]
    public async Task El_registro_con_conexion_va_por_el_mismo_camino_y_es_idempotente()
    {
        var op = Crear(Entorno.TomarActivoSembrado(), 1);
        op.IdOperacion = null;   // sin idOperacion, el id de la evaluación identifica el registro
        op.Tipo = null;
        var cliente = api.Cliente("linea-A");

        var primera = await cliente.PostAsJsonAsync("/v1/evaluaciones", op, JsonEvaluacion.Opciones);
        var segunda = await api.Cliente("linea-A", replica: 1).PostAsJsonAsync("/v1/evaluaciones", op, JsonEvaluacion.Opciones);

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(await primera.Content.ReadAsStringAsync(), await segunda.Content.ReadAsStringAsync());
        var resultado = await primera.Content.ReadFromJsonAsync<ResultadoOperacionDto>(JsonEvaluacion.Opciones);
        Assert.Equal(ResultadoOperacion.APLICADA, resultado!.Resultado);
        await using var db = api.NuevoContexto();
        Assert.Equal(1, await db.EvaluacionVersiones.CountAsync(v => v.EvaluacionId == op.Evaluacion!.Id));
        ValidadorContrato.Instancia.Validar(await EventoAsync("EV-09", op.Evaluacion!.Id!.Value.ToString()));

        var invalida = Crear(Guid.NewGuid(), 1);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await cliente.PostAsJsonAsync("/v1/evaluaciones", invalida, JsonEvaluacion.Opciones)).StatusCode);
    }

    // ---------- Catálogo en ejecución (AD-28, QA-10) ----------

    [Fact]
    public async Task Un_tipo_de_activo_nuevo_se_usa_sin_redesplegar_y_deja_EV25()
    {
        var administrador = api.ConRol("administrador", "admin.1");
        var criterios = Enum.GetNames<NivelDano>().Select(n => new { nivel = n, criterio = $"Criterio de {n}" }).ToArray();
        var nuevo = new { codigo = "refugio_temporal", nombre = "Refugio temporal", criterios };

        Assert.Equal(HttpStatusCode.Forbidden, (await api.Cliente("cat-A").PostAsJsonAsync("/v1/catalogos/tipos-activo", nuevo)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrador.PostAsJsonAsync("/v1/catalogos/tipos-activo",
            new { codigo = "Refugio Temporal", nombre = "x", criterios })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrador.PostAsJsonAsync("/v1/catalogos/tipos-activo",
            new { codigo = "refugio_b", nombre = "x", criterios = criterios.Take(4) })).StatusCode);
        var creado = await administrador.PostAsJsonAsync("/v1/catalogos/tipos-activo", nuevo);
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await administrador.PostAsJsonAsync("/v1/catalogos/tipos-activo", nuevo)).StatusCode);

        var sobre = await EventoAsync("EV-25", "tipo-activo:REFUGIO_TEMPORAL:v1");
        ValidadorContrato.Instancia.Validar(sobre);
        Assert.Equal("REFUGIO_TEMPORAL", sobre.LeerDatos<TipoActivoActualizadoV1>().Codigo);

        // La otra réplica ya acepta evaluaciones del tipo nuevo: nada se modificó ni se redesplegó (QA-10).
        var (lat, lon) = Entorno.TomarZonaLibre();
        var r = await Uno("cat-A", CrearConActivoNuevo(lat, lon, tipo: "refugio_temporal"), replica: 1);
        Assert.Equal(ResultadoOperacion.APLICADA, r.Resultado);
        var descarga = await api.Cliente("cat-A").GetFromJsonAsync<DescargaDto>("/v1/descarga", JsonEvaluacion.Opciones);
        Assert.Contains(descarga!.TiposActivo, t => t.Codigo == "refugio_temporal" && t.Criterios.Count == 5);

        var modificado = await administrador.PatchAsJsonAsync("/v1/catalogos/tipos-activo/refugio_temporal", new { nombre = "Albergue temporal" });
        Assert.True(modificado.IsSuccessStatusCode, await modificado.Content.ReadAsStringAsync());
        var tipo = await modificado.Content.ReadFromJsonAsync<TipoActivoDto>(JsonEvaluacion.Opciones);
        Assert.Equal(2, tipo!.Version);
        Assert.Equal("Albergue temporal", (await EventoAsync("EV-25", "tipo-activo:REFUGIO_TEMPORAL:v2")).LeerDatos<TipoActivoActualizadoV1>().Nombre);
        var lista = await administrador.GetFromJsonAsync<List<TipoActivoDto>>("/v1/catalogos/tipos-activo", JsonEvaluacion.Opciones);
        Assert.True(lista!.Count >= 12);
    }

    // ---------- Descarga filtrada e incremental (DA-19) ----------

    [Fact]
    public async Task La_descarga_no_trae_desastres_cerrados_y_filtra_por_zona_y_por_cambios()
    {
        var enArmenia = CrearConActivoNuevo(LatArmenia - 0.02, LonArmenia - 0.02, tipo: "carretera");
        await Uno("desc-A", enArmenia);
        var cliente = api.Cliente("desc-A");
        var activoArmenia = enArmenia.Evaluacion!.ActivoId;

        var todo = await cliente.GetFromJsonAsync<DescargaDto>("/v1/descarga", JsonEvaluacion.Opciones);
        Assert.DoesNotContain(todo!.Desastres, d => d.Id == Entorno.DesastreCerrado);
        Assert.False(todo.Incremental);
        Assert.Equal("63001", todo.Activos.Single(a => a.Id == activoArmenia).CodigoMunicipio);
        Assert.Contains(todo.Activos, a => a.Id == ActivosDePrueba.Id(1));

        // Con desastre: solo ese, y los activos de su delimitación vigente (EV-05: Armenia y Calarcá).
        var delSismo = await cliente.GetFromJsonAsync<DescargaDto>($"/v1/descarga?desastreId={Entorno.DesastreSismo}", JsonEvaluacion.Opciones);
        Assert.Equal(Entorno.DesastreSismo, Assert.Single(delSismo!.Desastres).Id);
        Assert.Equal(["63001", "63130"], delSismo.Desastres[0].CodigosMunicipio);
        Assert.Contains(delSismo.Activos, a => a.Id == activoArmenia);
        Assert.All(delSismo.Activos, a => Assert.Contains(a.CodigoMunicipio, new[] { "63001", "63130" }));
        // Un desastre cerrado no se entrega aunque se pida.
        var cerrado = await cliente.GetFromJsonAsync<DescargaDto>($"/v1/descarga?desastreId={Entorno.DesastreCerrado}", JsonEvaluacion.Opciones);
        Assert.Empty(cerrado!.Desastres);

        // Con zona: municipio o departamento.
        var bogota = await cliente.GetFromJsonAsync<DescargaDto>("/v1/descarga?zona=11", JsonEvaluacion.Opciones);
        Assert.DoesNotContain(bogota!.Activos, a => a.Id == activoArmenia);
        Assert.Contains(bogota.Activos, a => a.Id == ActivosDePrueba.Id(1));
        var armenia = await cliente.GetFromJsonAsync<DescargaDto>("/v1/descarga?zona=63001", JsonEvaluacion.Opciones);
        Assert.All(armenia!.Activos, a => Assert.Equal("63001", a.CodigoMunicipio));
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.GetAsync("/v1/descarga?zona=armenia")).StatusCode);

        // Incremental: desde el instante de la descarga anterior solo llegan los activos que cambiaron después.
        string desde = Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(5).ToString("O"));
        var sinCambios = await cliente.GetFromJsonAsync<DescargaDto>($"/v1/descarga?desde={desde}", JsonEvaluacion.Opciones);
        Assert.True(sinCambios!.Incremental);
        Assert.Empty(sinCambios.Activos);
        Assert.NotEmpty(sinCambios.TiposActivo);
        string antes = Uri.EscapeDataString(todo.InstanteServidor.AddMinutes(-10).ToString("O"));
        var recientes = await cliente.GetFromJsonAsync<DescargaDto>($"/v1/descarga?desde={antes}", JsonEvaluacion.Opciones);
        Assert.Contains(recientes!.Activos, a => a.Id == activoArmenia);
        Assert.DoesNotContain(recientes.Activos, a => a.Id == ActivosDePrueba.Id(ActivosDePrueba.Total));   // sembrado en 2026-11-01, sin cambios
    }

    // ---------- Copias locales por eventos (E-29, E-25, E-11) ----------

    [Fact]
    public async Task Los_eventos_de_desastre_se_aplican_una_vez_y_en_cualquier_orden()
    {
        var id = Guid.NewGuid();
        var cerrado = Entorno.Evento(CatalogoEventos.EstadoDesastreCambiado, $"desastre:{id}:v3",
            new EstadoDesastreCambiadoV1(id, Canon.EstadoDesastre.EN_ATENCION, Canon.EstadoDesastre.CERRADO, "coordinador", 3));
        var zona = Entorno.Evento(CatalogoEventos.ZonaActualizada, $"desastre:{id}:zona:v2",
            new ZonaActualizadaV1(id, 2, null, ["63130"], "SE-2", "coordinador"));
        var zonaVieja = Entorno.Evento(CatalogoEventos.ZonaActualizada, $"desastre:{id}:zona:v1",
            new ZonaActualizadaV1(id, 1, null, ["63001"], "SE-2", "coordinador"));

        // Llegan antes que EV-03 y fuera de orden; uno llega repetido a la otra réplica.
        Assert.True(await api.AplicarEventoAsync(cerrado));
        Assert.False(await api.AplicarEventoAsync(cerrado, replica: 1));
        Assert.True(await api.AplicarEventoAsync(zona));
        Assert.True(await api.AplicarEventoAsync(zonaVieja));
        await using (var db = api.NuevoContexto())
        {
            Assert.False((await db.Desastres.SingleAsync(d => d.Id == id)).Registrado);   // aún no admite evaluaciones
        }
        Assert.True(await api.AplicarEventoAsync(Entorno.DesastreCreado(id, "Avalancha de prueba", "AVALANCHA")));
        Assert.True(await api.AplicarEventoAsync(Entorno.Organismos(id, 2, "ungrd", "cruz-roja")));
        Assert.True(await api.AplicarEventoAsync(Entorno.Organismos(id, 1, "otro", "defensa-civil")));

        await using (var db = api.NuevoContexto())
        {
            var desastre = await db.Desastres.SingleAsync(d => d.Id == id);
            Assert.True(desastre.Registrado);
            Assert.Equal("Avalancha de prueba", desastre.Nombre);
            Assert.Equal(EvaluacionDanos.Dominio.EstadoDesastre.CERRADO, desastre.Estado);   // gana la versión 3, no la 1 de EV-03
            Assert.Equal(["63130"], desastre.CodigosMunicipio);
            Assert.Equal("ungrd", desastre.OrganismoResponsable);
            Assert.True(desastre.Participa("Cruz-Roja"));
            Assert.False(desastre.Participa("defensa-civil"));
        }
    }

    [Fact]
    public async Task La_capa_de_municipios_llega_completa_por_EV21_y_asigna_el_mas_cercano_fuera_de_todo_poligono()
    {
        var sobres = Entorno.SobresDeLaCapa();
        Assert.Equal(33, sobres.Count);
        Assert.All(sobres, ValidadorContrato.Instancia.Validar);
        Assert.False(await api.AplicarEventoAsync(sobres[0], replica: 1));   // ya aplicada: no se repite

        await using var db = api.NuevoContexto();
        Assert.Equal(1122, await db.Divisiones.CountAsync(d => d.Capa == "MUNICIPIOS"));
        Assert.Equal(33, await db.PartesCapa.CountAsync(p => p.Capa == "MUNICIPIOS"));

        // Un punto en el mar Caribe, fuera de todos los polígonos: se asigna el municipio más cercano (DA-07).
        var enElMar = CrearConActivoNuevo(11.5, -74.5, tipo: "otra_infraestructura");
        Assert.Equal(ResultadoOperacion.APLICADA, (await Uno("mar-A", enElMar)).Resultado);
        var activo = await db.Activos.SingleAsync(a => a.Id == enElMar.Evaluacion!.ActivoId);
        Assert.Matches("^(47|08)[0-9]{3}$", activo.CodigoMunicipio);   // Magdalena o Atlántico

        // Con la capa cargada, la réplica declara lista esa dependencia.
        var salud = await api.CreateClient().GetStringAsync("/salud/vivo");
        Assert.Contains("evaluacion-danos", salud);
    }

    // ---------- Apoyo ----------

    private HttpClient Validador(int replica = 0) => api.ConRol("organismo", "validador.1", Entorno.OrganismoParticipante, replica);

    private async Task<ResultadoOperacionDto> Uno(string dispositivo, Operacion op, int replica = 0)
    {
        var respuesta = await Entorno.Sincronizar(api.Cliente(dispositivo, replica: replica), Lote(dispositivo, op));
        return Assert.Single(respuesta.Resultados);
    }

    /// <summary>El único evento de ese tipo en la bandeja cuyo sobre contiene el texto.</summary>
    private async Task<Sobre> EventoAsync(string tipo, string texto)
    {
        await using var db = api.NuevoContexto();
        var sobres = await db.Bandeja.Where(b => b.Tipo == tipo).Select(b => b.Sobre).ToListAsync();
        return Sobre.Leer(Assert.Single(sobres, s => s.Contains(texto)));
    }
}
