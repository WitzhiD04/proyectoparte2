using Microsoft.Extensions.Logging.Abstractions;
using Plataforma.Comun.Adaptadores;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Mensajeria;
using SimSe7;

namespace AdaptadorSe7.Pruebas;

/// <summary>SE-7 de prueba para la entrada: el registro real del simulador, consultado como lo hace el adaptador.</summary>
public sealed class EntradaSobreSimulador(RegistroSe7 registro, int tamanoPagina) : IEntradaAdaptador<CambioObraSe7>
{
    public int Consultas { get; private set; }
    public string SistemaExterno => "SE-7";

    public Task<LoteExterno<CambioObraSe7>> RecuperarDesde(string? marcaAvance, CancellationToken ct)
    {
        Consultas++;
        PaginaCambios pagina = registro.CambiosDesde(marcaAvance is null ? 0 : long.Parse(marcaAvance), tamanoPagina);
        return Task.FromResult(new LoteExterno<CambioObraSe7>(
            pagina.Cambios.Select(c => new CambioObraSe7(c.Secuencia, c.CodigoObra, c.Clase, c.PorcentajeAvance, c.Contratista, c.OcurridoEn)).ToList(),
            pagina.Marca, pagina.HayMas));
    }

    public Sobre? Traducir(CambioObraSe7 externo) => TraduccionSe7.AEvento(externo, Ejemplos.Instante);
}

/// <summary>La marca y la bandeja en memoria, con la misma regla que la de EF: confirma solo si la marca no cambió.</summary>
public sealed class AlmacenEnMemoria : IAlmacenEntrada
{
    public string? Marca { get; set; }
    public List<Sobre> Bandeja { get; } = [];
    /// <summary>Simula una caída del proceso antes de confirmar la página número n (1, 2, ...).</summary>
    public int? FallarAntesDeConfirmar { get; set; }
    private int _confirmaciones;

    public Task<string?> LeerMarcaAsync(string flujo, CancellationToken ct) => Task.FromResult(Marca);

    public Task<bool> ConfirmarAsync(string flujo, string? anterior, string nueva, int cambios, IReadOnlyList<Sobre> eventos, CancellationToken ct)
    {
        if (++_confirmaciones == FallarAntesDeConfirmar)
        {
            throw new InvalidOperationException("Caída simulada antes de confirmar.");
        }
        if (Marca != anterior)
        {
            return Task.FromResult(false);
        }
        Bandeja.AddRange(eventos.Where(e => Bandeja.All(b => b.ClaveIdempotencia != e.ClaveIdempotencia)));
        Marca = nueva;
        return Task.FromResult(true);
    }
}

public class PruebasMarcaDeAvance
{
    private static RegistroSe7 ConCambios(int avances)
    {
        RegistroSe7 registro = new();
        for (int i = 1; i <= avances; i++)
        {
            registro.AgregarCambio(Ejemplos.Proyecto.ToString(), "AVANCE", i, null);
        }
        return registro;
    }

    private static OpcionesEntrada Opciones(int paginas = 20) => new() { Flujo = "se7.cambios", MaximoPaginasPorRonda = paginas };

    [Fact]
    public async Task Una_ronda_recorre_las_paginas_y_deja_la_marca_en_el_ultimo_cambio()
    {
        RegistroSe7 registro = ConCambios(7);
        AlmacenEnMemoria almacen = new();
        EntradaSobreSimulador se7 = new(registro, tamanoPagina: 3);

        ResultadoRonda ronda = await new CicloEntrada<CambioObraSe7>(se7, almacen, Opciones()).EjecutarAsync(CancellationToken.None);

        Assert.Equal(7, ronda.Cambios);
        Assert.Equal(7, ronda.Eventos);
        Assert.Equal(3, ronda.Paginas);
        Assert.Equal("7", almacen.Marca);
        Assert.Equal(Enumerable.Range(1, 7).Select(i => $"se7:avance:{i}"), almacen.Bandeja.Select(s => s.ClaveIdempotencia));
        Assert.All(almacen.Bandeja, ValidadorContrato.Instancia.Validar);

        // Sin cambios nuevos, la siguiente ronda no publica nada y la marca no se mueve.
        ResultadoRonda vacia = await new CicloEntrada<CambioObraSe7>(se7, almacen, Opciones()).EjecutarAsync(CancellationToken.None);
        Assert.Equal(0, vacia.Cambios);
        Assert.Equal("7", almacen.Marca);

        // Un cambio nuevo en SE-7: se recupera desde la marca, solo él.
        registro.AgregarCambio(Ejemplos.Proyecto.ToString(), "CONTRATISTA", null, "Constructora Andina");
        await new CicloEntrada<CambioObraSe7>(se7, almacen, Opciones()).EjecutarAsync(CancellationToken.None);
        Assert.Equal(8, almacen.Bandeja.Count);
        Assert.Equal("se7:contratista:8", almacen.Bandeja[^1].ClaveIdempotencia);
        Assert.Equal("8", almacen.Marca);
    }

    [Fact]
    public async Task Si_el_proceso_cae_antes_de_confirmar_la_pagina_se_pide_otra_vez_y_no_se_duplica()
    {
        RegistroSe7 registro = ConCambios(6);
        AlmacenEnMemoria almacen = new() { FallarAntesDeConfirmar = 2 };
        EntradaSobreSimulador se7 = new(registro, tamanoPagina: 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CicloEntrada<CambioObraSe7>(se7, almacen, Opciones()).EjecutarAsync(CancellationToken.None));
        Assert.Equal("2", almacen.Marca);              // la primera página quedó confirmada
        Assert.Equal(2, almacen.Bandeja.Count);

        // Reinicio: otra instancia del ciclo, desde la marca guardada.
        await new CicloEntrada<CambioObraSe7>(se7, almacen, Opciones()).EjecutarAsync(CancellationToken.None);
        Assert.Equal("6", almacen.Marca);
        Assert.Equal(Enumerable.Range(1, 6).Select(i => $"se7:avance:{i}"), almacen.Bandeja.Select(s => s.ClaveIdempotencia));
    }

    [Fact]
    public async Task Si_otra_instancia_confirmo_la_pagina_la_ronda_se_detiene_sin_escribir()
    {
        RegistroSe7 registro = ConCambios(3);
        AlmacenEnMemoria almacen = new();
        EntradaSobreSimulador se7 = new(registro, tamanoPagina: 10);

        // La otra instancia avanzó la marca entre la lectura y la confirmación.
        EntradaQueAdelantaLaMarca adelanta = new(se7, almacen);
        ResultadoRonda ronda = await new CicloEntrada<CambioObraSe7>(adelanta, almacen, Opciones()).EjecutarAsync(CancellationToken.None);

        Assert.True(ronda.Conflicto);
        Assert.Empty(almacen.Bandeja);
        Assert.Equal("3", almacen.Marca);
    }

    private sealed class EntradaQueAdelantaLaMarca(EntradaSobreSimulador interna, AlmacenEnMemoria almacen) : IEntradaAdaptador<CambioObraSe7>
    {
        public string SistemaExterno => interna.SistemaExterno;
        public async Task<LoteExterno<CambioObraSe7>> RecuperarDesde(string? marcaAvance, CancellationToken ct)
        {
            LoteExterno<CambioObraSe7> lote = await interna.RecuperarDesde(marcaAvance, ct);
            almacen.Marca = lote.MarcaSiguiente;
            return lote;
        }
        public Sobre? Traducir(CambioObraSe7 externo) => interna.Traducir(externo);
    }

    [Fact]
    public async Task Un_cambio_sin_evento_mueve_la_marca_igual()
    {
        RegistroSe7 registro = new();
        registro.AgregarCambio("OBRA-PROPIA-1", "AVANCE", 10, null);       // obra que no es de la plataforma
        registro.AgregarCambio(Ejemplos.Proyecto.ToString(), "AVANCE", 20, null);
        AlmacenEnMemoria almacen = new();

        ResultadoRonda ronda = await new CicloEntrada<CambioObraSe7>(new EntradaSobreSimulador(registro, 10), almacen, Opciones())
            .EjecutarAsync(CancellationToken.None);

        Assert.Equal(2, ronda.Cambios);
        Assert.Equal(1, ronda.Eventos);
        Assert.Equal("2", almacen.Marca);
    }

    [Fact]
    public async Task La_ronda_tiene_un_limite_de_paginas()
    {
        AlmacenEnMemoria almacen = new();
        await new CicloEntrada<CambioObraSe7>(new EntradaSobreSimulador(ConCambios(10), 2), almacen, Opciones(paginas: 2))
            .EjecutarAsync(CancellationToken.None);
        Assert.Equal("4", almacen.Marca);   // el resto, en la siguiente ronda
    }
}

/// <summary>SE-7 de prueba para la salida.</summary>
public sealed class SalidaFalsa : ISalidaAdaptador
{
    public string SistemaExterno => "SE-7";
    public Queue<Exception?> Respuestas { get; } = new();
    public int Llamadas { get; private set; }

    public Task<ResultadoEntrega> Entregar(Sobre eventoCanonico, CancellationToken ct)
    {
        Llamadas++;
        Exception? error = Respuestas.Count > 0 ? Respuestas.Dequeue() : null;
        return error is null ? Task.FromResult(ResultadoEntrega.Entregado) : Task.FromException<ResultadoEntrega>(error);
    }
}

public class PruebasSalida
{
    private static ManejadorSalida Manejador(SalidaFalsa salida, MonitorCircuito monitor) =>
        new(salida, monitor, new OpcionesSalida { EsperaReintentoSegundos = 0.05 }, NullLogger<ManejadorSalida>.Instance);

    [Fact]
    public async Task Con_SE7_caido_el_manejador_no_lanza_reintenta_hasta_entregar()
    {
        SalidaFalsa salida = new();
        salida.Respuestas.Enqueue(new SistemaExternoNoDisponibleException("503"));
        salida.Respuestas.Enqueue(new Polly.Timeout.TimeoutRejectedException());
        salida.Respuestas.Enqueue(new HttpRequestException("conexión rechazada"));

        // Si lanzara, el consumidor de comun lo reencolaría y, tras 5 entregas, lo mandaría a la DLQ.
        await Manejador(salida, new MonitorCircuito("SE-7")).ManejarAsync(Ejemplos.ProyectoCreado(), CancellationToken.None);

        Assert.Equal(4, salida.Llamadas);
    }

    [Fact]
    public async Task Con_el_circuito_abierto_espera_hasta_que_la_compuerta_lo_cancela()
    {
        SalidaFalsa salida = new();
        for (int i = 0; i < 1000; i++)
        {
            salida.Respuestas.Enqueue(new Polly.CircuitBreaker.BrokenCircuitException());
        }
        MonitorCircuito monitor = new("SE-7");
        monitor.AlAbrirse();
        using CancellationTokenSource compuerta = new(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Manejador(salida, monitor).ManejarAsync(Ejemplos.ProyectoCreado(), compuerta.Token));
    }

    [Fact]
    public async Task Un_rechazo_de_SE7_es_permanente_y_va_a_la_cola_de_mensajes_muertos()
    {
        SalidaFalsa salida = new();
        salida.Respuestas.Enqueue(new EntregaRechazadaException("422"));

        await Assert.ThrowsAsync<EventoInvalidoException>(() =>
            Manejador(salida, new MonitorCircuito("SE-7")).ManejarAsync(Ejemplos.ProyectoCreado(), CancellationToken.None));
        Assert.Equal(1, salida.Llamadas);
    }

    [Fact]
    public async Task El_monitor_avisa_el_cambio_y_no_lo_pierde_si_ya_ocurrio()
    {
        MonitorCircuito monitor = new("SE-7");
        Task espera = monitor.EsperarCambioAsync(EstadoIntegracion.CERRADO, Timeout.InfiniteTimeSpan, CancellationToken.None);
        Assert.False(espera.IsCompleted);
        monitor.AlAbrirse();
        await espera.WaitAsync(TimeSpan.FromSeconds(5));

        // Ya abierto: quien esperaba un cambio desde CERRADO vuelve enseguida.
        await monitor.EsperarCambioAsync(EstadoIntegracion.CERRADO, Timeout.InfiniteTimeSpan, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        monitor.AlSemiabrirse();
        monitor.AlAbrirse();    // la prueba falló: no hay transición nueva
        monitor.AlCerrarse();
        List<EstadoIntegracion> nuevos = [];
        while (monitor.Transiciones.TryRead(out TransicionCircuito? t))
        {
            nuevos.Add(t.Nuevo);
        }
        Assert.Equal([EstadoIntegracion.ABIERTO, EstadoIntegracion.CERRADO], nuevos);
    }
}

public class PruebasSimulador
{
    private static VersionObra Version(string situacion = Situaciones.Aprobada) =>
        new("ALTA", "ALTA", "Escuela", Ejemplos.Desastre.ToString(), situacion, 0, 1m, 0);

    [Fact]
    public void Una_version_repetida_no_tiene_efecto()
    {
        RegistroSe7 registro = new();
        Assert.True(registro.RegistrarVersion("obra-1", 1, Version()));
        Assert.False(registro.RegistrarVersion("obra-1", 1, Version()));
        Assert.True(registro.RegistrarVersion("obra-1", 2, Version(Situaciones.EnObra)));

        EstadoObra obra = Assert.Single(registro.Estado().Obras);
        Assert.Equal([1, 2], obra.Versiones);
        Assert.Equal(1, obra.EntregasRepetidas);
        Assert.Equal(Situaciones.EnObra, obra.Situacion);
    }

    [Fact]
    public void Una_version_vieja_que_llega_tarde_no_cambia_la_situacion()
    {
        RegistroSe7 registro = new();
        registro.RegistrarVersion("obra-1", 3, Version(Situaciones.EnObra));
        registro.RegistrarVersion("obra-1", 1, Version(Situaciones.Formulada));
        Assert.Equal(Situaciones.EnObra, Assert.Single(registro.Estado().Obras).Situacion);
    }

    [Fact]
    public void La_consulta_por_marca_es_idempotente_y_paginada()
    {
        RegistroSe7 registro = new();
        for (int i = 1; i <= 5; i++)
        {
            registro.AgregarCambio("obra-1", "AVANCE", i * 10, null);
        }

        PaginaCambios primera = registro.CambiosDesde(0, 2);
        Assert.Equal([1L, 2L], primera.Cambios.Select(c => c.Secuencia));
        Assert.Equal("2", primera.Marca);
        Assert.True(primera.HayMas);
        Assert.Equal(primera.Cambios, registro.CambiosDesde(0, 2).Cambios);   // la misma marca, los mismos cambios

        PaginaCambios ultima = registro.CambiosDesde(4, 10);
        Assert.Equal([5L], ultima.Cambios.Select(c => c.Secuencia));
        Assert.False(ultima.HayMas);
        PaginaCambios vacia = registro.CambiosDesde(5, 10);
        Assert.Empty(vacia.Cambios);
        Assert.Equal("5", vacia.Marca);
    }

    [Fact]
    public void Con_el_tiempo_asigna_contratista_a_lo_aprobado_y_avanza_lo_que_esta_en_obra()
    {
        RegistroSe7 registro = new();
        registro.RegistrarVersion("aprobada", 1, Version(Situaciones.Aprobada));
        registro.RegistrarVersion("en-obra", 1, Version(Situaciones.EnObra));
        registro.RegistrarVersion("formulada", 1, Version(Situaciones.Formulada));

        IReadOnlyList<CambioObra> cambios = registro.Generar(new Random(7));

        Assert.Equal(2, cambios.Count);
        Assert.Contains(cambios, c => c.CodigoObra == "aprobada" && c.Clase == "CONTRATISTA" && c.Contratista is not null);
        Assert.Contains(cambios, c => c.CodigoObra == "en-obra" && c.Clase == "AVANCE" && c.PorcentajeAvance is >= 5 and <= 15);
        // Lo recibido de la plataforma no aparece como cambio de SE-7: solo lo generado.
        Assert.Equal(2, registro.Estado().Cambios);
        // Ya tiene contratista: no se le asigna otro.
        Assert.DoesNotContain(registro.Generar(new Random(7)), c => c.CodigoObra == "aprobada");
    }
}
