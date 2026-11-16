using Auditoria.Dominio;
using Xunit;

namespace Auditoria.Pruebas.Dominio;

/// <summary>Detección de huecos en la secuencia de un productor (ATAM R-02, E-18, E-31).</summary>
public class PruebasSeguimientoProductor
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Plazo = TimeSpan.FromSeconds(60);
    private static readonly HashSet<long> Ninguna = [];

    [Fact]
    public void Con_1_2_4_y_el_plazo_vencido_se_reporta_el_hueco_3()
    {
        SeguimientoProductor s = SeguimientoProductor.Nuevo("reconstruccion");

        RevisionSecuencia primera = s.Revisar([1, 2, 4], Ninguna, T0, Plazo);
        HuecoSecuencia hueco = Assert.Single(primera.Detectados);
        Assert.Equal(3, hueco.Secuencia);
        Assert.Empty(primera.Reportados);
        Assert.Equal(2, s.MayorContiguo);
        Assert.Equal(4, s.MayorRecibido);

        // Antes del plazo no se reporta.
        Assert.Empty(s.Revisar([], Ninguna, T0.AddSeconds(59), Plazo).Reportados);
        // Justo en el plazo tampoco: debe persistir "más" que el plazo.
        Assert.Empty(s.Revisar([], Ninguna, T0.AddSeconds(60), Plazo).Reportados);

        RevisionSecuencia vencida = s.Revisar([], Ninguna, T0.AddSeconds(61), Plazo);
        Assert.Equal(3, Assert.Single(vencida.Reportados).Secuencia);
        Assert.Equal(T0.AddSeconds(61), hueco.ReportadoEn);
        Assert.True(hueco.Abierto);

        // Ya reportado: no se vuelve a reportar.
        Assert.Empty(s.Revisar([], Ninguna, T0.AddSeconds(120), Plazo).Reportados);
    }

    [Fact]
    public void Si_el_3_llega_antes_del_plazo_no_se_reporta()
    {
        SeguimientoProductor s = SeguimientoProductor.Nuevo("reconstruccion");
        s.Revisar([1, 2, 4], Ninguna, T0, Plazo);

        RevisionSecuencia llegada = s.Revisar([], new HashSet<long> { 3 }, T0.AddSeconds(30), Plazo);
        HuecoSecuencia cubierto = Assert.Single(llegada.Cubiertos);
        Assert.False(cubierto.Reportado);
        Assert.Empty(s.HuecosAbiertos);
        Assert.Equal(4, s.MayorContiguo);

        Assert.Empty(s.Revisar([5], Ninguna, T0.AddSeconds(200), Plazo).Reportados);
        Assert.Equal(5, s.MayorContiguo);
    }

    [Fact]
    public void Lo_que_llega_fuera_de_orden_dentro_de_una_revision_no_es_un_hueco()
    {
        SeguimientoProductor s = SeguimientoProductor.Nuevo("evaluacion-danos");
        RevisionSecuencia revision = s.Revisar([3, 1, 2, 2], Ninguna, T0, Plazo);
        Assert.Empty(revision.Detectados);
        Assert.Equal(3, s.MayorContiguo);
    }

    [Fact]
    public void Un_hueco_reportado_que_llega_tarde_queda_cubierto_y_conserva_el_reporte()
    {
        SeguimientoProductor s = SeguimientoProductor.Nuevo("gestion-desastres");
        s.Revisar([1, 4], Ninguna, T0, Plazo);
        s.Revisar([], Ninguna, T0.AddMinutes(5), Plazo);

        RevisionSecuencia tarde = s.Revisar([], new HashSet<long> { 2 }, T0.AddMinutes(6), Plazo);
        HuecoSecuencia dos = Assert.Single(tarde.Cubiertos);
        Assert.True(dos.Reportado);
        Assert.False(dos.Abierto);
        Assert.Equal(2, s.MayorContiguo); // el 3 sigue faltando
        Assert.Equal([3L], s.HuecosAbiertos.Select(h => h.Secuencia));
    }

    [Fact]
    public void Un_salto_enorme_se_procesa_por_tramos_sin_perder_numeros()
    {
        SeguimientoProductor s = SeguimientoProductor.Nuevo("adaptador-se7");
        RevisionSecuencia primera = s.Revisar([1, 25], Ninguna, T0, Plazo, maximoHuecosNuevos: 10);
        Assert.Equal(10, primera.Detectados.Count);
        Assert.Equal(11, s.RevisadoHasta);
        Assert.Equal(1, s.MayorRecibido);

        // La siguiente revisión vuelve a leer desde RevisadoHasta y completa el salto.
        RevisionSecuencia segunda = s.Revisar([25], Ninguna, T0.AddSeconds(1), Plazo, maximoHuecosNuevos: 10);
        Assert.Equal(Enumerable.Range(12, 10).Select(i => (long)i), segunda.Detectados.Select(h => h.Secuencia));
        RevisionSecuencia tercera = s.Revisar([25], Ninguna, T0.AddSeconds(2), Plazo, maximoHuecosNuevos: 10);
        Assert.Equal([22L, 23L, 24L], tercera.Detectados.Select(h => h.Secuencia));
        Assert.Equal(25, s.MayorRecibido);
        Assert.Equal(23, s.HuecosAbiertos.Count);
        Assert.Equal(1, s.MayorContiguo);
    }

    [Fact]
    public void Los_numeros_ya_revisados_se_ignoran()
    {
        SeguimientoProductor s = SeguimientoProductor.Nuevo("reconstruccion");
        s.Revisar([1, 2, 3], Ninguna, T0, Plazo);
        RevisionSecuencia repetida = s.Revisar([2, 3], Ninguna, T0.AddSeconds(1), Plazo);
        Assert.Empty(repetida.Detectados);
        Assert.Equal(3, s.MayorContiguo);
    }

    [Fact]
    public void Reconstituido_continua_donde_quedo()
    {
        HuecoSecuencia abierto = HuecoSecuencia.Reconstituir("p", 3, T0, null, null);
        SeguimientoProductor s = SeguimientoProductor.Reconstituir("p", 2, 4, 4, T0, [abierto]);
        RevisionSecuencia revision = s.Revisar([5], Ninguna, T0.AddSeconds(90), Plazo);
        Assert.Empty(revision.Detectados);
        Assert.Same(abierto, Assert.Single(revision.Reportados));
        Assert.Equal(5, s.MayorRecibido);
    }
}

public class PruebasEntradaAuditoria
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);
    private static readonly ProcedenciaDato Plataforma = new("plataforma", null, "ALTA");

    private static EntradaAuditoria Crear(Guid id, long? secuencia = 1) => EntradaAuditoria.Registrar(id, "EV-18",
        "proyecto.creado", 1, "reconstruccion", secuencia, Ahora.AddHours(-5).ToOffset(TimeSpan.FromHours(-5)), Ahora,
        null, "proyecto:1:v1", Interpretacion.Desconocida, Plataforma, "{}");

    [Fact]
    public void Se_registra_en_UTC_y_sin_interpretacion_si_el_tipo_es_desconocido()
    {
        EntradaAuditoria e = Crear(Guid.NewGuid());
        Assert.Equal(TimeSpan.Zero, e.InstanteEmision.Offset);
        Assert.Null(e.Actor);
        Assert.Null(e.Entidad);
        Assert.Null(e.Resultado);
    }

    [Fact]
    public void Un_evento_sin_identificador_o_con_secuencia_invalida_no_se_registra()
    {
        Assert.Equal("EVENTO_SIN_ID", Assert.Throws<ReglaDeNegocioException>(() => Crear(Guid.Empty)).Codigo);
        Assert.Equal("SECUENCIA_INVALIDA", Assert.Throws<ReglaDeNegocioException>(() => Crear(Guid.NewGuid(), 0)).Codigo);
    }

    [Theory]
    [InlineData("auditor", true)]
    [InlineData("autoridad", true)]
    [InlineData("evaluador", false)]
    [InlineData("responsable-proyecto", false)]
    [InlineData("administrador", false)]
    public void Solo_auditor_y_autoridad_leen_el_registro(string rol, bool puede)
    {
        Solicitante s = new("u", new HashSet<string> { rol });
        if (puede)
        {
            PoliticaConsulta.ExigirLectura(s);
        }
        else
        {
            Assert.Throws<NoAutorizadoException>(() => PoliticaConsulta.ExigirLectura(s));
        }
    }
}
