namespace Auditoria.Dominio;

/// <summary>
/// Resultado de la operación que se audita (enunciado: "quién, qué hizo, cuándo, sobre qué información, resultado").
/// Los eventos de cambio del catálogo son hechos ya aplicados, así que su resultado es <see cref="Exitoso"/>.
/// <see cref="Rechazado"/> queda listo para los rechazos de autorización que publicarán el gateway y los servicios
/// (SAD §7.1: "ambos publican el resultado de la verificación al registro de auditoría", DA-15). Ese evento todavía no
/// está en el catálogo: cuando exista, su adaptador de entrada solo tiene que elegir este valor.
/// </summary>
public enum ResultadoOperacion { Exitoso, Rechazado }

/// <summary>Fuente del dato y su nivel de confianza (AD-11). Permite auditar de dónde vino cada cambio.</summary>
public sealed record ProcedenciaDato(string Fuente, string? SistemaOrigen, string Confianza);

/// <summary>
/// Una entrada del registro de auditoría (AD-21, QA-13). Es inmutable: no tiene métodos que la cambien, y la base
/// solo permite insertarla y leerla (AD-41). Guarda el sobre completo, tal como llegó, para que ninguna información se
/// pierda aunque el servicio no sepa interpretar el tipo de evento; los demás campos son la parte que se consulta.
/// </summary>
public sealed class EntradaAuditoria
{
    private EntradaAuditoria() { }

    public Guid IdEvento { get; private init; }
    /// <summary>Identificador del catálogo (EV-nn).</summary>
    public string Tipo { get; private init; } = "";
    public string Nombre { get; private init; } = "";
    public int Version { get; private init; }
    public string Productor { get; private init; } = "";
    /// <summary>Consecutivo del productor (E-18). Con él se detectan los eventos que no llegaron (R-02).</summary>
    public long? SecuenciaProductor { get; private init; }
    /// <summary>Cuándo ocurrió el hecho, según el productor. Ordena el rastro y define la partición mensual.</summary>
    public DateTimeOffset InstanteEmision { get; private init; }
    /// <summary>Cuándo quedó registrado aquí. La diferencia con la emisión es la medida de QA-15 (c) y QA-13 (c).</summary>
    public DateTimeOffset InstanteRegistro { get; private init; }
    public string? IdCorrelacion { get; private init; }
    public string ClaveIdempotencia { get; private init; } = "";
    /// <summary>Quién. Nulo si el tipo de evento no es conocido por este servicio o no trae autor.</summary>
    public string? Actor { get; private init; }
    /// <summary>Sobre qué clase de información (desastre, evaluacion, proyecto...). Nulo si el tipo no es conocido.</summary>
    public string? Entidad { get; private init; }
    public string? IdEntidad { get; private init; }
    /// <summary>Qué hizo.</summary>
    public string? Accion { get; private init; }
    /// <summary>Con qué resultado. Nulo si el tipo no es conocido: no se inventa un resultado que el evento no declara.</summary>
    public ResultadoOperacion? Resultado { get; private init; }
    /// <summary>Valor anterior de lo modificado (QA-13, QA-15 a), cuando el evento lo trae.</summary>
    public string? ValorAnterior { get; private init; }
    public string? ValorNuevo { get; private init; }
    public ProcedenciaDato Procedencia { get; private init; } = new("plataforma", null, "ALTA");
    /// <summary>El sobre canónico completo, en JSON. La aplicación no lo interpreta.</summary>
    public string Sobre { get; private init; } = "{}";

    /// <summary>Crea la entrada de un evento recibido. Valida lo mínimo para que el rastro se pueda consultar.</summary>
    public static EntradaAuditoria Registrar(
        Guid idEvento, string tipo, string nombre, int version, string productor, long? secuenciaProductor,
        DateTimeOffset instanteEmision, DateTimeOffset instanteRegistro, string? idCorrelacion, string claveIdempotencia,
        Interpretacion interpretacion, ProcedenciaDato procedencia, string sobre)
    {
        if (idEvento == Guid.Empty)
        {
            throw new ReglaDeNegocioException("EVENTO_SIN_ID", "El evento no trae identificador.");
        }
        Exigir(tipo, "tipo");
        Exigir(nombre, "nombre");
        Exigir(productor, "productor");
        Exigir(claveIdempotencia, "claveIdempotencia");
        Exigir(sobre, "sobre");
        if (secuenciaProductor is < 1)
        {
            throw new ReglaDeNegocioException("SECUENCIA_INVALIDA", "La secuencia del productor empieza en 1.");
        }
        return new EntradaAuditoria
        {
            IdEvento = idEvento,
            Tipo = tipo,
            Nombre = nombre,
            Version = version,
            Productor = productor,
            SecuenciaProductor = secuenciaProductor,
            InstanteEmision = instanteEmision.ToUniversalTime(),
            InstanteRegistro = instanteRegistro.ToUniversalTime(),
            IdCorrelacion = idCorrelacion,
            ClaveIdempotencia = claveIdempotencia,
            Actor = interpretacion.Actor,
            Entidad = interpretacion.Entidad,
            IdEntidad = interpretacion.IdEntidad,
            Accion = interpretacion.Accion,
            Resultado = interpretacion.Resultado,
            ValorAnterior = interpretacion.ValorAnterior,
            ValorNuevo = interpretacion.ValorNuevo,
            Procedencia = procedencia,
            Sobre = sobre,
        };
    }

    /// <summary>Reconstruye una entrada leída del almacén (sin validar: ya se validó al registrarla).</summary>
    public static EntradaAuditoria Reconstituir(
        Guid idEvento, string tipo, string nombre, int version, string productor, long? secuenciaProductor,
        DateTimeOffset instanteEmision, DateTimeOffset instanteRegistro, string? idCorrelacion, string claveIdempotencia,
        Interpretacion interpretacion, ProcedenciaDato procedencia, string sobre) => new()
    {
        IdEvento = idEvento,
        Tipo = tipo,
        Nombre = nombre,
        Version = version,
        Productor = productor,
        SecuenciaProductor = secuenciaProductor,
        InstanteEmision = instanteEmision,
        InstanteRegistro = instanteRegistro,
        IdCorrelacion = idCorrelacion,
        ClaveIdempotencia = claveIdempotencia,
        Actor = interpretacion.Actor,
        Entidad = interpretacion.Entidad,
        IdEntidad = interpretacion.IdEntidad,
        Accion = interpretacion.Accion,
        Resultado = interpretacion.Resultado,
        ValorAnterior = interpretacion.ValorAnterior,
        ValorNuevo = interpretacion.ValorNuevo,
        Procedencia = procedencia,
        Sobre = sobre,
    };

    private static void Exigir(string valor, string campo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new ReglaDeNegocioException("EVENTO_INCOMPLETO", $"El evento no trae {campo}.");
        }
    }
}

/// <summary>
/// Lo que el adaptador de entrada sabe leer de los datos de un evento: quién, sobre qué, qué hizo y con qué resultado.
/// Para un tipo que no conoce todo es nulo (<see cref="Desconocida"/>), y el evento se registra igual (QA-09, R-03).
/// </summary>
public sealed record Interpretacion(
    string? Actor,
    string? Entidad,
    string? IdEntidad,
    string? Accion,
    ResultadoOperacion? Resultado,
    string? ValorAnterior = null,
    string? ValorNuevo = null)
{
    public static Interpretacion Desconocida { get; } = new(null, null, null, null, null);
}
