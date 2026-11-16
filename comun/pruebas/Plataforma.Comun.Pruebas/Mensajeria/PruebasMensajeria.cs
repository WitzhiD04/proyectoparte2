using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Mensajeria;
using Plataforma.Comun.Pruebas.Contrato;

namespace Plataforma.Comun.Pruebas.Mensajeria;

/// <summary>Consumidor de prueba: registra el evento en "efectos". Falla siempre con la clave "falla-siempre".</summary>
public sealed class ManejadorPrueba : IManejadorEvento
{
    private readonly ContextoPrueba _db;
    public ManejadorPrueba(ContextoPrueba db) { _db = db; }

    public async Task ManejarAsync(Sobre sobre, CancellationToken ct)
    {
        if (sobre.ClaveIdempotencia.StartsWith("falla-siempre", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Fallo transitorio simulado.");
        }
        Efecto? efecto = await _db.Efectos.FindAsync([sobre.ClaveIdempotencia], ct);
        if (efecto is null)
        {
            _db.Efectos.Add(new Efecto { Clave = sobre.ClaveIdempotencia, Veces = 1 });
        }
        else
        {
            efecto.Veces++;
        }
    }
}

/// <summary>
/// La mensajería contra un PostgreSQL y un RabbitMQ reales, con la topología de despliegue (DA-03, DA-04, QA-04).
/// Productor: "reconstruccion" publica EV-18. Consumidor: "consulta-programa" lee de q.consulta-programa.
/// </summary>
[Collection(ColeccionBus.Nombre)]
public sealed class PruebasMensajeria : IAsyncLifetime
{
    private const string Cola = "q.consulta-programa";
    private readonly EntornoBus _entorno;

    public PruebasMensajeria(EntornoBus entorno) { _entorno = entorno; }

    public async Task InitializeAsync()
    {
        await _entorno.LimpiarTablasAsync();
        await _entorno.VaciarColasAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task EscribirEventosAsync(int cantidad, string prefijo = "evento")
    {
        using ServiceProvider proveedor = _entorno.Proveedor(_entorno.Opciones("reconstruccion"));
        // En lotes de 100 por transacción, como varias operaciones de usuario.
        for (int inicio = 0; inicio < cantidad; inicio += 100)
        {
            using IServiceScope ambito = proveedor.CreateScope();
            ContextoPrueba db = ambito.ServiceProvider.GetRequiredService<ContextoPrueba>();
            EscritorBandeja<ContextoPrueba> escritor = ambito.ServiceProvider.GetRequiredService<EscritorBandeja<ContextoPrueba>>();
            for (int i = inicio; i < Math.Min(cantidad, inicio + 100); i++)
            {
                escritor.Agregar(EjemplosContrato.Sobre("EV-18", $"{prefijo}:{i}"));
            }
            await db.SaveChangesAsync();
        }
    }

    private RelevoBandeja<ContextoPrueba> Relevo(ServiceProvider proveedor) => new(
        proveedor.GetRequiredService<IServiceScopeFactory>(), proveedor.GetRequiredService<ConexionBus>(),
        proveedor.GetRequiredService<OpcionesBus>(), NullLogger<RelevoBandeja<ContextoPrueba>>.Instance);

    private ConsumidorIdempotente<ContextoPrueba> Consumidor(ServiceProvider proveedor) => new(
        proveedor.GetRequiredService<IServiceScopeFactory>(), proveedor.GetRequiredService<ConexionBus>(),
        proveedor.GetRequiredService<OpcionesBus>(), NullLogger<ConsumidorIdempotente<ContextoPrueba>>.Instance);

    private ServiceProvider ProveedorConsumidor()
    {
        ServiceProvider proveedor = _entorno.Proveedor(_entorno.Opciones("consulta-programa", Cola));
        return proveedor;
    }

    private static ServiceProvider ConManejador(EntornoBus entorno)
    {
        Microsoft.Extensions.DependencyInjection.ServiceCollection servicios = new();
        servicios.AddLogging();
        servicios.AddDbContext<ContextoPrueba>(o => o.UseNpgsql(entorno.CadenaBase));
        servicios.AddSingleton(entorno.Opciones("consulta-programa", Cola));
        servicios.AddSingleton<ConexionBus>();
        servicios.AddScoped<IManejadorEvento, ManejadorPrueba>();
        return servicios.BuildServiceProvider();
    }

    private async Task<int> EfectosAsync()
    {
        await using ContextoPrueba db = _entorno.CrearContexto();
        return await db.Efectos.CountAsync();
    }

    private async Task<int> PendientesAsync()
    {
        await using ContextoPrueba db = _entorno.CrearContexto();
        return await db.Set<FilaBandeja>().CountAsync(f => f.PublicadoEn == null);
    }

    private async Task PublicarTodoAsync(RelevoBandeja<ContextoPrueba> relevo)
    {
        while (await relevo.NumerarAsync(default) > 0) { }
        ResultadoPublicacion resultado;
        do
        {
            resultado = await relevo.PublicarAsync(default);
        } while (resultado.Publicados > 0 && !resultado.Rechazado);
    }

    [Fact]
    public async Task Con_el_consumidor_detenido_no_se_pierde_ningun_evento()
    {
        await EscribirEventosAsync(1000);
        using ServiceProvider productor = _entorno.Proveedor(_entorno.Opciones("reconstruccion"));
        await PublicarTodoAsync(Relevo(productor));
        Assert.Equal(0, await PendientesAsync());

        // El consumidor arranca después: los 1000 eventos esperaban en su cola.
        using ServiceProvider consumidor = ConManejador(_entorno);
        ConsumidorIdempotente<ContextoPrueba> servicio = Consumidor(consumidor);
        await servicio.StartAsync(default);
        await EntornoBus.EsperarAsync(async () => await EfectosAsync() == 1000, "1000 efectos");
        await servicio.StopAsync(default);

        await using ContextoPrueba db = _entorno.CrearContexto();
        Assert.Equal(1000, await db.Efectos.CountAsync(e => e.Veces == 1));
        Assert.Equal(1000, await db.Set<FilaMensajeProcesado>().CountAsync());
    }

    [Fact]
    public async Task Dos_relevos_a_la_vez_numeran_sin_huecos_ni_repetidos()
    {
        await EscribirEventosAsync(600);
        using ServiceProvider productor = _entorno.Proveedor(_entorno.Opciones("reconstruccion"));
        RelevoBandeja<ContextoPrueba> a = Relevo(productor);
        RelevoBandeja<ContextoPrueba> b = Relevo(productor);

        async Task NumerarTodo(RelevoBandeja<ContextoPrueba> relevo)
        {
            while (await relevo.NumerarAsync(default) > 0) { }
        }
        await Task.WhenAll(NumerarTodo(a), NumerarTodo(b));

        await using ContextoPrueba db = _entorno.CrearContexto();
        List<long> secuencias = await db.Set<FilaBandeja>().Select(f => f.SecuenciaProductor!.Value).OrderBy(s => s).ToListAsync();
        Assert.Equal(Enumerable.Range(1, 600).Select(i => (long)i), secuencias);
        Assert.Equal(600, (await db.Set<FilaContadorProductor>().SingleAsync()).Ultimo);
    }

    [Fact]
    public async Task La_secuencia_respeta_el_orden_de_los_hechos_dentro_de_una_transaccion()
    {
        // EF Core no inserta en el orden en que se agregan las filas: la numeración debe seguir el orden de los hechos.
        using ServiceProvider proveedor = _entorno.Proveedor(_entorno.Opciones("reconstruccion"));
        using (IServiceScope ambito = proveedor.CreateScope())
        {
            ContextoPrueba db = ambito.ServiceProvider.GetRequiredService<ContextoPrueba>();
            EscritorBandeja<ContextoPrueba> escritor = ambito.ServiceProvider.GetRequiredService<EscritorBandeja<ContextoPrueba>>();
            for (int i = 0; i < 20; i++)
            {
                escritor.Agregar(EjemplosContrato.Sobre("EV-18", $"orden:{i:D2}"));
            }
            await db.SaveChangesAsync();
        }
        RelevoBandeja<ContextoPrueba> relevo = Relevo(proveedor);
        while (await relevo.NumerarAsync(default) > 0) { }

        await using ContextoPrueba lectura = _entorno.CrearContexto();
        List<string> claves = await lectura.Set<FilaBandeja>().OrderBy(f => f.SecuenciaProductor).Select(f => f.ClaveIdempotencia).ToListAsync();
        Assert.Equal(Enumerable.Range(0, 20).Select(i => $"orden:{i:D2}"), claves);
    }

    [Fact]
    public async Task El_sobre_publicado_lleva_la_secuencia_del_productor()
    {
        await EscribirEventosAsync(3);
        using ServiceProvider productor = _entorno.Proveedor(_entorno.Opciones("reconstruccion"));
        await PublicarTodoAsync(Relevo(productor));

        List<Sobre> recibidos = new();
        ManejadorQueGuarda manejador = new(recibidos);
        using ServiceProvider consumidor = _entorno.Proveedor(_entorno.Opciones("consulta-programa", Cola), manejador);
        ConsumidorIdempotente<ContextoPrueba> servicio = Consumidor(consumidor);
        await servicio.StartAsync(default);
        await EntornoBus.EsperarAsync(() => Task.FromResult(recibidos.Count == 3), "3 sobres");
        await servicio.StopAsync(default);

        Assert.Equal([1L, 2L, 3L], recibidos.Select(s => s.SecuenciaProductor!.Value).OrderBy(s => s));
        Assert.All(recibidos, s => Assert.Equal("reconstruccion", s.Productor));
    }

    [Fact]
    public async Task Un_mensaje_que_no_cumple_el_contrato_va_a_la_cola_de_muertos_sin_bloquear_la_cola()
    {
        await _entorno.PublicarCrudoAsync("ev18.proyecto.creado", "{ esto no es un sobre");
        await EscribirEventosAsync(5);
        using ServiceProvider productor = _entorno.Proveedor(_entorno.Opciones("reconstruccion"));
        await PublicarTodoAsync(Relevo(productor));

        using ServiceProvider consumidor = ConManejador(_entorno);
        ConsumidorIdempotente<ContextoPrueba> servicio = Consumidor(consumidor);
        await servicio.StartAsync(default);
        await EntornoBus.EsperarAsync(async () => await EfectosAsync() == 5, "los 5 válidos se procesan");
        await EntornoBus.EsperarAsync(async () => await _entorno.MensajesEnColaAsync("dlq.consulta-programa") == 1,
            "el inválido en dlq.consulta-programa");
        await servicio.StopAsync(default);
    }

    [Fact]
    public async Task Un_error_que_se_repite_se_aparta_tras_el_limite_de_entregas()
    {
        await EscribirEventosAsync(1, "falla-siempre");
        await EscribirEventosAsync(2, "bueno");
        using ServiceProvider productor = _entorno.Proveedor(_entorno.Opciones("reconstruccion"));
        await PublicarTodoAsync(Relevo(productor));

        using ServiceProvider consumidor = ConManejador(_entorno);
        ConsumidorIdempotente<ContextoPrueba> servicio = Consumidor(consumidor);
        await servicio.StartAsync(default);
        await EntornoBus.EsperarAsync(async () => await EfectosAsync() == 2, "los 2 buenos se procesan");
        await EntornoBus.EsperarAsync(async () => await _entorno.MensajesEnColaAsync("dlq.consulta-programa") == 1,
            "el que falla siempre termina en dlq.consulta-programa", segundos: 90);
        await servicio.StopAsync(default);
    }

    [Fact]
    public async Task Con_la_cola_llena_el_relevo_no_marca_el_evento_y_lo_entrega_despues()
    {
        // Política de prueba de prioridad mayor: la cola admite 10 mensajes (las políticas no se combinan).
        await _entorno.PonerPoliticaAsync("prueba-llena", "^q\\\\.consulta-programa$",
            "{\"max-length\":10,\"overflow\":\"reject-publish\",\"dead-letter-exchange\":\"plataforma.dlx\",\"dead-letter-strategy\":\"at-least-once\"}", 50);
        try
        {
            await Task.Delay(1000); // la política tarda un momento en aplicarse
            await EscribirEventosAsync(15);
            using ServiceProvider productor = _entorno.Proveedor(_entorno.Opciones("reconstruccion"));
            RelevoBandeja<ContextoPrueba> relevo = Relevo(productor);
            while (await relevo.NumerarAsync(default) > 0) { }
            ResultadoPublicacion resultado = await relevo.PublicarAsync(default);

            // En una cola quorum el rechazo empieza cuando la cola ya superó el máximo: acepta 10 u 11.
            Assert.True(resultado.Rechazado);
            Assert.InRange(resultado.Publicados, 10, 11);
            Assert.Equal(15 - resultado.Publicados, await PendientesAsync());
        }
        finally
        {
            await _entorno.QuitarPoliticaAsync("prueba-llena");
        }

        // Con la cola descongestionada el relevo entrega el resto. Ninguno se perdió ni se aplicó dos veces.
        using ServiceProvider consumidor = ConManejador(_entorno);
        ConsumidorIdempotente<ContextoPrueba> servicio = Consumidor(consumidor);
        await servicio.StartAsync(default);
        using ServiceProvider productor2 = _entorno.Proveedor(_entorno.Opciones("reconstruccion"));
        await EntornoBus.EsperarAsync(async () =>
        {
            await PublicarTodoAsync(Relevo(productor2));
            return await PendientesAsync() == 0 && await EfectosAsync() == 15;
        }, "los 15 eventos aplicados una vez");
        await servicio.StopAsync(default);
        await using ContextoPrueba db = _entorno.CrearContexto();
        Assert.Equal(15, await db.Efectos.CountAsync(e => e.Veces == 1));
    }

    [Fact]
    public async Task Con_el_bus_caido_los_eventos_quedan_en_la_bandeja()
    {
        await EscribirEventosAsync(4);
        // Un nodo que no responde: la publicación falla y nada se marca como publicado.
        using ServiceProvider caido = _entorno.Proveedor(_entorno.Opciones("reconstruccion", nodo: "localhost:1"));
        RelevoBandeja<ContextoPrueba> relevoCaido = Relevo(caido);
        await relevoCaido.NumerarAsync(default);
        await Assert.ThrowsAnyAsync<Exception>(() => relevoCaido.PublicarAsync(default));
        Assert.Equal(4, await PendientesAsync());

        // El bus vuelve: los mismos eventos salen, con la numeración que ya tenían.
        using ServiceProvider productor = _entorno.Proveedor(_entorno.Opciones("reconstruccion"));
        await PublicarTodoAsync(Relevo(productor));
        Assert.Equal(0, await PendientesAsync());
    }

    [Fact]
    public async Task El_mismo_evento_dos_veces_tiene_efecto_una_vez()
    {
        using ServiceProvider consumidor = ConManejador(_entorno);
        ConsumidorIdempotente<ContextoPrueba> servicio = Consumidor(consumidor);
        Sobre sobre = EjemplosContrato.Sobre("EV-18", "repetido:1") with { SecuenciaProductor = 1 };

        Assert.True(await servicio.AplicarAsync(sobre, default));
        Assert.False(await servicio.AplicarAsync(sobre, default));

        await using ContextoPrueba db = _entorno.CrearContexto();
        Assert.Equal(1, (await db.Efectos.SingleAsync()).Veces);
    }

    [Fact]
    public async Task El_escritor_no_deja_salir_un_evento_que_no_cumple_el_contrato()
    {
        using ServiceProvider proveedor = _entorno.Proveedor(_entorno.Opciones("reconstruccion"));
        using IServiceScope ambito = proveedor.CreateScope();
        EscritorBandeja<ContextoPrueba> escritor = ambito.ServiceProvider.GetRequiredService<EscritorBandeja<ContextoPrueba>>();
        Sobre invalido = EjemplosContrato.Sobre("EV-18") with { Datos = System.Text.Json.JsonSerializer.SerializeToElement(new { idProyecto = "x" }) };
        Assert.Throws<EventoInvalidoException>(() => escritor.Agregar(invalido));
    }

    private sealed class ManejadorQueGuarda : IManejadorEvento
    {
        private readonly List<Sobre> _recibidos;
        public ManejadorQueGuarda(List<Sobre> recibidos) { _recibidos = recibidos; }

        public Task ManejarAsync(Sobre sobre, CancellationToken ct)
        {
            lock (_recibidos)
            {
                _recibidos.Add(sobre);
            }
            return Task.CompletedTask;
        }
    }
}
