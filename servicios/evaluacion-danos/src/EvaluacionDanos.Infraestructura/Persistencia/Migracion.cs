using EvaluacionDanos.Aplicacion;
using EvaluacionDanos.Aplicacion.CasosDeUso;
using EvaluacionDanos.Dominio;
using EvaluacionDanos.Infraestructura.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Plataforma.Comun.Mensajeria;
using Plataforma.Comun.Migraciones;

namespace EvaluacionDanos.Infraestructura.Persistencia;

/// <summary>
/// Despliegue del esquema de Evaluación de Daños (DT-19, corrige H-15). Corre con "migrar" y con la cadena del dueño:
/// crea el rol de la aplicación, aplica las migraciones (que incluyen PostGIS y los permisos de mínimo privilegio,
/// DT-02, corrige H-14) y, con "--sembrar", carga el catálogo inicial de tipos de activo.
/// </summary>
public static class MigracionEvaluacionDanos
{
    public const string RolApp = "evaluacion_danos_app";

    public static async Task EjecutarAsync(IConfiguration configuracion, bool sembrar, ILogger log, CancellationToken ct = default)
    {
        string dueno = Exigir(configuracion, "ConnectionStrings:EvaluacionDanosDueno");
        RolBase[] roles = [new(RolApp, Exigir(configuracion, "Migracion:ClaveEvaluacionDanosApp"))];
        BaseDelServicio[] bases = [new("evaluacion_danos", () => new EvaluacionDanosDbContext(Opciones(dueno)))];
        // Los roles se crean conectado a la base de mantenimiento: la del servicio aún no existe la primera vez.
        string administracion = new NpgsqlConnectionStringBuilder(dueno) { Database = "postgres" }.ConnectionString;
        await Migrador.EjecutarAsync(administracion, roles, bases, sembrar: false, log, ct);
        if (sembrar)
        {
            await using EvaluacionDanosDbContext db = new(Opciones(dueno));
            int nuevos = await SiembraCatalogo.EjecutarAsync(db, ct);
            log.LogInformation("Catálogo inicial: {Nuevos} tipos de activo cargados.", nuevos);
        }
    }

    public static DbContextOptions<EvaluacionDanosDbContext> Opciones(string cadena) =>
        new DbContextOptionsBuilder<EvaluacionDanosDbContext>().UseNpgsql(cadena, o => o.UseNetTopologySuite()).Options;

    private static string Exigir(IConfiguration configuracion, string clave) =>
        configuracion[clave] is { Length: > 0 } valor ? valor : throw new InvalidOperationException($"Falta {clave}.");
}

/// <summary>Para "dotnet ef migrations add": no se conecta a ninguna base.</summary>
public sealed class FabricaDisenoEvaluacionDanos : IDesignTimeDbContextFactory<EvaluacionDanosDbContext>
{
    public EvaluacionDanosDbContext CreateDbContext(string[] args) =>
        new(MigracionEvaluacionDanos.Opciones("Host=localhost;Database=evaluacion_danos"));
}

/// <summary>
/// Catálogo inicial: los 11 tipos de activo del SRS (RF-ED-04) con sus criterios por nivel (RF-ED-05), los mismos del
/// prototipo. Es idempotente: solo agrega los tipos que faltan. Cada tipo nuevo deja su EV-25 en la bandeja (E-16),
/// igual que si lo creara el administrador, para que Consulta de Daños conozca el catálogo inicial.
/// Los desastres ya no se siembran: llegan por eventos de Gestión de Desastres (E-29).
/// </summary>
public static class SiembraCatalogo
{
    public static async Task<int> EjecutarAsync(EvaluacionDanosDbContext db, CancellationToken ct = default)
    {
        var existentes = (await (from t in db.TiposActivo select t.Codigo).ToListAsync(ct)).ToHashSet();
        var ahora = new RelojDelSistema().Ahora();
        var bandeja = new EscritorBandeja<EvaluacionDanosDbContext>(db);
        int nuevos = 0;
        foreach (TipoActivo tipo in Tipos())
        {
            if (existentes.Contains(tipo.Codigo))
            {
                continue;
            }
            tipo.ActualizadoEn = ahora;
            db.TiposActivo.Add(tipo);
            var hecho = new TipoActivoActualizado(tipo.Codigo, tipo.Nombre, CatalogoDto.De(tipo).Criterios, tipo.Version, ahora);
            bandeja.Agregar(PublicadorHechosBandeja.Traducir(hecho, "siembra-catalogo"));
            nuevos++;
        }
        await db.SaveChangesAsync(ct);
        return nuevos;
    }

    public static List<TipoActivo> Tipos() =>
    [
        Tipo("vivienda", "Vivienda",
            "Sin afectación visible",
            "Daños menores en acabados, vidrios o tejas",
            "Fisuras no estructurales",
            "Daño estructural, no habitable",
            "Colapso total de la vivienda"),
        Tipo("edificio_publico", "Edificio público",
            "Sin afectación visible",
            "Daños en acabados; el edificio sigue en servicio",
            "Fisuras no estructurales; servicio parcial",
            "Daño estructural; se cierra al público",
            "Colapso total o parcial del edificio"),
        Tipo("hospital", "Hospital",
            "Sin afectación; atención normal",
            "Daños menores; atención normal",
            "Daño en áreas no críticas; atención reducida",
            "Daño estructural o en áreas críticas; se evacúa",
            "Colapso; el hospital deja de operar"),
        Tipo("escuela", "Escuela",
            "Sin afectación visible",
            "Daños menores en acabados o mobiliario",
            "Fisuras no estructurales; aulas parcialmente utilizables",
            "Daño estructural; no se pueden dictar clases",
            "Colapso total de la edificación"),
        Tipo("puente", "Puente",
            "Sin afectación; tránsito normal",
            "Daños menores en barandas o capa de rodadura",
            "Daño en elementos no portantes",
            "Daño portante, sin tránsito",
            "Colapso del puente"),
        Tipo("carretera", "Carretera",
            "Sin afectación; tránsito normal",
            "Baches o derrumbes menores; tránsito normal",
            "Pérdida parcial de banca; paso a un carril",
            "Pérdida de banca o derrumbe; vía cerrada",
            "Tramo destruido; requiere reconstrucción"),
        Tipo("sistema_agua", "Sistema de agua",
            "Sin afectación; servicio normal",
            "Fugas menores; servicio normal",
            "Daño en redes secundarias; servicio intermitente",
            "Daño en conducción o planta; sin servicio",
            "Infraestructura destruida"),
        Tipo("red_electrica", "Red eléctrica",
            "Sin afectación; servicio normal",
            "Daños menores; cortes breves",
            "Postes o transformadores dañados; cortes por sectores",
            "Subestación o línea principal dañada; sin servicio",
            "Infraestructura destruida"),
        Tipo("red_telecomunicaciones", "Red de telecomunicaciones",
            "Sin afectación; servicio normal",
            "Daños menores; servicio degradado",
            "Antenas o cableado dañados; cobertura parcial",
            "Nodo principal dañado; sin servicio",
            "Infraestructura destruida"),
        Tipo("instalacion_industrial", "Instalación industrial",
            "Sin afectación; opera normal",
            "Daños menores; opera normal",
            "Daño en áreas no críticas; operación reducida",
            "Daño estructural o riesgo de derrame; se detiene",
            "Colapso o pérdida total de la instalación"),
        Tipo("otra_infraestructura", "Otra infraestructura crítica",
            "Sin afectación; opera normal",
            "Daños menores; opera normal",
            "Daño parcial; operación reducida",
            "Daño grave; deja de operar",
            "Destrucción total"),
    ];

    private static TipoActivo Tipo(string codigo, string nombre,
        string sinDano, string leve, string moderado, string grave, string destruccion)
    {
        string[] textos = [sinDano, leve, moderado, grave, destruccion];
        var tipo = new TipoActivo { Codigo = codigo, Nombre = nombre, Version = 1 };
        var niveles = Enum.GetValues<NivelDano>();
        for (int i = 0; i < niveles.Length; i++)
        {
            tipo.Criterios.Add(new CriterioNivel { TipoActivoCodigo = codigo, Nivel = niveles[i], Criterio = textos[i] });
        }
        return tipo;
    }
}
