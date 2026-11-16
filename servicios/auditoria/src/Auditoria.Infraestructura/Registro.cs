using Auditoria.Aplicacion;
using Auditoria.Aplicacion.CasosDeUso;
using Auditoria.Aplicacion.Puertos;
using Auditoria.Infraestructura.Mensajeria;
using Auditoria.Infraestructura.Persistencia;
using Auditoria.Infraestructura.Procesos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Comun.Mensajeria;

namespace Auditoria.Infraestructura;

/// <summary>Conecta cada puerto con su adaptador.</summary>
public static class Registro
{
    public static IServiceCollection AgregarInfraestructura(this IServiceCollection servicios, IConfiguration configuracion)
    {
        OpcionesAuditoria opciones = OpcionesAuditoria.Leer(configuracion);
        servicios.AddSingleton(opciones);
        servicios.AddSingleton(opciones.Huecos);
        servicios.AddSingleton<IReloj, RelojDelSistema>();

        string cadena = configuracion.GetConnectionString("Auditoria")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Auditoria.");
        servicios.AddDbContext<AuditoriaDbContext>(o => o.UseNpgsql(cadena));
        servicios.AddHealthChecks().AddDbContextCheck<AuditoriaDbContext>("base", tags: ["listo"]);

        servicios.AddScoped<RegistroAuditoriaEf>();
        servicios.AddScoped<IRegistroAuditoria>(s => s.GetRequiredService<RegistroAuditoriaEf>());
        servicios.AddScoped<ISecuenciasRegistradas>(s => s.GetRequiredService<RegistroAuditoriaEf>());
        servicios.AddScoped<ISeguimientos, SeguimientosEf>();
        servicios.AddSingleton<IAvisosHuecos, AvisosHuecosPorLog>();

        // Casos de uso que usan los adaptadores de entrada asíncronos (bus y procesos).
        servicios.AddScoped<IRegistrarEventoEntrada, RegistrarEvento>();
        servicios.AddScoped<IRevisarSecuenciasEntrada, RevisarSecuencias>();

        // Consumidor idempotente de q.auditoria con su adaptador de entrada (DA-03). Auditoría no publica: no hay
        // bandeja ni relevo, y su usuario del bus solo puede leer su cola.
        servicios.AgregarBus(configuracion);
        servicios.AgregarConsumo<AuditoriaDbContext, ManejadorEventosAuditoria>();

        servicios.AddHostedService<ProcesoRevisionHuecos>();
        servicios.AddHostedService<ProcesoParticiones>();
        return servicios;
    }
}
