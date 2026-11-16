using System.Diagnostics;
using System.Text.Json;
using GestionDesastres.Aplicacion;
using GestionDesastres.Aplicacion.CasosDeUso;
using GestionDesastres.Dominio;
using GestionDesastres.Infraestructura.Persistencia;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;
using Plataforma.Comun.Mensajeria;
using Canonico = Plataforma.Comun.Eventos;

namespace GestionDesastres.Infraestructura.Mensajeria;

/// <summary>Traducción entre los valores del dominio y las enumeraciones canónicas del contrato (E-08).</summary>
public static class MapeoCanonico
{
    public static Canonico.Severidad AlContrato(Dominio.Severidad s) => s switch
    {
        Dominio.Severidad.Nivel1 => Canonico.Severidad.NIVEL_1,
        Dominio.Severidad.Nivel2 => Canonico.Severidad.NIVEL_2,
        Dominio.Severidad.Nivel3 => Canonico.Severidad.NIVEL_3,
        Dominio.Severidad.Nivel4 => Canonico.Severidad.NIVEL_4,
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    public static Canonico.EstadoDesastre AlContrato(Dominio.EstadoDesastre e) => e switch
    {
        Dominio.EstadoDesastre.Detectado => Canonico.EstadoDesastre.DETECTADO,
        Dominio.EstadoDesastre.Registrado => Canonico.EstadoDesastre.REGISTRADO,
        Dominio.EstadoDesastre.EnEvaluacion => Canonico.EstadoDesastre.EN_EVALUACION,
        Dominio.EstadoDesastre.EnAtencion => Canonico.EstadoDesastre.EN_ATENCION,
        Dominio.EstadoDesastre.EnRecuperacion => Canonico.EstadoDesastre.EN_RECUPERACION,
        Dominio.EstadoDesastre.EnReconstruccion => Canonico.EstadoDesastre.EN_RECONSTRUCCION,
        Dominio.EstadoDesastre.Cerrado => Canonico.EstadoDesastre.CERRADO,
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };
}

/// <summary>
/// Adaptador de salida del puerto <see cref="IPublicadorHechos"/>: traduce cada entrada del historial al evento del catálogo
/// y lo deja en la bandeja del mismo DbContext, sin guardar (AD-45). El registro emite EV-03 y, como el evento trae solo la
/// versión de la zona, también EV-05 con la geometría inicial y EV-27 con los organismos iniciales, para que las copias
/// locales queden completas sin consultar a este servicio (AD-01). El cambio de perfil es interno y no emite evento.
/// </summary>
public sealed class PublicadorHechosBandeja : IPublicadorHechos
{
    public const string EncabezadoCorrelacion = "X-Id-Correlacion";
    private readonly EscritorBandeja<GestionDesastresDbContext> _bandeja;
    private readonly IHttpContextAccessor _http;

    public PublicadorHechosBandeja(EscritorBandeja<GestionDesastresDbContext> bandeja, IHttpContextAccessor http)
    {
        _bandeja = bandeja;
        _http = http;
    }

    public Task RegistrarAsync(IEnumerable<HechoDesastre> hechos, CancellationToken ct = default)
    {
        string? correlacion = _http.HttpContext?.Request.Headers[EncabezadoCorrelacion].FirstOrDefault()
            ?? Activity.Current?.TraceId.ToString() ?? _http.HttpContext?.TraceIdentifier;
        foreach (HechoDesastre hecho in hechos)
        {
            foreach (Sobre sobre in Traducir(hecho, correlacion))
            {
                _bandeja.Agregar(sobre);
            }
        }
        return Task.CompletedTask;
    }

    public static IEnumerable<Sobre> Traducir(HechoDesastre hecho, string? correlacion)
    {
        CambioDesastre c = hecho.Cambio;
        InstantaneaDesastre d = hecho.Desastre;
        string clave = $"desastre:{d.Id}:v{c.Version}";
        Sobre Crear<T>(DefinicionEvento def, string sufijo, T datos) =>
            Sobre.Crear(def, CatalogoEventos.GestionDesastres, clave + sufijo, datos, c.Instante, idCorrelacion: correlacion);

        switch (c.Tipo)
        {
            case TipoCambio.Registro:
                VersionZona zona = d.Zonas.Single(z => z.Version == 1);
                yield return Crear(CatalogoEventos.DesastreCreado, "", new DesastreCreadoV1(d.Id, d.CodigoTipo,
                    MapeoCanonico.AlContrato(c.Resultado.Severidad), zona.Version, MapeoCanonico.AlContrato(c.Resultado.Estado),
                    d.OrganismosRegistra[0], c.Resultado.Responsable, OrigenDesastre.MANUAL, d.Nombre, d.Descripcion, d.InstanteInicio,
                    new Plataforma.Comun.Eventos.Datos.Ubicacion(d.Ubicacion.Lat, d.Ubicacion.Lon), c.Version));
                yield return Crear(CatalogoEventos.ZonaActualizada, ":zona", Zona(d.Id, zona));
                yield return Crear(CatalogoEventos.OrganismosDesastreCambiados, ":organismos", Organismos(d.Id, c));
                break;
            case TipoCambio.Severidad:
                yield return Crear(CatalogoEventos.SeveridadCambiada, "", new SeveridadCambiadaV1(d.Id,
                    MapeoCanonico.AlContrato(Enum.Parse<Dominio.Severidad>(c.ValorAnterior!)), MapeoCanonico.AlContrato(c.Resultado.Severidad),
                    c.Version, c.Autor, c.Justificacion));
                break;
            case TipoCambio.Estado:
                yield return Crear(CatalogoEventos.EstadoDesastreCambiado, "", new EstadoDesastreCambiadoV1(d.Id,
                    MapeoCanonico.AlContrato(Enum.Parse<Dominio.EstadoDesastre>(c.ValorAnterior!)), MapeoCanonico.AlContrato(c.Resultado.Estado),
                    c.Autor, c.Version));
                break;
            case TipoCambio.Zona:
                int version = int.Parse(c.ValorNuevo!.TrimStart('v'));
                yield return Crear(CatalogoEventos.ZonaActualizada, "", Zona(d.Id, d.Zonas.Single(z => z.Version == version)));
                break;
            case TipoCambio.Organismos:
                yield return Crear(CatalogoEventos.OrganismosDesastreCambiados, "", Organismos(d.Id, c));
                break;
        }
    }

    private static ZonaActualizadaV1 Zona(Guid id, VersionZona z) => new(id, z.Version,
        z.GeometriaGeoJson is null ? null : JsonDocument.Parse(z.GeometriaGeoJson).RootElement.Clone(), z.CodigosMunicipio,
        z.Fuente.ToString(), z.Autor);

    private static OrganismosDesastreCambiadosV1 Organismos(Guid id, CambioDesastre c) => new(id, c.Resultado.Responsable,
        c.Resultado.Participantes.Select(p => new Plataforma.Comun.Eventos.Datos.Participante(p.Organismo, p.Responsabilidad)).ToList(),
        c.Version, c.Autor);
}

/// <summary>
/// Adaptador de entrada de <c>q.gestion-desastres</c>: EV-18 y EV-20 alimentan la copia local de proyectos (regla de cierre,
/// E-07) y EV-21 la capa de municipios (DA-07). Los demás tipos se ignoran.
/// </summary>
public sealed class ManejadorEventosGestion : IManejadorEvento
{
    private readonly ActualizarProyectoDelDesastre _proyectos;
    private readonly ActualizarCapaMunicipios _capa;
    private readonly ILogger<ManejadorEventosGestion> _log;

    public ManejadorEventosGestion(ActualizarProyectoDelDesastre proyectos, ActualizarCapaMunicipios capa, ILogger<ManejadorEventosGestion> log)
    {
        _proyectos = proyectos;
        _capa = capa;
        _log = log;
    }

    public async Task ManejarAsync(Sobre sobre, CancellationToken ct)
    {
        switch (sobre.Tipo)
        {
            case "EV-18":
                ProyectoCreadoV1 creado = sobre.LeerDatos<ProyectoCreadoV1>();
                await _proyectos.EjecutarAsync(creado.IdProyecto, creado.IdDesastre, EsFinal(creado.Proyecto.Estado), creado.Proyecto.Version, ct);
                break;
            case "EV-20":
                EstadoOAvanceCambiadoV1 cambio = sobre.LeerDatos<EstadoOAvanceCambiadoV1>();
                await _proyectos.EjecutarAsync(cambio.IdProyecto, cambio.Proyecto.IdDesastre, EsFinal(cambio.Proyecto.Estado), cambio.Proyecto.Version, ct);
                break;
            case "EV-21":
                CapaGeograficaActualizadaV1 capa = sobre.LeerDatos<CapaGeograficaActualizadaV1>();
                if (capa.Capa != CapaGeografica.MUNICIPIOS)
                {
                    _log.LogDebug("Gestión de Desastres no usa la capa {Capa}; se ignora.", capa.Capa);
                    break;
                }
                await _capa.EjecutarAsync(capa.Divisiones.Select(d => new DivisionGeografica(d.Codigo, d.Nombre, d.CodigoPadre,
                    d.Geometria.GetRawText())).ToList(), capa.VersionCapa, ct);
                break;
            default:
                _log.LogDebug("Gestión de Desastres no maneja {Tipo}; se ignora.", sobre.Tipo);
                break;
        }
    }

    private static bool EsFinal(Canonico.EstadoProyecto estado) => estado is Canonico.EstadoProyecto.FINALIZADO or Canonico.EstadoProyecto.CANCELADO;
}
