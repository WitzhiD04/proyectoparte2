using System.Text.Json;
using ConsultaDanos.Api.Tablero;
using ConsultaDanos.Aplicacion;
using ConsultaDanos.Dominio;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConsultaDanos.Api.Pages.TableroDanos;

/// <summary>
/// Tablero de daños: el consolidado con la priorización (RF-ED-10) y el indicador de la bandeja (R-08). Llama en el mismo
/// proceso a los casos de uso de la API; solo cambia la presentación (E-41).
/// </summary>
public sealed class IndexModel : PageModel
{
    private readonly ConsultarConsolidado _consolidado;
    private readonly ConsultarIndicadores _indicadores;
    private readonly IDirectorioDesastres _directorio;
    private readonly ILogger<IndexModel> _log;

    public IndexModel(ConsultarConsolidado consolidado, ConsultarIndicadores indicadores, IDirectorioDesastres directorio,
        ILogger<IndexModel> log)
    {
        _consolidado = consolidado;
        _indicadores = indicadores;
        _directorio = directorio;
        _log = log;
    }

    [BindProperty(Name = "desastre", SupportsGet = true)]
    public Guid? IdDesastre { get; set; }

    public IReadOnlyList<DesastreTablero> Desastres { get; private set; } = [];
    public IndicadoresRevision? Bandeja { get; private set; }
    public DateTimeOffset? BandejaAl { get; private set; }
    public ConsolidadoVista? Consolidado { get; private set; }
    public bool Degradado { get; private set; }
    public List<string> Errores { get; } = [];

    public string NombreDesastre =>
        Desastres.FirstOrDefault(d => d.Id == IdDesastre)?.Nombre ?? (IdDesastre is null ? "" : "Desastre elegido");

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LeerDesastresAsync(ct);
        await LeerBandejaAsync(ct);
        IdDesastre ??= Desastres.FirstOrDefault()?.Id;
        if (IdDesastre is Guid desastre)
        {
            await LeerConsolidadoAsync(desastre, ct);
        }
    }

    private async Task LeerDesastresAsync(CancellationToken ct)
    {
        try
        {
            Desastres = await _directorio.ListarAsync(ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException
            && !ct.IsCancellationRequested)
        {
            // Sin la lista el tablero sigue sirviendo para el desastre que venga en la dirección.
            _log.LogWarning(e, "No se pudo leer la lista de desastres.");
            Errores.Add("No se pudo leer la lista de desastres del servicio Consulta. Intente de nuevo en unos segundos.");
        }
    }

    private async Task LeerBandejaAsync(CancellationToken ct)
    {
        try
        {
            (Bandeja, BandejaAl) = await _indicadores.EjecutarAsync(ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(e, "No se pudo leer la bandeja de revisión.");
            Errores.Add("No se pudo leer el indicador de la bandeja de revisión.");
        }
    }

    private async Task LeerConsolidadoAsync(Guid desastre, CancellationToken ct)
    {
        try
        {
            RespuestaLectura respuesta = await _consolidado.EjecutarAsync(desastre, ct);
            Consolidado = ConsolidadoVista.Leer(respuesta.Contenido);
            Degradado = respuesta.Degradado || Consolidado.Degradado;
        }
        catch (DatosNoDisponiblesException e)
        {
            Errores.Add(e.Message);
        }
    }
}
