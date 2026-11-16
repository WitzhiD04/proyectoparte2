using EvaluacionDanos.Dominio;

namespace EvaluacionDanos.Pruebas.Dominio;

/// <summary>Reglas que el servicio gana al entrar a la plataforma: copia local de desastres, revisión y catálogo.</summary>
public class ReglasNuevasTests
{
    private static readonly Guid IdDesastre = Guid.NewGuid();

    private static Desastre DesastreConOrganismos()
    {
        var desastre = Desastre.Desconocido(IdDesastre);
        desastre.AplicarCreacion("Sismo del Quindío", "SISMO", EstadoDesastre.EN_EVALUACION, 1);
        desastre.AplicarOrganismos("ungrd", ["defensa-civil", "alcaldia.armenia"], 1);
        return desastre;
    }

    private static Evaluacion Registrada() => new()
    {
        Id = Guid.NewGuid(), ActivoId = Guid.NewGuid(), DesastreId = IdDesastre, Estado = EstadoEvaluacion.REGISTRADA,
        Evaluador = "Ana", Organizacion = "Cruz Roja", TipoActivoCodigo = "vivienda", NivelDano = NivelDano.GRAVE,
    };

    // ---------- Copia local del desastre (E-29): el orden de llegada no importa ----------

    [Fact]
    public void Un_estado_con_version_anterior_no_pisa_al_mas_nuevo()
    {
        var desastre = Desastre.Desconocido(IdDesastre);
        desastre.AplicarEstado(EstadoDesastre.CERRADO, 3);
        Assert.False(desastre.Registrado);

        desastre.AplicarCreacion("Sismo", "SISMO", EstadoDesastre.REGISTRADO, 1);
        desastre.AplicarEstado(EstadoDesastre.EN_ATENCION, 2);

        Assert.True(desastre.Registrado);
        Assert.Equal("Sismo", desastre.Nombre);
        Assert.Equal(EstadoDesastre.CERRADO, desastre.Estado);
        Assert.True(desastre.Cerrado);
        Assert.Equal(3, desastre.VersionDesastre);
    }

    [Fact]
    public void La_zona_y_los_organismos_solo_avanzan_de_version()
    {
        var desastre = DesastreConOrganismos();
        desastre.AplicarZona(2, ["63130", "63001", "63001"]);
        desastre.AplicarZona(1, ["11001"]);
        desastre.AplicarOrganismos("otro", ["x"], 1);

        Assert.Equal(["63001", "63130"], desastre.CodigosMunicipio);
        Assert.Equal("ungrd", desastre.OrganismoResponsable);
        Assert.True(desastre.Participa("ungrd"));          // el responsable también participa
        Assert.True(desastre.Participa(" Defensa-Civil "));
        Assert.False(desastre.Participa("x"));
        Assert.False(desastre.Participa(null));
    }

    // ---------- Validación (RF-ED-08, DA-09) ----------

    [Fact]
    public void Valida_una_organizacion_participante()
    {
        Assert.Null(ReglasRevision.Validacion(Registrada(), DesastreConOrganismos(), false, "defensa-civil", EstadoValidacion.VALIDADA, null));
    }

    [Fact]
    public void No_valida_una_organizacion_que_no_participa()
    {
        var rechazo = ReglasRevision.Validacion(Registrada(), DesastreConOrganismos(), false, "alcaldia.pereira", EstadoValidacion.VALIDADA, null);
        Assert.Equal(TipoRechazo.NoAutorizado, rechazo!.Tipo);
    }

    [Fact]
    public void No_se_valida_con_conflicto_pendiente_ni_preliminar_ni_duplicado()
    {
        var desastre = DesastreConOrganismos();
        var enConflicto = Registrada();
        enConflicto.Estado = EstadoEvaluacion.EN_CONFLICTO;
        var preliminar = Registrada();
        preliminar.DesastreId = null;
        preliminar.Estado = EstadoEvaluacion.PRELIMINAR;
        var duplicado = Registrada();
        duplicado.Estado = EstadoEvaluacion.DUPLICADO;

        Assert.Equal(TipoRechazo.ReglaIncumplida, ReglasRevision.Validacion(Registrada(), desastre, true, "ungrd", EstadoValidacion.VALIDADA, null)!.Tipo);
        Assert.Equal(TipoRechazo.ReglaIncumplida, ReglasRevision.Validacion(enConflicto, desastre, false, "ungrd", EstadoValidacion.VALIDADA, null)!.Tipo);
        Assert.Equal(TipoRechazo.ReglaIncumplida, ReglasRevision.Validacion(preliminar, null, false, "ungrd", EstadoValidacion.VALIDADA, null)!.Tipo);
        Assert.Equal(TipoRechazo.ReglaIncumplida, ReglasRevision.Validacion(duplicado, desastre, false, "ungrd", EstadoValidacion.VALIDADA, null)!.Tipo);
    }

    [Fact]
    public void Rechazar_exige_motivo_y_repetir_el_estado_no_cambia_nada()
    {
        var desastre = DesastreConOrganismos();
        Assert.Equal(TipoRechazo.Invalido, ReglasRevision.Validacion(Registrada(), desastre, false, "ungrd", EstadoValidacion.RECHAZADA, " ")!.Tipo);
        Assert.Null(ReglasRevision.Validacion(Registrada(), desastre, false, "ungrd", EstadoValidacion.RECHAZADA, "No corresponde al activo"));
        // PENDIENTE devuelve a registrada: una que ya está registrada no cambia.
        Assert.Equal(TipoRechazo.ReglaIncumplida, ReglasRevision.Validacion(Registrada(), desastre, false, "ungrd", EstadoValidacion.PENDIENTE, null)!.Tipo);
        Assert.Equal(EstadoEvaluacion.REGISTRADA, ReglasRevision.EstadoTras(EstadoValidacion.PENDIENTE));
    }

    // ---------- Resolución (AD-32, AD-33) ----------

    [Theory]
    [InlineData(TipoConflicto.VERSION, DecisionConflicto.PREVALECE_ENTRANTE, true, true)]
    [InlineData(TipoConflicto.VERSION, DecisionConflicto.PREVALECE_VIGENTE, true, true)]
    [InlineData(TipoConflicto.VERSION, DecisionConflicto.PREVALECE_ENTRANTE, false, true)]
    [InlineData(TipoConflicto.VERSION, DecisionConflicto.PREVALECE_VIGENTE, false, false)]
    [InlineData(TipoConflicto.VERSION, DecisionConflicto.FUSIONAR, true, false)]
    [InlineData(TipoConflicto.VERSION, DecisionConflicto.RECHAZAR_DUPLICADO, true, false)]
    [InlineData(TipoConflicto.DUPLICADO, DecisionConflicto.FUSIONAR, true, true)]
    [InlineData(TipoConflicto.DUPLICADO, DecisionConflicto.RECHAZAR_DUPLICADO, true, true)]
    [InlineData(TipoConflicto.DUPLICADO, DecisionConflicto.PREVALECE_ENTRANTE, true, false)]
    public void Cada_tipo_de_conflicto_admite_solo_sus_decisiones(TipoConflicto tipo, DecisionConflicto decision, bool hayVigente, bool admite)
    {
        var conflicto = new Conflicto { Id = Guid.NewGuid(), Tipo = tipo };
        var rechazo = ReglasRevision.Resolucion(conflicto, decision, hayVigente, "Decisión del operador");
        Assert.Equal(admite, rechazo is null);
    }

    [Fact]
    public void Un_conflicto_resuelto_no_se_decide_otra_vez_y_la_decision_exige_justificacion()
    {
        var resuelto = new Conflicto { Tipo = TipoConflicto.VERSION, Estado = EstadoConflicto.RESUELTO };
        Assert.Equal(TipoRechazo.ReglaIncumplida, ReglasRevision.Resolucion(resuelto, DecisionConflicto.PREVALECE_VIGENTE, true, "x")!.Tipo);
        var pendiente = new Conflicto { Tipo = TipoConflicto.VERSION };
        Assert.Equal(TipoRechazo.Invalido, ReglasRevision.Resolucion(pendiente, DecisionConflicto.PREVALECE_VIGENTE, true, "")!.Tipo);
    }

    // ---------- Catálogo (AD-28) ----------

    [Theory]
    [InlineData("vivienda", true)]
    [InlineData("refugio_temporal_2", true)]
    [InlineData("Vivienda", false)]
    [InlineData("2casas", false)]
    [InlineData("casa-lote", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void El_codigo_del_tipo_va_en_minusculas(string? codigo, bool valido)
    {
        Assert.Equal(valido, CatalogoTipos.CodigoValido(codigo));
    }

    [Fact]
    public void Un_tipo_necesita_un_criterio_por_cada_nivel()
    {
        var completos = Enum.GetNames<NivelDano>().Select(n => ((string?)n, (string?)"criterio")).ToList();
        Assert.Null(CatalogoTipos.ValidarCriterios(completos));
        Assert.NotNull(CatalogoTipos.ValidarCriterios(completos.Take(4).ToList()));
        Assert.NotNull(CatalogoTipos.ValidarCriterios(completos.Take(4).Append(completos[0]).ToList()));   // nivel repetido
        Assert.NotNull(CatalogoTipos.ValidarCriterios(completos.Take(4).Append(("CATASTROFICO", "x")).ToList()));
        Assert.NotNull(CatalogoTipos.ValidarCriterios(completos.Take(4).Append(("DESTRUCCION_TOTAL", " ")).ToList()));
        Assert.NotNull(CatalogoTipos.ValidarCriterios(null));
    }

    // ---------- Versiones hechas en el servidor ----------

    [Fact]
    public void Un_cambio_del_servidor_cuenta_como_otro_dispositivo_para_la_regla_de_conflicto()
    {
        // La regla (AD-43) no cambió: la validación solo agrega una versión del activo con autor del servidor, y por
        // eso una edición hecha sin conexión sobre la base anterior queda en conflicto en vez de sobrescribir.
        var versiones = new[]
        {
            new VersionRegistrada(1, "semilla"), new VersionRegistrada(2, "celular-A"),
            new VersionRegistrada(3, ActivoVersion.AutorServidor("validador.1")),
        };
        Assert.True(ReglaConflicto.Evaluar(2, "celular-A", versiones).HayConflicto);
        Assert.False(ReglaConflicto.Evaluar(3, "celular-A", versiones).HayConflicto);
        Assert.Equal(Limites.IdDispositivo, ActivoVersion.AutorServidor(new string('u', 200)).Length);
    }

    [Fact]
    public void El_contenido_de_una_evaluacion_conserva_sus_atributos()
    {
        var evaluacion = Registrada();
        evaluacion.InstanteCampo = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
        var contenido = evaluacion.ComoContenido();
        Assert.Equal(evaluacion.Id, contenido.Id);
        Assert.Equal("GRAVE", contenido.NivelDano);
        Assert.Equal(evaluacion.InstanteCampo, contenido.InstanteCampo!.Value.UtcDateTime);
        Assert.Equal(EstadoEvaluacion.REGISTRADA, evaluacion.EstadoSinRevision());
    }
}
