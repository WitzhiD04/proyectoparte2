using System.Reflection;
using EvaluacionDanos.Aplicacion;
using EvaluacionDanos.Dominio;

namespace EvaluacionDanos.Pruebas.Arquitectura;

/// <summary>
/// Regla de dependencias de la arquitectura limpia y hexagonal (AD-08, corrige H-12): el dominio no depende de nada y
/// la aplicación solo del dominio. Ninguno conoce el contrato del bus, la base de datos ni el framework web.
/// </summary>
public class PruebasArquitectura
{
    private static readonly string[] Prohibidos =
    [
        "Plataforma.Comun", "Microsoft.EntityFrameworkCore", "Npgsql", "NetTopologySuite", "RabbitMQ", "Minio",
        "Microsoft.AspNetCore", "Microsoft.Extensions", "EvaluacionDanos.Infraestructura", "EvaluacionDanos.Api", "System.Text.Json",
    ];

    private static IEnumerable<string> Referencias(Assembly ensamblado) =>
        ensamblado.GetReferencedAssemblies().Select(a => a.Name ?? "");

    [Fact]
    public void El_dominio_no_depende_de_otros_proyectos_ni_de_paquetes()
    {
        Assembly dominio = typeof(ReglaConflicto).Assembly;
        Assert.DoesNotContain(Referencias(dominio), r => r.StartsWith("EvaluacionDanos.", StringComparison.Ordinal));
        Assert.DoesNotContain(Referencias(dominio), r => Prohibidos.Any(p => r.StartsWith(p, StringComparison.Ordinal)));
    }

    [Fact]
    public void La_aplicacion_solo_depende_del_dominio()
    {
        Assembly aplicacion = typeof(Hecho).Assembly;
        List<string> propias = Referencias(aplicacion).Where(r => r.StartsWith("EvaluacionDanos.", StringComparison.Ordinal)).ToList();
        Assert.Equal(["EvaluacionDanos.Dominio"], propias);
        Assert.DoesNotContain(Referencias(aplicacion), r => Prohibidos.Any(p => r.StartsWith(p, StringComparison.Ordinal)));
    }
}
