namespace AdaptadorSe6;

// Formato de SE-6 tal como lo ve el adaptador (AD-03): cuentas de proyecto, situaciones financieras y lotes de
// movimientos. Solo existe en este proyecto; el resto de la plataforma conoce únicamente el contrato de comun (AD-09).

/// <summary>Situación financiera de un proyecto en SE-6.</summary>
public static class SituacionSe6
{
    public const string Abierta = "ABIERTA";
    public const string Comprometida = "COMPROMETIDA";
    public const string EnEjecucion = "EN_EJECUCION";
    public const string Liquidada = "LIQUIDADA";
    public const string Anulada = "ANULADA";
}

/// <summary>Clase de movimiento financiero en SE-6.</summary>
public static class ClaseMovimientoSe6
{
    public const string Apertura = "APERTURA";
    public const string Presupuesto = "PRESUPUESTO";
    public const string Fuente = "FUENTE";
    public const string Costo = "COSTO";
    public const string Finalizacion = "FINALIZACION";
    public const string Cancelacion = "CANCELACION";
}

/// <summary>
/// Un elemento dentro de un lote enviado a SE-6. La idempotencia en SE-6 usa el par
/// (<see cref="CodigoProyecto"/>, <see cref="VersionHistorial"/>) según el Cuadro 14 y DA-11.
/// </summary>
public sealed record MovimientoSe6(
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
    string? Nota = null)
{
    /// <summary>Clave de idempotencia del elemento (idProyecto + versionHistorial, DA-11).</summary>
    public string ClaveElemento => $"{CodigoProyecto}:v{VersionHistorial}";
}

/// <summary>Cuerpo de <c>PUT /v1/lotes/{idLote}</c>.</summary>
public sealed record LoteMovimientosSe6(IReadOnlyList<MovimientoSe6> Movimientos);

/// <summary>Una entrega por lote a SE-6: identificador determinista del lote y su lista de movimientos.</summary>
public sealed record PeticionLoteSe6(string IdLote, LoteMovimientosSe6 Cuerpo)
{
    public string Ruta => $"v1/lotes/{Uri.EscapeDataString(IdLote)}";
}
