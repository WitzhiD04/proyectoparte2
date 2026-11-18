namespace SimSe6;

// Formato propio de SE-6, el sistema financiero externo (DA-11). Es distinto del contrato de la plataforma a propósito:
// el adaptador traduce (AD-03, AD-09). Vocabulario de SE-6: una "cuenta" de proyecto, su "situacion" financiera y sus
// "movimientos" recibidos en lotes (Cuadro 14: "envío por lote").

/// <summary>Situación financiera de la cuenta de un proyecto en SE-6.</summary>
public static class Situaciones
{
    public const string Abierta = "ABIERTA";
    public const string Comprometida = "COMPROMETIDA";
    public const string EnEjecucion = "EN_EJECUCION";
    public const string Liquidada = "LIQUIDADA";
    public const string Anulada = "ANULADA";

    public static readonly IReadOnlySet<string> Todas = new HashSet<string>
    {
        Abierta, Comprometida, EnEjecucion, Liquidada, Anulada,
    };
}

/// <summary>Clases de movimiento que registra SE-6.</summary>
public static class ClasesMovimiento
{
    public const string Apertura = "APERTURA";
    public const string Presupuesto = "PRESUPUESTO";
    public const string Fuente = "FUENTE";
    public const string Costo = "COSTO";
    public const string Finalizacion = "FINALIZACION";
    public const string Cancelacion = "CANCELACION";

    public static readonly IReadOnlySet<string> Todas = new HashSet<string>
    {
        Apertura, Presupuesto, Fuente, Costo, Finalizacion, Cancelacion,
    };
}

/// <summary>
/// Un movimiento financiero dentro de un lote enviado a SE-6. La clave de idempotencia del elemento es el par
/// (<see cref="CodigoProyecto"/>, <see cref="VersionHistorial"/>) según el Cuadro 14 y DA-11: repetir el mismo
/// movimiento en el mismo lote o en otro lote no tiene efecto.
/// </summary>
public sealed record MovimientoFinanciero(
    string CodigoProyecto,
    int VersionHistorial,
    string Clase,
    string TituloProyecto,
    string CodigoDesastre,
    string Situacion,
    decimal? Presupuesto,
    string? FuenteFinanciacion,
    decimal? MontoMovimiento,
    decimal CostoAcumulado,
    DateTimeOffset OcurridoEn,
    string? Autor = null,
    string? Nota = null);

/// <summary>Cuerpo de <c>PUT /v1/lotes/{idLote}</c> (y <c>POST /v1/lotes</c>).</summary>
public sealed record SolicitudLoteMovimientos(IReadOnlyList<MovimientoFinanciero> Movimientos);

/// <summary>Resultado por elemento del lote. <c>Registrado</c> es falso si SE-6 ya tenía esa versión del proyecto.</summary>
public sealed record ResultadoElementoLote(string CodigoProyecto, int VersionHistorial, bool Registrado);

/// <summary>Respuesta al procesar un lote de movimientos.</summary>
public sealed record RespuestaLote(
    string IdLote,
    int Recibidos,
    int Nuevos,
    int Repetidos,
    IReadOnlyList<ResultadoElementoLote> Resultados);

/// <summary>Cuerpo de <c>POST /admin/caida</c>: on (responde 503), off (normal) o lento (responde después del retraso).</summary>
public sealed record SolicitudCaida(string Modo);

public sealed record EstadoCuentaProyecto(
    string CodigoProyecto,
    string TituloProyecto,
    string CodigoDesastre,
    string Situacion,
    decimal? Presupuesto,
    string? FuenteFinanciacion,
    decimal CostoAcumulado,
    IReadOnlyList<int> Versiones,
    IReadOnlyList<MovimientoFinanciero> Movimientos,
    int EntregasRepetidas);

public sealed record EstadoSimulador(
    string Modo,
    IReadOnlyList<EstadoCuentaProyecto> Proyectos,
    IReadOnlyList<MovimientoFinanciero> Movimientos,
    int LotesRecibidos,
    int MovimientosRegistrados,
    int EntregasRepetidas);
