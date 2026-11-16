using Auditoria.Aplicacion;
using Auditoria.Aplicacion.CasosDeUso;
using Auditoria.Dominio;
using Xunit;

namespace Auditoria.Pruebas.CasosDeUso;

public class PruebasCasosDeUso
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);
    private static readonly Solicitante Auditor = new("contraloria", new HashSet<string> { "auditor" });
    private static readonly Solicitante Evaluador = new("ana.perez", new HashSet<string> { "evaluador" });

    private static ComandoRegistrarEvento Comando(string productor, long secuencia, Guid? id = null, string? idProyecto = null) => new(
        id ?? Guid.NewGuid(), "EV-19", "proyecto.presupuesto-cambiado", 1, productor, secuencia, T0.AddSeconds(secuencia),
        "corr-1", $"proyecto:{idProyecto}:v{secuencia}",
        new Interpretacion("alcaldia.armenia", "proyecto", idProyecto ?? "p1", "CAMBIO_PRESUPUESTO", ResultadoOperacion.Exitoso,
            "100", "200"),
        new ProcedenciaDato("plataforma", null, "ALTA"), "{\"tipo\":\"EV-19\"}");

    [Fact]
    public async Task Registrar_el_mismo_evento_dos_veces_deja_una_sola_entrada()
    {
        RegistroEnMemoria registro = new();
        RegistrarEvento casoDeUso = new(registro, new RelojFijo(T0.AddSeconds(30)));
        ComandoRegistrarEvento comando = Comando("reconstruccion", 1);

        Assert.True(await casoDeUso.EjecutarAsync(comando));
        // El mismo evento con otra clave de idempotencia (por ejemplo, republicado por otro relevo): sigue siendo uno.
        Assert.False(await casoDeUso.EjecutarAsync(comando with { ClaveIdempotencia = "otra" }));

        EntradaAuditoria entrada = Assert.Single(registro.Entradas);
        Assert.Equal(T0.AddSeconds(30), entrada.InstanteRegistro);
        Assert.Equal("100", entrada.ValorAnterior);
        Assert.Equal("200", entrada.ValorNuevo);
    }

    [Fact]
    public async Task La_consulta_exige_rol_auditor_o_autoridad()
    {
        RegistroEnMemoria registro = new();
        ConsultarRegistro casoDeUso = new(registro);
        SalidaEnMemoria<PaginaRegistroDto> salida = new();
        await casoDeUso.EjecutarAsync(new SolicitudConsultaRegistro(Evaluador, new FiltroRegistro()), salida);
        Assert.NotNull(salida.NoAutorizadoMensaje);
        Assert.Null(salida.Respuesta);
    }

    [Fact]
    public async Task La_consulta_filtra_pagina_y_ordena_cronologicamente()
    {
        RegistroEnMemoria registro = new();
        RegistrarEvento registrar = new(registro, new RelojFijo(T0));
        // Llegan desordenados; la consulta los devuelve por instante de emisión.
        foreach (long s in new long[] { 3, 1, 2 })
        {
            await registrar.EjecutarAsync(Comando("reconstruccion", s, idProyecto: "p1"));
        }
        await registrar.EjecutarAsync(Comando("reconstruccion", 4, idProyecto: "p2"));

        ConsultarRegistro consultar = new(registro);
        SalidaEnMemoria<PaginaRegistroDto> salida = new();
        await consultar.EjecutarAsync(new SolicitudConsultaRegistro(Auditor,
            new FiltroRegistro(Entidad: "proyecto", IdEntidad: "p1"), Pagina: 1, Tamano: 2), salida);

        PaginaRegistroDto pagina = Assert.IsType<PaginaRegistroDto>(salida.Respuesta);
        Assert.Equal(3, pagina.Total);
        Assert.Equal([1L, 2L], pagina.Entradas.Select(e => e.SecuenciaProductor!.Value));
        Assert.Equal("EXITOSO", pagina.Entradas[0].Resultado);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 5000)]
    public async Task Parametros_de_pagina_invalidos_se_rechazan(int pagina, int tamano)
    {
        SalidaEnMemoria<PaginaRegistroDto> salida = new();
        await new ConsultarRegistro(new RegistroEnMemoria()).EjecutarAsync(
            new SolicitudConsultaRegistro(Auditor, new FiltroRegistro(), pagina, tamano), salida);
        Assert.Equal("CONSULTA_INVALIDA", salida.Codigo);
    }

    [Fact]
    public async Task Un_rango_de_fechas_invertido_se_rechaza()
    {
        SalidaEnMemoria<PaginaRegistroDto> salida = new();
        await new ConsultarRegistro(new RegistroEnMemoria()).EjecutarAsync(
            new SolicitudConsultaRegistro(Auditor, new FiltroRegistro(Desde: T0, Hasta: T0.AddDays(-1))), salida);
        Assert.Equal("CONSULTA_INVALIDA", salida.Codigo);
    }

    [Fact]
    public async Task La_revision_reporta_el_hueco_3_solo_al_vencer_el_plazo_y_avisa_una_vez()
    {
        RegistroEnMemoria registro = new();
        RegistrarEvento registrar = new(registro, new RelojFijo(T0));
        foreach (long s in new long[] { 1, 2, 4 })
        {
            await registrar.EjecutarAsync(Comando("reconstruccion", s));
        }
        // Otro productor sin huecos: su secuencia es independiente.
        foreach (long s in new long[] { 1, 2 })
        {
            await registrar.EjecutarAsync(Comando("gestion-desastres", s));
        }

        SeguimientosEnMemoria seguimientos = new();
        AvisosEnMemoria avisos = new();
        RelojFijo reloj = new(T0);
        OpcionesHuecos opciones = new(TimeSpan.FromSeconds(60));
        RevisarSecuencias revisar = new(registro, seguimientos, avisos, reloj, opciones);

        ResumenRevision primera = await revisar.EjecutarAsync();
        Assert.Equal(new ResumenRevision(2, 1, 0, 0), primera);
        Assert.Empty(avisos.Reportados);

        reloj.Avanzar(TimeSpan.FromSeconds(61));
        ResumenRevision segunda = await revisar.EjecutarAsync();
        Assert.Equal(1, segunda.Reportados);
        HuecoSecuencia hueco = Assert.Single(avisos.Reportados);
        Assert.Equal(("reconstruccion", 3L), (hueco.Productor, hueco.Secuencia));

        reloj.Avanzar(TimeSpan.FromSeconds(61));
        await revisar.EjecutarAsync();
        Assert.Single(avisos.Reportados);

        SalidaEnMemoria<HuecosDto> salida = new();
        await new ConsultarHuecos(seguimientos, opciones).EjecutarAsync(new SolicitudHuecos(Auditor), salida);
        HuecosDto huecos = Assert.IsType<HuecosDto>(salida.Respuesta);
        ProductorDto reconstruccion = Assert.Single(huecos.Productores, p => p.Productor == "reconstruccion");
        Assert.Equal(2, reconstruccion.MayorContiguo);
        Assert.Equal(4, reconstruccion.MayorRecibido);
        Assert.Equal([3L], reconstruccion.Faltantes);
        Assert.Equal("ABIERTO", Assert.Single(huecos.Reportados).Estado);
        Assert.Empty(Assert.Single(huecos.Productores, p => p.Productor == "gestion-desastres").Faltantes);
    }

    [Fact]
    public async Task Si_el_3_llega_antes_del_plazo_la_revision_no_lo_reporta()
    {
        RegistroEnMemoria registro = new();
        RegistrarEvento registrar = new(registro, new RelojFijo(T0));
        foreach (long s in new long[] { 1, 2, 4 })
        {
            await registrar.EjecutarAsync(Comando("reconstruccion", s));
        }
        SeguimientosEnMemoria seguimientos = new();
        AvisosEnMemoria avisos = new();
        RelojFijo reloj = new(T0);
        RevisarSecuencias revisar = new(registro, seguimientos, avisos, reloj, new OpcionesHuecos(TimeSpan.FromSeconds(60)));

        await revisar.EjecutarAsync();
        reloj.Avanzar(TimeSpan.FromSeconds(20));
        await registrar.EjecutarAsync(Comando("reconstruccion", 3));
        Assert.Equal(1, (await revisar.EjecutarAsync()).Cubiertos);

        reloj.Avanzar(TimeSpan.FromMinutes(10));
        await revisar.EjecutarAsync();
        Assert.Empty(avisos.Reportados);

        SalidaEnMemoria<HuecosDto> salida = new();
        await new ConsultarHuecos(seguimientos, OpcionesHuecos.PorOmision).EjecutarAsync(new SolicitudHuecos(Auditor), salida);
        Assert.Empty(salida.Respuesta!.Reportados);
        Assert.Equal(4, Assert.Single(salida.Respuesta.Productores).MayorContiguo);
    }

    [Fact]
    public async Task Los_huecos_tambien_exigen_rol()
    {
        SalidaEnMemoria<HuecosDto> salida = new();
        await new ConsultarHuecos(new SeguimientosEnMemoria(), OpcionesHuecos.PorOmision)
            .EjecutarAsync(new SolicitudHuecos(Evaluador), salida);
        Assert.NotNull(salida.NoAutorizadoMensaje);
    }
}
