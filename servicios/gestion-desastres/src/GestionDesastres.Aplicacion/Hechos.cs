using GestionDesastres.Dominio;

namespace GestionDesastres.Aplicacion;

/// <summary>
/// Un hecho de negocio de un desastre: una entrada nueva del historial más el estado resultante. Es un dato de la
/// aplicación; el adaptador de salida lo traduce a EV-03, EV-04, EV-05, EV-06 o EV-27 con el sobre canónico.
/// </summary>
public sealed record HechoDesastre(CambioDesastre Cambio, InstantaneaDesastre Desastre);

public static class HechosDeDesastre
{
    public static List<HechoDesastre> De(Desastre desastre)
    {
        InstantaneaDesastre estado = desastre.AInstantanea();
        return desastre.CambiosNuevos.Select(c => new HechoDesastre(c, estado)).ToList();
    }
}
