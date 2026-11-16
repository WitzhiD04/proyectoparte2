using GestionDesastres.Dominio;

namespace GestionDesastres.Aplicacion;

public sealed record ZonaDto(int Version, DateTimeOffset VigenteDesde, string? Geometria, IReadOnlyList<string> CodigosMunicipio,
    string Fuente, string Autor, string Justificacion);

public sealed record ParticipanteDto(string Organismo, string Responsabilidad);

/// <summary>Estado vigente de un desastre, para operar sobre él (la consulta de alta carga la atiende el servicio Consulta, AD-15).</summary>
public sealed record DesastreDto(
    Guid Id, string Tipo, string Nombre, string Descripcion, DateTimeOffset InstanteInicio, double Lat, double Lon,
    string Severidad, string Estado, string Perfil, IReadOnlyList<string> OrganismosRegistra, IReadOnlyList<string> Fuentes,
    string Responsable, IReadOnlyList<ParticipanteDto> Participantes, ZonaDto ZonaVigente, int Version,
    DateTimeOffset InstanteActualizacion, IReadOnlyList<string> TransicionesPosibles);

public sealed record CambioDto(int Version, string Tipo, string? ValorAnterior, string? ValorNuevo, string Autor, string? Organizacion,
    string Justificacion, DateTimeOffset Instante);

public sealed record HistorialDto(Guid IdDesastre, IReadOnlyList<CambioDto> Cambios, IReadOnlyList<ZonaDto> Zonas);

public sealed record TipoDesastreDto(string Codigo, string Nombre, string Perfil, IReadOnlyDictionary<string, string> CriteriosPorNivel,
    IReadOnlyDictionary<string, IReadOnlyList<ParticipanteDto>> OrganismosPorNivel, DateTimeOffset VigenteDesde, DateTimeOffset? VigenteHasta,
    bool Vigente);

public sealed record TransicionDto(string Origen, string Destino, string Condicion, string Observacion);

public sealed record PerfilDto(string Codigo, string Nombre, int Rango, IReadOnlyList<string> Estados, IReadOnlyList<TransicionDto> Transiciones);

public sealed record PerfilPorNivelDto(IReadOnlyDictionary<string, string> Minimos);

public static class Mapeos
{
    public static ZonaDto ADto(VersionZona z) => new(z.Version, z.VigenteDesde, z.GeometriaGeoJson, z.CodigosMunicipio, z.Fuente.ToString(),
        z.Autor, z.Justificacion);

    public static DesastreDto ADto(Desastre d, PerfilCicloVida? perfil) => new(
        d.Id, d.CodigoTipo, d.Nombre, d.Descripcion, d.InstanteInicio, d.Ubicacion.Lat, d.Ubicacion.Lon, d.Severidad.ToString(),
        d.Estado.ToString(), d.CodigoPerfil, d.OrganismosRegistra, d.Fuentes, d.Responsable,
        d.Participantes.Select(p => new ParticipanteDto(p.Organismo, p.Responsabilidad)).ToList(), ADto(d.ZonaVigente), d.Version,
        d.InstanteActualizacion, Posibles(d, perfil));

    /// <summary>Transiciones que el perfil admite desde el estado actual, más la reapertura si está cerrado.</summary>
    private static IReadOnlyList<string> Posibles(Desastre d, PerfilCicloVida? perfil)
    {
        List<string> posibles = perfil?.Transiciones.Where(t => t.Origen == d.Estado).Select(t => t.Destino.ToString()).ToList() ?? [];
        if (d.Estado == EstadoDesastre.Cerrado)
        {
            posibles.Add(EstadoDesastre.EnAtencion.ToString());
        }
        return posibles;
    }

    public static CambioDto ADto(CambioDesastre c) => new(c.Version, c.Tipo.ToString(), c.ValorAnterior, c.ValorNuevo, c.Autor, c.Organizacion,
        c.Justificacion, c.Instante);

    public static TipoDesastreDto ADto(TipoDesastre t, DateTimeOffset ahora) => new(t.Codigo, t.Nombre, t.CodigoPerfil,
        t.CriteriosPorNivel.ToDictionary(p => p.Key.ToString(), p => p.Value),
        t.OrganismosPorNivel.ToDictionary(p => p.Key.ToString(),
            p => (IReadOnlyList<ParticipanteDto>)p.Value.Select(x => new ParticipanteDto(x.Organismo, x.Responsabilidad)).ToList()),
        t.VigenteDesde, t.VigenteHasta, t.EsVigente(ahora));

    public static PerfilDto ADto(PerfilCicloVida p) => new(p.Codigo, p.Nombre, p.Rango, p.Estados.Select(e => e.ToString()).ToList(),
        p.Transiciones.Select(t => new TransicionDto(t.Origen.ToString(), t.Destino.ToString(), t.Condicion.ToString(), t.Observacion)).ToList());
}
