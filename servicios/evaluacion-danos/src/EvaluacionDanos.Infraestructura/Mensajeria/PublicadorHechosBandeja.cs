using System.Diagnostics;
using EvaluacionDanos.Aplicacion;
using EvaluacionDanos.Aplicacion.Puertos;
using EvaluacionDanos.Infraestructura.Persistencia;
using Microsoft.AspNetCore.Http;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;
using Canon = Plataforma.Comun.Eventos;
using Dominio = EvaluacionDanos.Dominio;
using InstantaneaEvaluacion = EvaluacionDanos.Aplicacion.InstantaneaEvaluacion;

namespace EvaluacionDanos.Infraestructura.Mensajeria;

/// <summary>
/// Traducción entre las enumeraciones del servicio y las canónicas del contrato (E-08). Coinciden por nombre; se
/// traduce de forma explícita para que un valor nuevo en un lado no viaje al otro sin que el contrato lo admita.
/// </summary>
public static class MapeoCanonico
{
    public static Canon.NivelDano AlContrato(Dominio.NivelDano nivel) => Enum.Parse<Canon.NivelDano>(nivel.ToString());

    /// <summary>El estado del servicio ya incluye DUPLICADO, como la enumeración canónica.</summary>
    public static Canon.EstadoEvaluacion AlContrato(Dominio.EstadoEvaluacion estado) => Enum.Parse<Canon.EstadoEvaluacion>(estado.ToString());

    public static Canon.TipoConflicto AlContrato(Dominio.TipoConflicto tipo) => Enum.Parse<Canon.TipoConflicto>(tipo.ToString());

    /// <summary>El EstadoConflicto del servicio corresponde al EstadoRevision canónico.</summary>
    public static EstadoRevision AlContrato(Dominio.EstadoConflicto estado) => Enum.Parse<EstadoRevision>(estado.ToString());

    public static Canon.EstadoValidacion AlContrato(Dominio.EstadoValidacion estado) => Enum.Parse<Canon.EstadoValidacion>(estado.ToString());

    public static Canon.DecisionConflicto AlContrato(Dominio.DecisionConflicto decision) => Enum.Parse<Canon.DecisionConflicto>(decision.ToString());

    public static Canon.MotivoActualizacion AlContrato(Dominio.MotivoActualizacion motivo) => Enum.Parse<Canon.MotivoActualizacion>(motivo.ToString());

    public static Dominio.EstadoDesastre AlDominio(Canon.EstadoDesastre estado) => Enum.Parse<Dominio.EstadoDesastre>(estado.ToString());

    /// <summary>
    /// Código canónico de un tipo de activo: el del catálogo en mayúsculas ("vivienda" → "VIVIENDA"). El contrato
    /// (EV-25) exige mayúsculas; la app y la base usan minúsculas. La correspondencia es de uno a uno
    /// (CatalogoTipos.CodigoValido solo admite minúsculas).
    /// </summary>
    public static string TipoActivoAlContrato(string codigo) => codigo.ToUpperInvariant();

    /// <summary>Código DIVIPOLA del departamento: los dos primeros dígitos del municipio.</summary>
    public static string Departamento(string codigoMunicipio) => codigoMunicipio[..2];

    public static DateTimeOffset Instante(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
}

/// <summary>
/// Adaptador de salida del puerto <see cref="IPublicadorHechos"/>. Traduce cada hecho al evento del catálogo con el
/// sobre canónico (EV-08, EV-09, EV-10, EV-22, EV-25 y EV-26; enmiendas E-02, E-03, E-05, E-06, E-13 y E-16) y lo deja
/// en la bandeja de salida del mismo DbContext, sin guardar: el caso de uso lo confirma junto con el cambio (AD-45,
/// corrige H-01). El escritor valida contra el esquema: un evento inválido no sale.
/// </summary>
public sealed class PublicadorHechosBandeja(EscritorBandeja<EvaluacionDanosDbContext> bandeja, IHttpContextAccessor http)
    : IPublicadorHechos
{
    public const string EncabezadoCorrelacion = "X-Id-Correlacion";

    public void Registrar(Hecho hecho) => bandeja.Agregar(Traducir(hecho, Correlacion()));

    /// <summary>Un hecho, un evento. Público para las pruebas del contrato.</summary>
    public static Sobre Traducir(Hecho hecho, string? correlacion)
    {
        DateTimeOffset instante = MapeoCanonico.Instante(hecho.Instante);
        switch (hecho)
        {
            case EvaluacionSincronizada h:
            {
                InstantaneaEvaluacion e = h.Evaluacion;
                EvaluacionSincronizadaV1 datos = new(e.IdEvaluacion, h.IdDispositivo, MapeoCanonico.Instante(h.InstanteLocal),
                    instante, h.EnConflicto, h.VersionesConservadas, e.IdActivo, e.IdDesastre,
                    MapeoCanonico.TipoActivoAlContrato(e.TipoActivoCodigo), MapeoCanonico.AlContrato(e.NivelDano),
                    new Ubicacion(e.Lat, e.Lon), e.PrecisionM, e.CodigoMunicipio, MapeoCanonico.AlContrato(e.Estado),
                    e.VersionEvaluacion, e.VersionActivo);
                return Crear(CatalogoEventos.EvaluacionSincronizada,
                    $"evaluacion:{e.IdEvaluacion}:v{e.VersionEvaluacion}:sincronizada", datos, instante, correlacion);
            }
            case ConflictoDetectado h:
            {
                ConflictoDetectadoV1 datos = new(h.IdConflicto, h.IdsEvaluaciones, h.Regla,
                    h.Versiones.Select(v => new VersionConservada(v.IdEvaluacion, v.NumeroVersion, v.Vigente)).ToList(),
                    EstadoRevision.PENDIENTE, h.IdActivo, MapeoCanonico.AlContrato(h.Tipo));
                return Crear(CatalogoEventos.ConflictoDetectado, $"conflicto:{h.IdConflicto}:detectado", datos, instante, correlacion);
            }
            case ValidacionCambiada h:
            {
                InstantaneaEvaluacion e = h.Evaluacion;
                EvaluacionValidadaV1 datos = new(e.IdEvaluacion, h.IdDesastre, MapeoCanonico.TipoActivoAlContrato(e.TipoActivoCodigo),
                    MapeoCanonico.AlContrato(e.NivelDano), new Ubicacion(e.Lat, e.Lon), h.Evaluador, h.Organizacion,
                    e.VersionEvaluacion, e.IdActivo, h.ReferenciaActivo, e.CodigoMunicipio,
                    MapeoCanonico.Departamento(e.CodigoMunicipio), MapeoCanonico.AlContrato(h.Estado), h.ValidadoPor, instante);
                return Crear(CatalogoEventos.EvaluacionValidada,
                    $"evaluacion:{e.IdEvaluacion}:v{e.VersionEvaluacion}:validacion", datos, instante, correlacion);
            }
            case EvaluacionActualizada h:
            {
                EvaluacionActualizadaV1 datos = new(h.Evaluacion.IdEvaluacion, MapeoCanonico.AlContrato(h.Motivo), h.Autor,
                    Instantanea(h.Evaluacion));
                return Crear(CatalogoEventos.EvaluacionActualizada,
                    $"evaluacion:{h.Evaluacion.IdEvaluacion}:v{h.Evaluacion.VersionEvaluacion}:actualizada", datos, instante, correlacion);
            }
            case ConflictoResuelto h:
            {
                ConflictoResueltoV1 datos = new(h.IdConflicto, MapeoCanonico.AlContrato(h.Decision), h.Vigente.IdEvaluacion,
                    h.Vigente.VersionEvaluacion, h.DecididoPor, h.Justificacion, Instantanea(h.Vigente));
                return Crear(CatalogoEventos.ConflictoResuelto, $"conflicto:{h.IdConflicto}:resuelto", datos, instante, correlacion);
            }
            case TipoActivoActualizado h:
            {
                string codigo = MapeoCanonico.TipoActivoAlContrato(h.Codigo);
                TipoActivoActualizadoV1 datos = new(codigo, h.Nombre,
                    h.Criterios.Select(c => new CriterioNivel(MapeoCanonico.AlContrato(c.Nivel), c.Criterio)).ToList(), h.Version);
                return Crear(CatalogoEventos.TipoActivoActualizado, $"tipo-activo:{codigo}:v{h.Version}", datos, instante, correlacion);
            }
            default:
                throw new NotSupportedException($"No hay evento del catálogo para el hecho {hecho.GetType().Name}.");
        }
    }

    private static Canon.Datos.InstantaneaEvaluacion Instantanea(InstantaneaEvaluacion e) => new(
        e.IdEvaluacion, e.IdActivo, e.IdDesastre, MapeoCanonico.TipoActivoAlContrato(e.TipoActivoCodigo),
        MapeoCanonico.AlContrato(e.NivelDano), new Ubicacion(e.Lat, e.Lon), e.PrecisionM, e.CodigoMunicipio,
        MapeoCanonico.AlContrato(e.Estado), e.VersionEvaluacion, e.VersionActivo);

    private static Sobre Crear<TDatos>(DefinicionEvento definicion, string clave, TDatos datos, DateTimeOffset instante,
        string? correlacion) =>
        Sobre.Crear(definicion, CatalogoEventos.EvaluacionDanos, clave, datos, instante, idCorrelacion: correlacion);

    /// <summary>Correlación de la petición: el encabezado que traiga, o el identificador de la traza actual (DA-20).</summary>
    private string? Correlacion()
    {
        HttpContext? contexto = http.HttpContext;
        string? encabezado = contexto?.Request.Headers[EncabezadoCorrelacion].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(encabezado))
        {
            return encabezado;
        }
        return Activity.Current?.TraceId.ToString() ?? contexto?.TraceIdentifier;
    }
}
