using EvaluacionDanos.Aplicacion;
using EvaluacionDanos.Dominio;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EvaluacionDanos.Api.Pages;

// Página /mapa?desastreId=..&nivelMinimo=.. con el mapa de calor. No es parte del API REST: era la interfaz de
// consola del prototipo. Los puntos los pide calor.js cada 10 s a /v1/mapa-calor del propio API.
// OBSOLETA: se conserva tal como está hasta que la reemplacen Consulta de Daños y la consola (DA-08, DA-13).
public class MapaModel(ICasoDeUso<bool, List<DesastreDto>> desastres) : PageModel
{
    public List<DesastreDto> Desastres { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public Guid? DesastreId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string NivelMinimo { get; set; } = "SIN_DANO";

    public DesastreDto? Desastre { get; private set; }

    public async Task OnGetAsync()
    {
        Desastres = (await desastres.EjecutarAsync(false)).Valor ?? [];

        Desastre = Desastres.FirstOrDefault(d => d.Id == DesastreId) ?? Desastres.FirstOrDefault();
        DesastreId = Desastre?.Id;
        if (!ValidadorEvaluacion.EsNombreDeEnum<NivelDano>(NivelMinimo))
        {
            NivelMinimo = "SIN_DANO";
        }
    }

    public static string Etiqueta(NivelDano nivel) => nivel switch
    {
        NivelDano.SIN_DANO => "Sin daño",
        NivelDano.LEVE => "Leve",
        NivelDano.MODERADO => "Moderado",
        NivelDano.GRAVE => "Grave",
        NivelDano.DESTRUCCION_TOTAL => "Destrucción total",
        _ => nivel.ToString()
    };
}
