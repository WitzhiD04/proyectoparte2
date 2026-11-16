using System.Reflection;
using GestionDesastres.Aplicacion;
using GestionDesastres.Dominio;

namespace GestionDesastres.Pruebas;

internal static class Datos
{
    public static readonly DateTimeOffset Ahora = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
    public static readonly Actor Responsable = new("coordinador.quindio", "ungrd", EsAutoridad: true, EsOrganismo: false, EsAdministrador: false, EsAutoridadNacional: false);
    public static readonly Actor OtroOrganismo = new("bombero.1", "bomberos", false, EsOrganismo: true, false, false);
    public static readonly Actor Nacional = new("presidencia", "autoridad-nacional", EsAutoridad: true, false, false, EsAutoridadNacional: true);
    public static readonly Actor Ciudadano = new("ciudadano", "ninguna", false, false, false, false);

    public static readonly PerfilCicloVida Simplificado = PerfilCicloVida.Simplificado();
    public static readonly PerfilCicloVida Estandar = PerfilCicloVida.Estandar();
    public static readonly PerfilCicloVida Extendido = PerfilCicloVida.Extendido();

    public static TipoDesastre Tipo(string perfil = "ESTANDAR", DateTimeOffset? hasta = null) => new("INUNDACION", "Inundación", perfil,
        new Dictionary<Severidad, string>(),
        new Dictionary<Severidad, IReadOnlyList<Participante>>
        {
            [Severidad.Nivel2] = [new Participante("bomberos", "Rescate"), new Participante("cruz-roja", "Atención")],
        },
        Ahora.AddYears(-1), hasta);

    public static PerfilCicloVida Perfil(string codigo) => codigo switch
    {
        "SIMPLIFICADO" => Simplificado,
        "ESTANDAR" => Estandar,
        _ => Extendido,
    };

    public static Desastre Registrar(Severidad nivel = Severidad.Nivel2, string perfilTipo = "ESTANDAR", Actor? actor = null) =>
        Desastre.Registrar(Guid.NewGuid(), Tipo(perfilTipo), Perfil(perfilTipo), Perfil(PerfilPorNivel.Inicial().MinimoPara(nivel)),
            "Inundación del río Cauca", "Desbordamiento tras lluvias", Ahora.AddHours(-6), new Ubicacion(4.89, -75.88), nivel,
            "ungrd", [], null, ["66400"], FuenteDelimitacion.DecisionAutoridad, actor ?? Responsable, Ahora);
}

public class PruebasCicloDeVida
{
    [Fact]
    public void Los_perfiles_son_subconjuntos_del_cuadro_16_y_ninguno_incluye_la_reapertura()
    {
        foreach (PerfilCicloVida perfil in new[] { Datos.Simplificado, Datos.Estandar, Datos.Extendido })
        {
            Assert.All(perfil.Transiciones, t => Assert.NotNull(TablaTransiciones.Buscar(t.Origen, t.Destino)));
            Assert.DoesNotContain(perfil.Transiciones, t => t.Condicion == CondicionTransicion.Reapertura);
        }
        Assert.Equal(15, TablaTransiciones.Todas.Count);
        Assert.Equal(TablaTransiciones.Todas.Count - 1, Datos.Extendido.Transiciones.Count);
    }

    [Fact]
    public void El_perfil_simplificado_solo_va_de_registrado_a_cerrado()
    {
        TransicionAdmisible unica = Assert.Single(Datos.Simplificado.Transiciones);
        Assert.Equal((EstadoDesastre.Registrado, EstadoDesastre.Cerrado), (unica.Origen, unica.Destino));
    }

    [Fact]
    public void Un_perfil_debe_incluir_registrado_y_cerrado()
    {
        Assert.Throws<ReglaDeNegocioException>(() => new PerfilCicloVida("X", "X", 9, [EstadoDesastre.Registrado]));
    }

    [Fact]
    public void El_perfil_minimo_por_nivel_inicial_sigue_el_cuadro_17()
    {
        PerfilPorNivel inicial = PerfilPorNivel.Inicial();
        Assert.Equal("SIMPLIFICADO", inicial.MinimoPara(Severidad.Nivel1));
        Assert.Equal("ESTANDAR", inicial.MinimoPara(Severidad.Nivel2));
        Assert.Equal("EXTENDIDO", inicial.MinimoPara(Severidad.Nivel3));
        Assert.Equal("EXTENDIDO", inicial.MinimoPara(Severidad.Nivel4));
    }
}

public class PruebasRegistro
{
    [Fact]
    public void Registrar_deja_el_evento_registrado_con_su_zona_y_los_organismos_por_defecto()
    {
        Desastre d = Datos.Registrar();
        Assert.Equal(EstadoDesastre.Registrado, d.Estado);
        Assert.Equal("ESTANDAR", d.CodigoPerfil);
        Assert.Equal(1, d.ZonaVigente.Version);
        Assert.Equal(["66400"], d.ZonaVigente.CodigosMunicipio);
        // RF-GD-12: organismos del tipo para el nivel, más el responsable.
        Assert.Equal(["ungrd", "bomberos", "cruz-roja"], d.Participantes.Select(p => p.Organismo));
        CambioDesastre registro = Assert.Single(d.CambiosNuevos);
        Assert.Equal(TipoCambio.Registro, registro.Tipo);
        Assert.Equal(["ungrd"], d.OrganismosRegistra);
    }

    [Fact]
    public void Un_nivel_alto_sube_el_perfil_del_tipo_desde_el_registro()
    {
        Desastre d = Datos.Registrar(Severidad.Nivel3, perfilTipo: "ESTANDAR");
        Assert.Equal("EXTENDIDO", d.CodigoPerfil);
    }

    [Fact]
    public void Un_tipo_retirado_no_sirve_para_registrar_eventos_nuevos()
    {
        TipoDesastre retirado = Datos.Tipo(hasta: Datos.Ahora.AddDays(-1));
        Assert.Throws<ReglaDeNegocioException>(() => Desastre.Registrar(Guid.NewGuid(), retirado, Datos.Estandar, Datos.Estandar, "x", "y",
            Datos.Ahora, new Ubicacion(4, -75), Severidad.Nivel2, "ungrd", [], null, ["66400"], FuenteDelimitacion.DecisionAutoridad,
            Datos.Responsable, Datos.Ahora));
    }

    [Fact]
    public void Sin_zona_o_sin_rol_no_se_registra()
    {
        Assert.Throws<ReglaDeNegocioException>(() => Desastre.Registrar(Guid.NewGuid(), Datos.Tipo(), Datos.Estandar, Datos.Estandar, "x", "y",
            Datos.Ahora, new Ubicacion(4, -75), Severidad.Nivel2, "ungrd", [], null, [], FuenteDelimitacion.DecisionAutoridad, Datos.Responsable, Datos.Ahora));
        Assert.Throws<NoAutorizadoException>(() => Datos.Registrar(actor: Datos.Ciudadano));
    }
}

public class PruebasOperaciones
{
    [Fact]
    public void Un_organismo_no_responsable_no_modifica_el_desastre_QA11()
    {
        Desastre d = Datos.Registrar();
        d.LimpiarPendientes();
        Assert.Throws<NoAutorizadoException>(() => d.Reclasificar(Severidad.Nivel3, "x", Datos.Estandar, Datos.Extendido, Datos.OtroOrganismo, Datos.Ahora));
        Assert.Throws<NoAutorizadoException>(() => d.Delimitar(null, ["66001"], FuenteDelimitacion.EvaluacionCampo, "x", Datos.OtroOrganismo, Datos.Ahora));
        Assert.Throws<NoAutorizadoException>(() => d.CambiarEstado(EstadoDesastre.EnEvaluacion, null, Datos.Estandar, 0, Datos.OtroOrganismo, Datos.Ahora));
        Assert.Throws<NoAutorizadoException>(() => d.AsignarOrganismos("bomberos", [], "x", Datos.OtroOrganismo, Datos.Ahora));
        Assert.Empty(d.CambiosNuevos);
    }

    [Fact]
    public void Subir_de_nivel_cambia_al_perfil_minimo_y_conserva_el_estado()
    {
        Desastre d = Datos.Registrar(Severidad.Nivel1, perfilTipo: "SIMPLIFICADO");
        Assert.Equal("SIMPLIFICADO", d.CodigoPerfil);
        d.LimpiarPendientes();

        d.Reclasificar(Severidad.Nivel3, "Nuevas evaluaciones", Datos.Simplificado, Datos.Extendido, Datos.Responsable, Datos.Ahora);

        Assert.Equal("EXTENDIDO", d.CodigoPerfil);
        Assert.Equal(EstadoDesastre.Registrado, d.Estado);
        Assert.Equal([TipoCambio.Severidad, TipoCambio.Perfil], d.CambiosNuevos.Select(c => c.Tipo));
        // Cada entrada guarda el estado resultante en ese momento (AD-20).
        Assert.Equal("SIMPLIFICADO", d.CambiosNuevos[0].Resultado.CodigoPerfil);
        Assert.Equal("EXTENDIDO", d.CambiosNuevos[1].Resultado.CodigoPerfil);
    }

    [Fact]
    public void Bajar_de_nivel_no_baja_el_perfil()
    {
        Desastre d = Datos.Registrar(Severidad.Nivel3);
        d.Reclasificar(Severidad.Nivel1, "Se controló", Datos.Extendido, Datos.Simplificado, Datos.Responsable, Datos.Ahora);
        Assert.Equal("EXTENDIDO", d.CodigoPerfil);
    }

    [Fact]
    public void El_perfil_decide_las_transiciones()
    {
        Desastre simplificado = Datos.Registrar(Severidad.Nivel1, perfilTipo: "SIMPLIFICADO");
        Assert.Throws<ReglaDeNegocioException>(() =>
            simplificado.CambiarEstado(EstadoDesastre.EnEvaluacion, null, Datos.Simplificado, 0, Datos.Responsable, Datos.Ahora));

        Desastre estandar = Datos.Registrar();
        estandar.CambiarEstado(EstadoDesastre.EnEvaluacion, null, Datos.Estandar, 0, Datos.Responsable, Datos.Ahora);
        Assert.Equal(EstadoDesastre.EnEvaluacion, estandar.Estado);
        // El perfil Estándar no tiene En reconstrucción.
        estandar.CambiarEstado(EstadoDesastre.EnRecuperacion, null, Datos.Estandar, 0, Datos.Responsable, Datos.Ahora);
        Assert.Throws<ReglaDeNegocioException>(() =>
            estandar.CambiarEstado(EstadoDesastre.EnReconstruccion, null, Datos.Estandar, 0, Datos.Responsable, Datos.Ahora));
    }

    [Fact]
    public void Cerrar_desde_reconstruccion_exige_proyectos_en_estado_final_y_justificacion()
    {
        Desastre d = Datos.Registrar(Severidad.Nivel3);
        foreach (EstadoDesastre e in new[] { EstadoDesastre.EnAtencion, EstadoDesastre.EnRecuperacion, EstadoDesastre.EnReconstruccion })
        {
            d.CambiarEstado(e, null, Datos.Extendido, 0, Datos.Responsable, Datos.Ahora);
        }

        ReglaDeNegocioException abiertos = Assert.Throws<ReglaDeNegocioException>(() =>
            d.CambiarEstado(EstadoDesastre.Cerrado, "Fin", Datos.Extendido, proyectosNoFinales: 2, Datos.Responsable, Datos.Ahora));
        Assert.Equal("PROYECTOS_ABIERTOS", abiertos.Codigo);
        Assert.Throws<ReglaDeNegocioException>(() => d.CambiarEstado(EstadoDesastre.Cerrado, null, Datos.Extendido, 0, Datos.Responsable, Datos.Ahora));

        d.CambiarEstado(EstadoDesastre.Cerrado, "Todos los proyectos terminaron", Datos.Extendido, 0, Datos.Responsable, Datos.Ahora);
        Assert.Equal(EstadoDesastre.Cerrado, d.Estado);
        Assert.Throws<ReglaDeNegocioException>(() => d.Delimitar(null, ["66400"], FuenteDelimitacion.SistemaGis, "x", Datos.Responsable, Datos.Ahora));
    }

    [Fact]
    public void Solo_la_autoridad_nacional_reabre_un_desastre_cerrado()
    {
        Desastre d = Datos.Registrar(Severidad.Nivel1, perfilTipo: "SIMPLIFICADO");
        d.CambiarEstado(EstadoDesastre.Cerrado, "Falsa alarma", Datos.Simplificado, 0, Datos.Responsable, Datos.Ahora);

        Assert.Throws<NoAutorizadoException>(() => d.CambiarEstado(EstadoDesastre.EnAtencion, "x", Datos.Simplificado, 0, Datos.Responsable, Datos.Ahora));
        Assert.Throws<ReglaDeNegocioException>(() => d.CambiarEstado(EstadoDesastre.EnAtencion, null, Datos.Simplificado, 0, Datos.Nacional, Datos.Ahora));
        d.CambiarEstado(EstadoDesastre.EnAtencion, "Réplica del sismo", Datos.Simplificado, 0, Datos.Nacional, Datos.Ahora);
        Assert.Equal(EstadoDesastre.EnAtencion, d.Estado);
    }

    [Fact]
    public void Una_delimitacion_nueva_sucede_a_la_anterior_sin_borrarla()
    {
        Desastre d = Datos.Registrar();
        d.Delimitar(null, ["66400", "66001"], FuenteDelimitacion.EvaluacionCampo, "Se extendió a Pereira", Datos.Responsable, Datos.Ahora.AddHours(4));
        Assert.Equal([1, 2], d.Zonas.Select(z => z.Version));
        Assert.Equal(["66001", "66400"], d.ZonaVigente.CodigosMunicipio);
        Assert.Equal(["66400"], d.Zonas[0].CodigosMunicipio);
        Assert.Throws<ReglaDeNegocioException>(() => d.Delimitar(null, [], FuenteDelimitacion.EvaluacionCampo, "x", Datos.Responsable, Datos.Ahora));
    }
}

/// <summary>Regla de dependencias (AD-08): el dominio no depende de nada y la aplicación solo del dominio.</summary>
public class PruebasArquitectura
{
    private static readonly string[] Prohibidos =
    [
        "Plataforma.Comun", "Microsoft.EntityFrameworkCore", "Npgsql", "NetTopologySuite", "RabbitMQ", "Microsoft.AspNetCore",
        "GestionDesastres.Infraestructura", "GestionDesastres.Api", "System.Text.Json",
    ];

    private static IEnumerable<string> Referencias(Assembly a) => a.GetReferencedAssemblies().Select(r => r.Name ?? "");

    [Fact]
    public void El_dominio_no_depende_de_otros_proyectos_ni_de_paquetes()
    {
        Assembly dominio = typeof(Desastre).Assembly;
        Assert.DoesNotContain(Referencias(dominio), r => r.StartsWith("GestionDesastres.", StringComparison.Ordinal));
        Assert.DoesNotContain(Referencias(dominio), r => Prohibidos.Any(p => r.StartsWith(p, StringComparison.Ordinal)));
    }

    [Fact]
    public void La_aplicacion_solo_depende_del_dominio()
    {
        Assembly aplicacion = typeof(HechoDesastre).Assembly;
        Assert.Equal(["GestionDesastres.Dominio"], Referencias(aplicacion).Where(r => r.StartsWith("GestionDesastres.", StringComparison.Ordinal)));
        Assert.DoesNotContain(Referencias(aplicacion), r => Prohibidos.Any(p => r.StartsWith(p, StringComparison.Ordinal)));
    }
}
