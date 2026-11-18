using ConsultaDanos.Api.Tablero;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConsultaDanos.Api.Pages.TableroDanos;

/// <summary>
/// Cierra la sesión del tablero y la de Keycloak. Va por GET porque el catálogo del gateway no admite una ruta pública de
/// modificación (E-41); Keycloak pide confirmación antes de cerrar, así que un enlace ajeno no saca a nadie.
/// </summary>
public sealed class SalirModel : PageModel
{
    public IActionResult OnGet() => SignOut(new AuthenticationProperties { RedirectUri = RegistroTablero.RutaBase },
        CookieAuthenticationDefaults.AuthenticationScheme, RegistroTablero.EsquemaOidc);
}
