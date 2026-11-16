using System.Text.Json;
using Auditoria.Aplicacion;
using Auditoria.Dominio;
using Auditoria.Infraestructura.Mensajeria;
using Auditoria.Infraestructura.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Mensajeria;
using Plataforma.Comun.Pruebas.Contrato;

namespace Auditoria.Integracion;

/// <summary>
/// El adaptador de entrada lee quién, sobre qué, qué hizo, con qué resultado y los valores anterior y nuevo de cada
/// evento del catálogo (QA-13, QA-15 a), y registra igual los tipos que no conoce (QA-09).
/// </summary>
public class PruebasInterpretacion : IClassFixture<BaseMigrada>
{
    private readonly BaseMigrada _base;
    public PruebasInterpretacion(BaseMigrada baseMigrada) { _base = baseMigrada; }

    public static TheoryData<string> TiposEnAlcance()
    {
        TheoryData<string> tipos = new();
        foreach (DefinicionEvento d in CatalogoEventos.EnAlcance)
        {
            tipos.Add(d.Tipo);
        }
        return tipos;
    }

    [Theory]
    [MemberData(nameof(TiposEnAlcance))]
    public void Cada_tipo_en_alcance_se_interpreta(string tipo)
    {
        Sobre sobre = EjemplosContrato.Sobre(tipo) with { SecuenciaProductor = 7 };
        ValidadorContrato.Instancia.Validar(sobre);

        ComandoRegistrarEvento comando = ManejadorEventosAuditoria.Traducir(sobre);

        Assert.NotNull(comando.Interpretacion.Entidad);
        Assert.NotNull(comando.Interpretacion.IdEntidad);
        Assert.NotNull(comando.Interpretacion.Accion);
        Assert.Equal(ResultadoOperacion.Exitoso, comando.Interpretacion.Resultado);
        // EV-18 no trae el autor de la creación (reportado como pregunta). Los demás sí traen quién.
        if (tipo != "EV-18")
        {
            Assert.False(string.IsNullOrWhiteSpace(comando.Interpretacion.Actor));
        }
        Assert.Equal(7, comando.SecuenciaProductor);
        Assert.Equal(sobre.IdEvento, JsonDocument.Parse(comando.Sobre).RootElement.GetProperty("idEvento").GetGuid());
    }

    [Fact]
    public void Los_cambios_de_presupuesto_y_de_estado_traen_valor_anterior_y_nuevo()
    {
        // QA-15 (a): el 100 % de los cambios de presupuesto y de estado produce una entrada con valor anterior y nuevo.
        Interpretacion presupuesto = InterpretacionEventos.Interpretar(EjemplosContrato.Sobre("EV-19"));
        Assert.Equal(("alcaldia.armenia", "proyecto", "CAMBIO_PRESUPUESTO"), (presupuesto.Actor, presupuesto.Entidad, presupuesto.Accion));
        Assert.Null(presupuesto.ValorAnterior); // la primera asignación no tenía presupuesto
        Assert.Equal("850000000", presupuesto.ValorNuevo);

        Interpretacion costo = InterpretacionEventos.Interpretar(EjemplosContrato.Sobre("EV-20"));
        Assert.Equal(("ing.perez", "CAMBIO_COSTO", "100000000", "120000000"),
            (costo.Actor, costo.Accion, costo.ValorAnterior, costo.ValorNuevo));

        Interpretacion estado = InterpretacionEventos.Interpretar(EjemplosContrato.Sobre("EV-06"));
        Assert.Equal(("desastre", "CAMBIO_ESTADO", "EN_RECUPERACION", "EN_RECONSTRUCCION"),
            (estado.Entidad, estado.Accion, estado.ValorAnterior, estado.ValorNuevo));

        Interpretacion severidad = InterpretacionEventos.Interpretar(EjemplosContrato.Sobre("EV-04"));
        Assert.Equal(("NIVEL_3", "NIVEL_4"), (severidad.ValorAnterior, severidad.ValorNuevo));
    }

    [Fact]
    public void Un_tipo_o_una_version_que_no_conoce_se_registra_sin_interpretar()
    {
        Sobre conocido = EjemplosContrato.Sobre("EV-18");
        Sobre nuevoTipo = conocido with { Tipo = "EV-99", Nombre = "gateway.autorizacion-rechazada" };
        Sobre nuevaVersion = conocido with { Version = 2 };

        foreach (Sobre sobre in new[] { nuevoTipo, nuevaVersion })
        {
            ComandoRegistrarEvento comando = ManejadorEventosAuditoria.Traducir(sobre);
            Assert.Equal(Interpretacion.Desconocida, comando.Interpretacion);
            Assert.Equal(sobre.Tipo, comando.Tipo);
            Assert.Contains(sobre.IdEvento.ToString(), comando.Sobre);
        }
    }

    [Fact]
    public async Task El_consumidor_registra_cada_evento_una_vez_por_clave_y_por_identificador()
    {
        await using ServiceProvider servicios = _base.Proveedor(new RelojDePrueba(DateTimeOffset.UtcNow));
        ConsumidorIdempotente<AuditoriaDbContext> consumidor = servicios.GetRequiredService<ConsumidorIdempotente<AuditoriaDbContext>>();
        Sobre sobre = EjemplosContrato.Sobre("EV-20", "proyecto:" + Guid.NewGuid() + ":v8") with { SecuenciaProductor = 8 };

        Assert.True(await consumidor.AplicarAsync(sobre, CancellationToken.None));
        // Reentrega del bus: la misma clave se descarta en el consumidor (DA-03).
        Assert.False(await consumidor.AplicarAsync(sobre, CancellationToken.None));
        // El mismo evento con otra clave: lo descarta Auditoría por su identificador.
        Assert.True(await consumidor.AplicarAsync(sobre with { ClaveIdempotencia = "otra:" + Guid.NewGuid() }, CancellationToken.None));

        await using AuditoriaDbContext db = _base.ComoApp();
        FilaRegistro fila = await db.Registro.SingleAsync(r => r.IdEvento == sobre.IdEvento);
        Assert.Equal(("ing.perez", "proyecto", "CAMBIO_COSTO", "Exitoso"), (fila.Actor, fila.Entidad, fila.Accion, fila.Resultado));
        Assert.Equal(8, fila.SecuenciaProductor);
        Assert.Equal("EV-20", JsonDocument.Parse(fila.Sobre).RootElement.GetProperty("tipo").GetString());
    }
}
