using System.Text.Json;
using GestionDesastres.Aplicacion;
using GestionDesastres.Aplicacion.CasosDeUso;
using GestionDesastres.Dominio;
using Microsoft.AspNetCore.Mvc;

namespace GestionDesastres.Api;

// Cuerpos de las peticiones. La geometría viaja como objeto GeoJSON (Polygon o MultiPolygon, WGS 84).
public sealed record CuerpoParticipante(string Organismo, string Responsabilidad);
public sealed record CuerpoZona(JsonElement? Geometria, List<string>? CodigosMunicipio, string? Fuente, string? Justificacion);
public sealed record CuerpoRegistro(string Tipo, string Nombre, string Descripcion, DateTimeOffset InstanteInicio, double Lat, double Lon,
    string Severidad, string Responsable, List<CuerpoParticipante>? Participantes, CuerpoZona Zona);
public sealed record CuerpoSeveridad(string Severidad, string Justificacion);
public sealed record CuerpoTransicion(string Estado, string? Justificacion);
public sealed record CuerpoOrganismos(string Responsable, List<CuerpoParticipante> Participantes, string Justificacion);

/// <summary>Rutas del Cuadro 15 del SAD para Gestión de Desastres. La consulta de alta carga es del servicio Consulta (AD-15).</summary>
[ApiController]
[Route("v1/desastres")]
public sealed class DesastresController : ControladorBase
{
    private static List<Participante> Participantes(List<CuerpoParticipante>? p) =>
        (p ?? []).Select(x => new Participante(x.Organismo, x.Responsabilidad)).ToList();

    private static string? Geometria(JsonElement? g) => g is { ValueKind: JsonValueKind.Object } e ? e.GetRawText() : null;

    /// <summary>RF-GD-02: registro manual. Publica EV-03, EV-05 y EV-27.</summary>
    [HttpPost]
    public async Task<IActionResult> Registrar([FromBody] CuerpoRegistro c, [FromServices] IRegistrarDesastreEntrada caso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        if (!LeerSeveridad(c.Severidad, out Severidad severidad)) return Invalido("severidad debe ser NIVEL_1, NIVEL_2, NIVEL_3 o NIVEL_4.");
        if (c.Zona is null) return Invalido("La zona o área afectada es obligatoria (Cuadro 7, atributo 5).");
        if (!LeerFuente(c.Zona.Fuente, out FuenteDelimitacion fuente)) return Invalido("fuente debe ser SISTEMA_GIS, EVALUACION_CAMPO o DECISION_AUTORIDAD.");
        PresentadorHttp<DesastreDto> p = new(StatusCodes.Status201Created);
        await caso.EjecutarAsync(new SolicitudRegistro(actor, c.Tipo, c.Nombre, c.Descripcion, c.InstanteInicio, c.Lat, c.Lon, severidad,
            c.Responsable, Participantes(c.Participantes), Geometria(c.Zona.Geometria), c.Zona.CodigosMunicipio ?? [], fuente), p, ct);
        return p.Resultado;
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Consultar(Guid id, [FromServices] IConsultarDesastreEntrada caso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<DesastreDto> p = new();
        await caso.EjecutarAsync(new SolicitudDesastre(actor, id), p, ct);
        return p.Resultado;
    }

    /// <summary>RF-GD-06: reclasificación. Publica EV-04.</summary>
    [HttpPatch("{id:guid}/severidad")]
    public async Task<IActionResult> Reclasificar(Guid id, [FromBody] CuerpoSeveridad c, [FromServices] IReclasificarEntrada caso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        if (!LeerSeveridad(c.Severidad, out Severidad severidad)) return Invalido("severidad debe ser NIVEL_1, NIVEL_2, NIVEL_3 o NIVEL_4.");
        PresentadorHttp<DesastreDto> p = new();
        await caso.EjecutarAsync(new SolicitudReclasificar(actor, id, severidad, c.Justificacion), p, ct);
        return p.Resultado;
    }

    /// <summary>RF-GD-08 y RF-GD-09: nueva versión de la delimitación. Publica EV-05.</summary>
    [HttpPut("{id:guid}/zona")]
    public async Task<IActionResult> Delimitar(Guid id, [FromBody] CuerpoZona c, [FromServices] IDelimitarEntrada caso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        if (!LeerFuente(c.Fuente, out FuenteDelimitacion fuente)) return Invalido("fuente debe ser SISTEMA_GIS, EVALUACION_CAMPO o DECISION_AUTORIDAD.");
        PresentadorHttp<DesastreDto> p = new();
        await caso.EjecutarAsync(new SolicitudDelimitar(actor, id, Geometria(c.Geometria), c.CodigosMunicipio ?? [], fuente, c.Justificacion ?? ""), p, ct);
        return p.Resultado;
    }

    /// <summary>RF-GD-07 y RF-GD-18: cambio de estado según el perfil. Publica EV-06.</summary>
    [HttpPost("{id:guid}/transiciones")]
    public async Task<IActionResult> Transicion(Guid id, [FromBody] CuerpoTransicion c, [FromServices] ICambiarEstadoEntrada caso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        if (!LeerEstado(c.Estado, out EstadoDesastre estado)) return Invalido("estado no es uno de los siete estados del ciclo de vida.");
        PresentadorHttp<DesastreDto> p = new();
        await caso.EjecutarAsync(new SolicitudTransicion(actor, id, estado, c.Justificacion), p, ct);
        return p.Resultado;
    }

    /// <summary>Evolución con autor e instante (Cuadro 15).</summary>
    [HttpGet("{id:guid}/historial")]
    public async Task<IActionResult> Historial(Guid id, [FromServices] IConsultarHistorialEntrada caso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<HistorialDto> p = new();
        await caso.EjecutarAsync(new SolicitudDesastre(actor, id), p, ct);
        return p.Resultado;
    }

    [HttpGet("{id:guid}/organismos")]
    public async Task<IActionResult> Organismos(Guid id, [FromServices] IConsultarDesastreEntrada caso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<DesastreDto> p = new();
        await caso.EjecutarAsync(new SolicitudDesastre(actor, id), p, ct);
        if (p.Resultado is ObjectResult { Value: DesastreDto d })
        {
            return Ok(new { d.Responsable, d.Participantes });
        }
        return p.Resultado;
    }

    /// <summary>RF-GD-12: organismos participantes y responsable. Publica EV-27.</summary>
    [HttpPut("{id:guid}/organismos")]
    public async Task<IActionResult> AsignarOrganismos(Guid id, [FromBody] CuerpoOrganismos c, [FromServices] IAsignarOrganismosEntrada caso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<DesastreDto> p = new();
        await caso.EjecutarAsync(new SolicitudOrganismos(actor, id, c.Responsable, Participantes(c.Participantes), c.Justificacion), p, ct);
        return p.Resultado;
    }
}

public sealed record CuerpoTipo(string Codigo, string Nombre, string Perfil, Dictionary<string, string>? CriteriosPorNivel,
    Dictionary<string, List<CuerpoParticipante>>? OrganismosPorNivel, DateTimeOffset? VigenteDesde, DateTimeOffset? VigenteHasta);
public sealed record CuerpoPerfil(string Codigo, string Nombre, int Rango, List<string> Estados);
public sealed record CuerpoPerfilPorNivel(Dictionary<string, string> Minimos);

/// <summary>Catálogo en ejecución (AD-07, QA-10). Leer: cualquier identidad. Modificar: administrador.</summary>
[ApiController]
[Route("v1/catalogos")]
public sealed class CatalogosController : ControladorBase
{
    [HttpGet("tipos-desastre")]
    public async Task<IActionResult> Tipos([FromServices] IListarTiposEntrada caso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<IReadOnlyList<TipoDesastreDto>> p = new();
        await caso.EjecutarAsync(new SolicitudCatalogo(actor), p, ct);
        return p.Resultado;
    }

    [HttpPost("tipos-desastre")]
    public Task<IActionResult> CrearTipo([FromBody] CuerpoTipo c, [FromServices] IGuardarTipoEntrada caso, CancellationToken ct) =>
        GuardarTipo(c, nuevo: true, caso, ct);

    [HttpPatch("tipos-desastre/{codigo}")]
    public Task<IActionResult> ModificarTipo(string codigo, [FromBody] CuerpoTipo c, [FromServices] IGuardarTipoEntrada caso, CancellationToken ct) =>
        GuardarTipo(c with { Codigo = codigo }, nuevo: false, caso, ct);

    private async Task<IActionResult> GuardarTipo(CuerpoTipo c, bool nuevo, IGuardarTipoEntrada caso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        Dictionary<Severidad, string> criterios = new();
        foreach ((string nivel, string texto) in c.CriteriosPorNivel ?? [])
        {
            if (!LeerSeveridad(nivel, out Severidad s)) return Invalido($"Nivel desconocido: {nivel}.");
            criterios[s] = texto;
        }
        Dictionary<Severidad, IReadOnlyList<Participante>> organismos = new();
        foreach ((string nivel, List<CuerpoParticipante> lista) in c.OrganismosPorNivel ?? [])
        {
            if (!LeerSeveridad(nivel, out Severidad s)) return Invalido($"Nivel desconocido: {nivel}.");
            organismos[s] = lista.Select(x => new Participante(x.Organismo, x.Responsabilidad)).ToList();
        }
        PresentadorHttp<TipoDesastreDto> p = new(nuevo ? StatusCodes.Status201Created : StatusCodes.Status200OK);
        await caso.EjecutarAsync(new SolicitudGuardarTipo(actor,
            new EntradaTipo(c.Codigo, c.Nombre, c.Perfil, criterios, organismos, c.VigenteDesde, c.VigenteHasta), nuevo), p, ct);
        return p.Resultado;
    }

    /// <summary>Perfil de estados y transiciones válidas de un tipo (Cuadro 15).</summary>
    [HttpGet("tipos-desastre/{codigo}/ciclo-vida")]
    public async Task<IActionResult> CicloVida(string codigo, [FromServices] IPerfilDeTipoEntrada caso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<PerfilDto> p = new();
        await caso.EjecutarAsync(new SolicitudPerfilDeTipo(actor, codigo), p, ct);
        return p.Resultado;
    }

    [HttpGet("perfiles-ciclo-vida")]
    public async Task<IActionResult> Perfiles([FromServices] IListarPerfilesEntrada caso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<IReadOnlyList<PerfilDto>> p = new();
        await caso.EjecutarAsync(new SolicitudCatalogo(actor), p, ct);
        return p.Resultado;
    }

    [HttpPost("perfiles-ciclo-vida")]
    public Task<IActionResult> CrearPerfil([FromBody] CuerpoPerfil c, [FromServices] IGuardarPerfilEntrada caso, CancellationToken ct) =>
        GuardarPerfil(c, nuevo: true, caso, ct);

    [HttpPut("perfiles-ciclo-vida/{codigo}")]
    public Task<IActionResult> ModificarPerfil(string codigo, [FromBody] CuerpoPerfil c, [FromServices] IGuardarPerfilEntrada caso, CancellationToken ct) =>
        GuardarPerfil(c with { Codigo = codigo }, nuevo: false, caso, ct);

    private async Task<IActionResult> GuardarPerfil(CuerpoPerfil c, bool nuevo, IGuardarPerfilEntrada caso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        List<EstadoDesastre> estados = new();
        foreach (string e in c.Estados ?? [])
        {
            if (!LeerEstado(e, out EstadoDesastre estado)) return Invalido($"Estado desconocido: {e}.");
            estados.Add(estado);
        }
        PresentadorHttp<PerfilDto> p = new(nuevo ? StatusCodes.Status201Created : StatusCodes.Status200OK);
        await caso.EjecutarAsync(new SolicitudGuardarPerfil(actor, c.Codigo, c.Nombre, c.Rango, estados, nuevo), p, ct);
        return p.Resultado;
    }

    [HttpGet("perfil-por-nivel")]
    public async Task<IActionResult> PerfilPorNivel([FromServices] IConsultarPerfilPorNivelEntrada caso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        PresentadorHttp<PerfilPorNivelDto> p = new();
        await caso.EjecutarAsync(new SolicitudCatalogo(actor), p, ct);
        return p.Resultado;
    }

    [HttpPut("perfil-por-nivel")]
    public async Task<IActionResult> GuardarPerfilPorNivel([FromBody] CuerpoPerfilPorNivel c, [FromServices] IGuardarPerfilPorNivelEntrada caso, CancellationToken ct)
    {
        if (!IntentarActor(out Actor actor, out IActionResult error)) return error;
        Dictionary<Severidad, string> minimos = new();
        foreach ((string nivel, string perfil) in c.Minimos ?? [])
        {
            if (!LeerSeveridad(nivel, out Severidad s)) return Invalido($"Nivel desconocido: {nivel}.");
            minimos[s] = perfil;
        }
        PresentadorHttp<PerfilPorNivelDto> p = new();
        await caso.EjecutarAsync(new SolicitudPerfilPorNivel(actor, minimos), p, ct);
        return p.Resultado;
    }
}
