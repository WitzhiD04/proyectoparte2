using System.Text.Json;

namespace Plataforma.Comun.Eventos.Datos;

// Eventos de Gestión de Desastres. Campos del SAD (Cuadro 16) y extensiones aditivas marcadas.

/// <summary>EV-03, desastre creado.</summary>
/// <param name="Nombre">Extensión: etiqueta corta para mostrar. El SRS (Cuadro 7) solo tiene la descripción.</param>
/// <param name="Descripcion">Extensión: atributo 7 del Cuadro 7 del SRS.</param>
/// <param name="InstanteInicio">Extensión: atributo 3 del Cuadro 7 del SRS.</param>
/// <param name="Ubicacion">Extensión: atributo 4 del Cuadro 7 del SRS.</param>
/// <param name="VersionDesastre">Extensión: versión del registro, para ordenar las copias locales.</param>
public sealed record DesastreCreadoV1(
    Guid IdDesastre,
    string Tipo,
    Severidad Severidad,
    int ZonaVersion,
    EstadoDesastre Estado,
    string OrganismoRegistra,
    string Responsable,
    OrigenDesastre Origen,
    string Nombre,
    string Descripcion,
    DateTimeOffset InstanteInicio,
    Ubicacion Ubicacion,
    int VersionDesastre);

/// <summary>EV-04, severidad cambiada.</summary>
public sealed record SeveridadCambiadaV1(
    Guid IdDesastre,
    Severidad SeveridadAnterior,
    Severidad SeveridadNueva,
    int Version,
    string Autor,
    string Motivo);

/// <summary>EV-05, zona actualizada.</summary>
/// <param name="Geometria">GeoJSON (Polygon o MultiPolygon) de la zona, o nulo si la zona es solo una lista de municipios.</param>
/// <param name="CodigosMunicipio">Extensión (DA-07): municipios DIVIPOLA que forman o intersecan la zona.</param>
public sealed record ZonaActualizadaV1(
    Guid IdDesastre,
    int ZonaVersion,
    JsonElement? Geometria,
    IReadOnlyList<string> CodigosMunicipio,
    string FuenteGeometria,
    string Autor);

/// <summary>EV-06, estado del desastre cambiado. Dispara FP-08 cuando el estado nuevo es EN_RECONSTRUCCION.</summary>
/// <param name="VersionDesastre">Extensión: versión del registro, para ordenar las copias locales.</param>
public sealed record EstadoDesastreCambiadoV1(
    Guid IdDesastre,
    EstadoDesastre EstadoAnterior,
    EstadoDesastre EstadoNuevo,
    string Autor,
    int VersionDesastre);

/// <summary>EV-27 (E-25), organismos participantes del desastre.</summary>
public sealed record OrganismosDesastreCambiadosV1(
    Guid IdDesastre,
    string OrganismoResponsable,
    IReadOnlyList<Participante> Participantes,
    int Version,
    string Autor);

public sealed record Participante(string Organismo, string Responsabilidad);

/// <summary>EV-21 (E-11), capa geográfica entregada por el adaptador de SE-2. Una capa grande se publica por partes.</summary>
public sealed record CapaGeograficaActualizadaV1(
    CapaGeografica Capa,
    int VersionCapa,
    IReadOnlyList<Division> Divisiones,
    string Fuente,
    int Parte,
    int Total);
