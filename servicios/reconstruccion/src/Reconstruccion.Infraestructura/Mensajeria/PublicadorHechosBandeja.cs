using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;
using Reconstruccion.Aplicacion;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Infraestructura.Persistencia;
using Dominio = Reconstruccion.Dominio;

namespace Reconstruccion.Infraestructura.Mensajeria;

/// <summary>
/// Adaptador de salida del puerto <see cref="IPublicadorHechos"/>. Traduce cada hecho de un proyecto al evento del
/// catálogo (EV-18, EV-19 o EV-20, enmienda E-10) con el sobre canónico, y lo deja en la bandeja de salida del mismo
/// DbContext, sin guardar: la unidad de trabajo lo confirma junto con el cambio y su historial (AD-45).
/// </summary>
public sealed class PublicadorHechosBandeja : IPublicadorHechos
{
    public const string EncabezadoCorrelacion = "X-Id-Correlacion";

    private readonly EscritorBandeja<ReconstruccionDbContext> _bandeja;
    private readonly IHttpContextAccessor _http;

    public PublicadorHechosBandeja(EscritorBandeja<ReconstruccionDbContext> bandeja, IHttpContextAccessor http)
    {
        _bandeja = bandeja;
        _http = http;
    }

    public Task RegistrarAsync(IEnumerable<HechoProyecto> hechos, CancellationToken ct = default)
    {
        string? correlacion = Correlacion();
        foreach (HechoProyecto hecho in hechos)
        {
            _bandeja.Agregar(Traducir(hecho, correlacion));
        }
        return Task.CompletedTask;
    }

    /// <summary>Un hecho, un evento. Público para las pruebas del contrato.</summary>
    public static Sobre Traducir(HechoProyecto hecho, string? correlacion)
    {
        Dominio.EntradaHistorial entrada = hecho.Entrada;
        ResumenDeProyecto p = hecho.Proyecto;
        ResumenProyecto resumen = new(p.IdDesastre, p.Nombre, MapeoCanonico.AlContrato(p.Estado), p.Presupuesto,
            p.CostoEjecutado, p.Avance, p.Version);
        // Un evento por entrada de historial: la versión de la entrada identifica el hecho.
        string clave = $"proyecto:{p.IdProyecto}:v{entrada.Version}";

        switch (entrada.TipoCambio)
        {
            case Dominio.TipoCambio.Creacion:
                ProyectoCreadoV1 creado = new(p.IdProyecto, p.IdsEvaluacionesOrigen, p.IdDesastre, p.Presupuesto,
                    p.FuenteFinanciacion, p.Responsable, Cronograma(p.Cronograma), p.CodigosMunicipio,
                    MapeoCanonico.AlContrato(p.Alcance), p.Nombre, resumen, entrada.Autor);
                return Sobre.Crear(CatalogoEventos.ProyectoCreado, CatalogoEventos.Reconstruccion, clave, creado,
                    entrada.Instante, idCorrelacion: correlacion, idEvento: entrada.Id);

            case Dominio.TipoCambio.Presupuesto:
            case Dominio.TipoCambio.Fuente:
                bool esPresupuesto = entrada.TipoCambio == Dominio.TipoCambio.Presupuesto;
                PresupuestoCambiadoV1 presupuesto = new(p.IdProyecto, MapeoCanonico.AlContrato(entrada.TipoCambio),
                    esPresupuesto ? Monto(entrada.ValorAnterior) : p.Presupuesto,
                    esPresupuesto ? Monto(entrada.ValorNuevo) : p.Presupuesto,
                    esPresupuesto ? p.FuenteFinanciacion : entrada.ValorAnterior,
                    esPresupuesto ? p.FuenteFinanciacion : entrada.ValorNuevo,
                    entrada.Version, entrada.Autor, entrada.Justificacion, resumen);
                return Sobre.Crear(CatalogoEventos.PresupuestoCambiado, CatalogoEventos.Reconstruccion, clave, presupuesto,
                    entrada.Instante, idCorrelacion: correlacion, idEvento: entrada.Id);

            default:
                // Responsable, cronograma, contratista, estado, avance, costo y documento.
                Plataforma.Comun.Eventos.EstadoProyecto estadoActual = MapeoCanonico.AlContrato(p.Estado);
                Plataforma.Comun.Eventos.EstadoProyecto estadoAnterior = entrada.TipoCambio == Dominio.TipoCambio.Estado
                                                                         && Enum.TryParse(entrada.ValorAnterior, out Dominio.EstadoProyecto anterior)
                    ? MapeoCanonico.AlContrato(anterior)
                    : estadoActual;
                EstadoOAvanceCambiadoV1 cambio = new(p.IdProyecto, MapeoCanonico.AlContrato(entrada.TipoCambio),
                    estadoAnterior, estadoActual, p.Avance, p.CostoEjecutado, hecho.CostoRegistrado,
                    entrada.ValorAnterior, entrada.ValorNuevo, entrada.Version, entrada.Autor, entrada.Justificacion, resumen);
                Procedencia procedencia = entrada.Procedencia == Dominio.ProyectoReconstruccion.ProcedenciaPlataforma
                    ? Procedencia.Plataforma
                    : Procedencia.DeSistemaExterno(entrada.Procedencia);
                return Sobre.Crear(CatalogoEventos.EstadoOAvanceCambiado, CatalogoEventos.Reconstruccion, clave, cambio,
                    entrada.Instante, procedencia, correlacion, entrada.Id);
        }
    }

    private static Cronograma? Cronograma(Dominio.Cronograma? cronograma) =>
        cronograma is null ? null : new Cronograma(cronograma.Inicio, cronograma.Fin);

    /// <summary>El historial guarda el monto como texto con formato invariante (Dinero.ToString).</summary>
    private static decimal? Monto(string? texto) =>
        decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal valor) ? valor : null;

    /// <summary>Correlación de la petición: el encabezado que traiga, o el identificador de la traza actual (DA-20).</summary>
    private string? Correlacion()
    {
        HttpContext? contexto = _http.HttpContext;
        string? encabezado = contexto?.Request.Headers[EncabezadoCorrelacion].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(encabezado))
        {
            return encabezado;
        }
        return Activity.Current?.TraceId.ToString() ?? contexto?.TraceIdentifier;
    }
}
