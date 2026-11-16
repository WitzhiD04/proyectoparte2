using Auditoria.Dominio;

namespace Auditoria.Aplicacion.Puertos;

/// <summary>Puerto de entrada: lo que un controller conoce de un caso de uso.</summary>
public interface IPuertoEntrada<TSolicitud, TRespuesta>
{
    Task EjecutarAsync(TSolicitud solicitud, IPuertoSalida<TRespuesta> salida, CancellationToken ct = default);
}

/// <summary>Puerto de salida: por aquí el caso de uso entrega su resultado. Lo implementa un presenter.</summary>
public interface IPuertoSalida<TRespuesta>
{
    void Exito(TRespuesta respuesta);
    void NoAutorizado(string mensaje);
    void ReglaIncumplida(string codigo, string mensaje);
}

/// <summary>
/// Registro de auditoría de solo inserción (AD-21, AD-41): el puerto no ofrece modificar ni borrar, y la base tampoco
/// lo permite al rol del servicio.
/// </summary>
public interface IRegistroAuditoria
{
    /// <summary>Segunda barrera de idempotencia, por identificador del evento (la primera es la clave, DA-03).</summary>
    Task<bool> ExisteAsync(Guid idEvento, CancellationToken ct = default);

    /// <summary>Deja la entrada lista para guardarse. La confirma la unidad de trabajo del consumidor, en la misma
    /// transacción que la marca de mensaje procesado.</summary>
    void Agregar(EntradaAuditoria entrada);

    /// <summary>Entradas que cumplen el filtro, en orden cronológico de emisión, y el total sin paginar.</summary>
    Task<PaginaEntradas> ConsultarAsync(FiltroRegistro filtro, int pagina, int tamano, CancellationToken ct = default);
}

/// <summary>Los números de secuencia ya registrados de cada productor (R-02). Se leen del propio registro.</summary>
public interface ISecuenciasRegistradas
{
    Task<IReadOnlyList<string>> ProductoresAsync(CancellationToken ct = default);

    /// <summary>Números registrados mayores que <paramref name="desde"/>, en orden ascendente, como máximo <paramref name="limite"/>.</summary>
    Task<IReadOnlyList<long>> MayoresQueAsync(string productor, long desde, int limite, CancellationToken ct = default);

    /// <summary>Cuáles de estos números ya están registrados.</summary>
    Task<IReadOnlySet<long>> RegistradasAsync(string productor, IReadOnlyCollection<long> secuencias, CancellationToken ct = default);
}

/// <summary>Estado de la secuencia de cada productor y sus huecos.</summary>
public interface ISeguimientos
{
    /// <summary>El seguimiento con sus huecos abiertos, o nulo si el productor no se ha revisado nunca.</summary>
    Task<SeguimientoProductor?> BuscarAsync(string productor, CancellationToken ct = default);

    /// <summary>Guarda el estado y los huecos nuevos o modificados en la revisión.</summary>
    Task GuardarAsync(SeguimientoProductor seguimiento, IReadOnlyList<HuecoSecuencia> cambiados, CancellationToken ct = default);

    Task<IReadOnlyList<SeguimientoProductor>> TodosAsync(CancellationToken ct = default);

    /// <summary>Huecos que se reportaron (estén cubiertos o no), del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<HuecoSecuencia>> ReportadosAsync(int limite, CancellationToken ct = default);
}

/// <summary>Alerta de un hueco que persistió más del plazo (R-02, DA-17: "un salto genera una alerta").</summary>
public interface IAvisosHuecos
{
    void HuecoReportado(HuecoSecuencia hueco, TimeSpan plazo);
}

public interface IReloj
{
    DateTimeOffset Ahora { get; }
}
