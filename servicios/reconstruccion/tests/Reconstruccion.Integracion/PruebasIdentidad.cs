using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Comun.Seguridad;
using Reconstruccion.Api.Controllers;
using Reconstruccion.Dominio;

namespace Reconstruccion.Integracion;

/// <summary>
/// La capa Api traduce la identidad común (IIdentidadActual, DA-15) al actor del dominio: roles del realm, ámbito y
/// territorio. Se prueba con el adaptador de encabezados real; el de JWT entrega la misma Identidad y lo prueba comun/.
/// </summary>
public class PruebasIdentidad
{
    /// <summary>Expone el armado del actor de la clase base, sin rutas ni casos de uso.</summary>
    private sealed class ControladorDePrueba : ControladorBase
    {
        public (bool Ok, Actor Actor, IActionResult Error) Armar()
        {
            bool ok = IntentarActor(out Actor actor, out IActionResult error);
            return (ok, actor, error);
        }
    }

    private static ControladorDePrueba Con(params (string Nombre, string Valor)[] encabezados)
    {
        ServiceCollection servicios = new();
        servicios.AgregarIdentidad(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        DefaultHttpContext http = new();
        foreach ((string nombre, string valor) in encabezados)
        {
            http.Request.Headers[nombre] = valor;
        }
        ServiceProvider proveedor = servicios.BuildServiceProvider();
        proveedor.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        http.RequestServices = proveedor;
        return new ControladorDePrueba { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    [Fact]
    public void La_autoridad_local_llega_con_su_ambito_y_su_territorio()
    {
        var (ok, actor, _) = Con(("X-Usuario", "alcaldia.armenia"), ("X-Roles", "autoridad"), ("X-Ambito", "LOCAL"),
            ("X-Territorio", "63001")).Armar();
        Assert.True(ok);
        Assert.Equal(new Actor("alcaldia.armenia", Rol.Autoridad, Ambito.Local, "63001"), actor);
    }

    [Theory]
    [InlineData("responsable-proyecto", Rol.Responsable)]
    [InlineData("auditor", Rol.Auditor)]
    [InlineData("auditor,autoridad", Rol.Autoridad)]   // con varios roles prima el de mayor alcance
    public void Los_roles_del_realm_se_traducen_al_rol_del_dominio(string roles, Rol esperado)
    {
        var (ok, actor, _) = Con(("X-Usuario", "persona"), ("X-Roles", roles)).Armar();
        Assert.True(ok);
        Assert.Equal(esperado, actor.Rol);
    }

    [Fact]
    public void Sin_identidad_es_401()
    {
        var (ok, _, error) = Con().Armar();
        Assert.False(ok);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeActionResult>(error).StatusCode);
    }

    [Theory]
    [InlineData("evaluador")]
    [InlineData("organismo")]
    public void Una_identidad_sin_rol_de_Reconstruccion_es_403(string rol)
    {
        var (ok, _, error) = Con(("X-Usuario", "persona"), ("X-Roles", rol)).Armar();
        Assert.False(ok);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeActionResult>(error).StatusCode);
    }

    [Fact]
    public void El_encabezado_viejo_X_Rol_ya_no_da_rol()
    {
        // Antes Reconstrucción leía X-Rol: Autoridad. Ahora la identidad es la común (X-Roles con los roles del realm).
        var (ok, _, error) = Con(("X-Usuario", "alcaldia.armenia"), ("X-Rol", "Autoridad"), ("X-Ambito", "Local")).Armar();
        Assert.False(ok);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeActionResult>(error).StatusCode);
    }
}
