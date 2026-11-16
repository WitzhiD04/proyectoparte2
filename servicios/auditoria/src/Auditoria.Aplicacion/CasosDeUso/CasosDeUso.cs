using Auditoria.Aplicacion.Puertos;
using Auditoria.Dominio;

namespace Auditoria.Aplicacion.CasosDeUso;

/// <summary>Base de los interactors: convierte las excepciones del dominio en llamadas al puerto de salida.</summary>
public abstract class Interactor
{
    protected static async Task ProtegerAsync<T>(IPuertoSalida<T> salida, Func<Task> accion)
    {
        try
        {
            await accion();
        }
        catch (NoAutorizadoException e)
        {
            salida.NoAutorizado(e.Message);
        }
        catch (ReglaDeNegocioException e)
        {
            salida.ReglaIncumplida(e.Codigo, e.Message);
        }
    }
}

// ---------------------------------------------------------------- Registrar un evento (entrada desde el bus)

/// <summary>Puerto de entrada asíncrono: lo usa el adaptador de eventos del bus, nunca una ruta HTTP (AD-21).</summary>
public interface IRegistrarEventoEntrada
{
    /// <summary>Registra el evento una sola vez. Devuelve falso si ya estaba registrado.</summary>
    Task<bool> EjecutarAsync(ComandoRegistrarEvento comando, CancellationToken ct = default);
}

/// <summary>
/// Guarda un evento en el registro inmutable (AD-21, QA-13). La primera barrera contra duplicados es la clave de
/// idempotencia del consumidor (DA-03); esta es la segunda, por identificador del evento, para el caso de un mismo
/// evento republicado con otra clave. La tercera es la llave primaria de la tabla.
/// </summary>
public sealed class RegistrarEvento : IRegistrarEventoEntrada
{
    private readonly IRegistroAuditoria _registro;
    private readonly IReloj _reloj;

    public RegistrarEvento(IRegistroAuditoria registro, IReloj reloj)
    {
        _registro = registro;
        _reloj = reloj;
    }

    public async Task<bool> EjecutarAsync(ComandoRegistrarEvento c, CancellationToken ct = default)
    {
        if (await _registro.ExisteAsync(c.IdEvento, ct))
        {
            return false;
        }
        EntradaAuditoria entrada = EntradaAuditoria.Registrar(c.IdEvento, c.Tipo, c.Nombre, c.Version, c.Productor,
            c.SecuenciaProductor, c.InstanteEmision, _reloj.Ahora, c.IdCorrelacion, c.ClaveIdempotencia, c.Interpretacion,
            c.Procedencia, c.Sobre);
        _registro.Agregar(entrada);
        return true;
    }
}

// ---------------------------------------------------------------- Consultar el rastro

public sealed record SolicitudConsultaRegistro(Solicitante Solicitante, FiltroRegistro Filtro, int Pagina = 1, int Tamano = ConsultarRegistro.TamanoPorOmision);

public interface IConsultarRegistroEntrada : IPuertoEntrada<SolicitudConsultaRegistro, PaginaRegistroDto> { }

/// <summary>
/// Consulta del rastro por entidad, actor, tipo o rango de fechas, paginada y en orden cronológico (DA-17, QA-13 d).
/// Solo para los roles auditor y autoridad.
/// </summary>
public sealed class ConsultarRegistro : Interactor, IConsultarRegistroEntrada
{
    public const int TamanoPorOmision = 100;
    public const int TamanoMaximo = 1_000;

    private readonly IRegistroAuditoria _registro;

    public ConsultarRegistro(IRegistroAuditoria registro)
    {
        _registro = registro;
    }

    public Task EjecutarAsync(SolicitudConsultaRegistro solicitud, IPuertoSalida<PaginaRegistroDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            PoliticaConsulta.ExigirLectura(solicitud.Solicitante);
            if (solicitud.Pagina < 1)
            {
                throw new ReglaDeNegocioException("CONSULTA_INVALIDA", "La página empieza en 1.");
            }
            if (solicitud.Tamano is < 1 or > TamanoMaximo)
            {
                throw new ReglaDeNegocioException("CONSULTA_INVALIDA", $"El tamaño de página va de 1 a {TamanoMaximo}.");
            }
            FiltroRegistro f = solicitud.Filtro;
            if (f.Desde is not null && f.Hasta is not null && f.Desde > f.Hasta)
            {
                throw new ReglaDeNegocioException("CONSULTA_INVALIDA", "'desde' debe ser anterior o igual a 'hasta'.");
            }
            PaginaEntradas pagina = await _registro.ConsultarAsync(f, solicitud.Pagina, solicitud.Tamano, ct);
            salida.Exito(new PaginaRegistroDto(solicitud.Pagina, solicitud.Tamano, pagina.Total,
                pagina.Entradas.Select(ADto).ToList()));
        });
    }

    public static EntradaDto ADto(EntradaAuditoria e) => new(e.IdEvento, e.Tipo, e.Nombre, e.Version, e.Productor,
        e.SecuenciaProductor, e.InstanteEmision, e.InstanteRegistro, e.IdCorrelacion, e.ClaveIdempotencia, e.Actor,
        e.Entidad, e.IdEntidad, e.Accion, e.Resultado?.ToString().ToUpperInvariant(), e.ValorAnterior, e.ValorNuevo,
        new ProcedenciaDto(e.Procedencia.Fuente, e.Procedencia.SistemaOrigen, e.Procedencia.Confianza), e.Sobre);
}

// ---------------------------------------------------------------- Consultar los huecos (R-02)

public sealed record SolicitudHuecos(Solicitante Solicitante);

public interface IConsultarHuecosEntrada : IPuertoEntrada<SolicitudHuecos, HuecosDto> { }

/// <summary>Estado de la secuencia de cada productor y los huecos reportados (R-02).</summary>
public sealed class ConsultarHuecos : Interactor, IConsultarHuecosEntrada
{
    public const int MaximoReportados = 1_000;

    private readonly ISeguimientos _seguimientos;
    private readonly OpcionesHuecos _opciones;

    public ConsultarHuecos(ISeguimientos seguimientos, OpcionesHuecos opciones)
    {
        _seguimientos = seguimientos;
        _opciones = opciones;
    }

    public Task EjecutarAsync(SolicitudHuecos solicitud, IPuertoSalida<HuecosDto> salida, CancellationToken ct = default)
    {
        return ProtegerAsync(salida, async () =>
        {
            PoliticaConsulta.ExigirLectura(solicitud.Solicitante);
            List<ProductorDto> productores = (await _seguimientos.TodosAsync(ct))
                .OrderBy(s => s.Productor, StringComparer.Ordinal)
                .Select(s => new ProductorDto(s.Productor, s.MayorContiguo, s.MayorRecibido,
                    s.HuecosAbiertos.Where(h => h.Reportado).Select(h => h.Secuencia).ToList(),
                    s.HuecosAbiertos.Where(h => !h.Reportado).Select(h => h.Secuencia).ToList(),
                    s.RevisadoEn))
                .ToList();
            List<HuecoDto> reportados = (await _seguimientos.ReportadosAsync(MaximoReportados, ct))
                .Select(h => new HuecoDto(h.Productor, h.Secuencia, h.DetectadoEn, h.ReportadoEn, h.CubiertoEn,
                    h.Abierto ? "ABIERTO" : "CUBIERTO"))
                .ToList();
            salida.Exito(new HuecosDto((int)_opciones.Plazo.TotalSeconds, productores, reportados));
        });
    }
}

// ---------------------------------------------------------------- Revisar las secuencias (proceso periódico)

public interface IRevisarSecuenciasEntrada
{
    Task<ResumenRevision> EjecutarAsync(CancellationToken ct = default);
}

/// <summary>
/// Detección de huecos (ATAM R-02, DA-17). Por productor compara los números registrados con los esperados y reporta
/// los que siguen faltando después del plazo. Lee el registro, no el orden de llegada: dos réplicas del consumidor o
/// del relevo pueden entregar fuera de orden sin que eso cuente como pérdida. La invoca un proceso periódico, que
/// garantiza que una sola réplica la ejecute a la vez.
/// </summary>
public sealed class RevisarSecuencias : IRevisarSecuenciasEntrada
{
    private readonly ISecuenciasRegistradas _secuencias;
    private readonly ISeguimientos _seguimientos;
    private readonly IAvisosHuecos _avisos;
    private readonly IReloj _reloj;
    private readonly OpcionesHuecos _opciones;

    public RevisarSecuencias(ISecuenciasRegistradas secuencias, ISeguimientos seguimientos, IAvisosHuecos avisos,
        IReloj reloj, OpcionesHuecos opciones)
    {
        _secuencias = secuencias;
        _seguimientos = seguimientos;
        _avisos = avisos;
        _reloj = reloj;
        _opciones = opciones;
    }

    public async Task<ResumenRevision> EjecutarAsync(CancellationToken ct = default)
    {
        int detectados = 0, cubiertos = 0, reportados = 0;
        IReadOnlyList<string> productores = await _secuencias.ProductoresAsync(ct);
        foreach (string productor in productores)
        {
            SeguimientoProductor seguimiento = await _seguimientos.BuscarAsync(productor, ct) ?? SeguimientoProductor.Nuevo(productor);
            IReadOnlyList<long> recibidas = await _secuencias.MayoresQueAsync(productor, seguimiento.RevisadoHasta, _opciones.Lote, ct);
            List<long> abiertos = seguimiento.HuecosAbiertos.Select(h => h.Secuencia).ToList();
            IReadOnlySet<long> llegadas = abiertos.Count == 0
                ? new HashSet<long>()
                : await _secuencias.RegistradasAsync(productor, abiertos, ct);

            RevisionSecuencia revision = seguimiento.Revisar(recibidas, llegadas, _reloj.Ahora, _opciones.Plazo);
            await _seguimientos.GuardarAsync(seguimiento, revision.Cambiados, ct);
            foreach (HuecoSecuencia hueco in revision.Reportados)
            {
                _avisos.HuecoReportado(hueco, _opciones.Plazo);
            }
            detectados += revision.Detectados.Count;
            cubiertos += revision.Cubiertos.Count;
            reportados += revision.Reportados.Count;
        }
        return new ResumenRevision(productores.Count, detectados, cubiertos, reportados);
    }
}
