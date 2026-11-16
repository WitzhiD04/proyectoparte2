using System.Text.Json;
using System.Text.Json.Nodes;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Mensajeria;

namespace Plataforma.Comun.Pruebas.Contrato;

/// <summary>El contrato de eventos (AD-09, DT-17): esquemas, sobre canónico y catálogo.</summary>
public class PruebasContrato
{
    private readonly ValidadorContrato _validador = ValidadorContrato.Instancia;

    public static TheoryData<string> EventosEnAlcance()
    {
        TheoryData<string> datos = new();
        foreach (DefinicionEvento e in CatalogoEventos.EnAlcance)
        {
            datos.Add(e.Tipo);
        }
        return datos;
    }

    [Fact]
    public void Cada_evento_en_alcance_tiene_esquema_y_ejemplo()
    {
        foreach (DefinicionEvento evento in CatalogoEventos.EnAlcance)
        {
            Assert.Contains(evento.Esquema, EsquemasEmbebidos.Nombres);
            Assert.True(EjemplosContrato.Datos.ContainsKey(evento.Tipo), $"Falta el ejemplo de {evento.Tipo}");
        }
        // Y ningún esquema sobra: los que están fuera de alcance no tienen esquema todavía (aviso A-11).
        int esquemasDeEventos = EsquemasEmbebidos.Nombres.Count(n => n.StartsWith("EV-", StringComparison.Ordinal));
        Assert.Equal(CatalogoEventos.EnAlcance.Count, esquemasDeEventos);
    }

    [Fact]
    public void Las_claves_de_ruteo_siguen_el_formato_y_no_se_repiten()
    {
        Assert.Equal(29, CatalogoEventos.Todos.Count);
        Assert.Equal(CatalogoEventos.Todos.Count, CatalogoEventos.Todos.Select(e => e.ClaveRuteo).Distinct().Count());
        foreach (DefinicionEvento evento in CatalogoEventos.Todos)
        {
            Assert.Matches(@"^ev[0-9]{2}\.[a-z]+\.[a-z-]+$", evento.ClaveRuteo);
        }
        Assert.Equal("ev18.proyecto.creado", CatalogoEventos.ProyectoCreado.ClaveRuteo);
    }

    [Theory]
    [MemberData(nameof(EventosEnAlcance))]
    public void El_ejemplo_de_cada_evento_cumple_su_esquema(string tipo)
    {
        Sobre sobre = EjemplosContrato.Sobre(tipo);
        _validador.Validar(sobre);
    }

    [Theory]
    [MemberData(nameof(EventosEnAlcance))]
    public void Si_falta_un_campo_obligatorio_el_evento_no_cumple(string tipo)
    {
        Sobre sobre = EjemplosContrato.Sobre(tipo);
        JsonObject datos = JsonNode.Parse(sobre.Datos.GetRawText())!.AsObject();
        string primero = datos.First().Key;
        datos.Remove(primero);
        Sobre incompleto = sobre with { Datos = JsonSerializer.SerializeToElement(datos) };

        EventoInvalidoException error = Assert.Throws<EventoInvalidoException>(() => _validador.Validar(incompleto));
        Assert.Contains(primero, error.Message);
    }

    [Fact]
    public void El_sobre_tiene_los_campos_del_SAD_mas_la_secuencia_y_los_datos()
    {
        JsonObject sobre = JsonNode.Parse(EjemplosContrato.Sobre("EV-18").Serializar())!.AsObject();
        string[] esperados =
        [
            "idEvento", "tipo", "nombre", "version", "instanteEmision", "productor", "secuenciaProductor",
            "idCorrelacion", "claveIdempotencia", "procedencia", "datos",
        ];
        Assert.Equal(esperados, sobre.Select(p => p.Key).ToArray());
        Assert.Equal("EV-18", (string?)sobre["tipo"]);
        Assert.Equal("plataforma", (string?)sobre["procedencia"]!["fuente"]);
    }

    [Fact]
    public void Las_enumeraciones_viajan_como_texto_canonico()
    {
        string json = EjemplosContrato.Sobre("EV-08").Serializar();
        Assert.Contains("\"nivelDano\":\"GRAVE\"", json);
        Assert.Contains("\"estadoValidacion\":\"VALIDADA\"", json);
    }

    [Fact]
    public void Un_valor_que_no_esta_en_la_enumeracion_no_cumple()
    {
        Sobre sobre = EjemplosContrato.Sobre("EV-06");
        JsonObject datos = JsonNode.Parse(sobre.Datos.GetRawText())!.AsObject();
        datos["estadoNuevo"] = "EnReconstruccion";
        Assert.Throws<EventoInvalidoException>(() => _validador.Validar(sobre with { Datos = JsonSerializer.SerializeToElement(datos) }));
    }

    [Fact]
    public void Un_costo_en_EV_20_exige_el_monto_registrado()
    {
        Sobre sobre = EjemplosContrato.Sobre("EV-20");
        JsonObject datos = JsonNode.Parse(sobre.Datos.GetRawText())!.AsObject();
        datos["costoRegistrado"] = null;
        Assert.Throws<EventoInvalidoException>(() => _validador.Validar(sobre with { Datos = JsonSerializer.SerializeToElement(datos) }));
    }

    [Fact]
    public void Un_campo_adicional_se_acepta_porque_la_evolucion_es_aditiva()
    {
        Sobre sobre = EjemplosContrato.Sobre("EV-03");
        JsonObject datos = JsonNode.Parse(sobre.Datos.GetRawText())!.AsObject();
        datos["campoDeUnaVersionFutura"] = "valor";
        _validador.Validar(sobre with { Datos = JsonSerializer.SerializeToElement(datos) });
    }

    [Fact]
    public void Un_tipo_fuera_del_catalogo_o_con_otro_nombre_no_cumple()
    {
        Sobre sobre = EjemplosContrato.Sobre("EV-18");
        Assert.Throws<EventoInvalidoException>(() => _validador.Validar(sobre with { Tipo = "EV-99" }));
        Assert.Throws<EventoInvalidoException>(() => _validador.Validar(sobre with { Nombre = "proyecto.borrado" }));
    }

    [Fact]
    public void En_modo_tolerante_un_tipo_nuevo_pasa_con_el_sobre_valido_y_uno_conocido_se_valida_completo_E34()
    {
        Sobre sobre = EjemplosContrato.Sobre("EV-18");
        Assert.False(_validador.ValidarConocidos(sobre with { Tipo = "EV-99", Nombre = "algo.nuevo" }));
        Assert.False(_validador.ValidarConocidos(sobre with { Version = 2 }));
        Assert.True(_validador.ValidarConocidos(sobre));
        // El sobre se exige siempre, y un tipo conocido con otro nombre sigue sin cumplir.
        Assert.Throws<EventoInvalidoException>(() => _validador.ValidarConocidos(sobre with { Tipo = "EV-99", Productor = "" }));
        Assert.Throws<EventoInvalidoException>(() => _validador.ValidarConocidos(sobre with { Nombre = "proyecto.borrado" }));
    }

    [Fact]
    public void EV_18_lleva_el_autor_y_sigue_aceptando_mensajes_sin_el_E33()
    {
        Sobre sobre = EjemplosContrato.Sobre("EV-18");
        JsonObject datos = JsonNode.Parse(sobre.Datos.GetRawText())!.AsObject();
        datos["autor"] = "ing.perez";
        _validador.Validar(sobre with { Datos = JsonSerializer.SerializeToElement(datos) });
        datos.Remove("autor");
        _validador.Validar(sobre with { Datos = JsonSerializer.SerializeToElement(datos) });
        datos["autor"] = "";
        Assert.Throws<EventoInvalidoException>(() => _validador.Validar(sobre with { Datos = JsonSerializer.SerializeToElement(datos) }));
    }

    [Fact]
    public void El_sobre_se_lee_igual_que_se_escribe()
    {
        Sobre original = EjemplosContrato.Sobre("EV-09") with { SecuenciaProductor = 42 };
        Sobre leido = Sobre.Leer(original.Serializar());
        Assert.Equal(original.IdEvento, leido.IdEvento);
        Assert.Equal(42, leido.SecuenciaProductor);
        Assert.Equal(original.Datos.GetRawText(), leido.Datos.GetRawText());
    }
}
