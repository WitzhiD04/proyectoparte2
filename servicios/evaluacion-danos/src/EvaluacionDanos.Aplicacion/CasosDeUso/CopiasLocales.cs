using EvaluacionDanos.Aplicacion.Puertos;
using EvaluacionDanos.Dominio;

namespace EvaluacionDanos.Aplicacion.CasosDeUso;

/// <summary>
/// Mantiene la copia local de los desastres con los eventos de Gestión de Desastres (E-29): EV-03, EV-05, EV-06 y
/// EV-27. Así el servicio no consulta a otro microservicio para validar una evaluación ni para la descarga
/// (AD-01, CON-6). No guarda: el consumidor confirma el efecto junto con el registro de idempotencia.
/// </summary>
public sealed class ActualizarCopiaDeDesastres(ICopiasLocales copias)
{
    public async Task DesastreCreadoAsync(Guid id, string nombre, string tipo, EstadoDesastre estado, int versionDesastre,
        CancellationToken cancelar) =>
        (await copias.DesastreAsync(id, cancelar)).AplicarCreacion(nombre, tipo, estado, versionDesastre);

    public async Task EstadoCambiadoAsync(Guid id, EstadoDesastre estado, int versionDesastre, CancellationToken cancelar) =>
        (await copias.DesastreAsync(id, cancelar)).AplicarEstado(estado, versionDesastre);

    public async Task ZonaActualizadaAsync(Guid id, int zonaVersion, IReadOnlyList<string> codigosMunicipio, CancellationToken cancelar) =>
        (await copias.DesastreAsync(id, cancelar)).AplicarZona(zonaVersion, codigosMunicipio);

    public async Task OrganismosCambiadosAsync(Guid id, string organismoResponsable, IReadOnlyList<string> participantes,
        int version, CancellationToken cancelar) =>
        (await copias.DesastreAsync(id, cancelar)).AplicarOrganismos(organismoResponsable, participantes, version);
}

/// <summary>
/// Guarda la capa geográfica que entrega el adaptador de SE-2 en EV-21 (DA-07, E-11), parte por parte. Es la copia
/// local con la que se asigna el municipio de cada activo, sin consultar a SE-2 en el camino crítico (CON-3).
/// </summary>
public sealed class CargarCapaGeografica(ICopiasLocales copias)
{
    public Task ParteRecibidaAsync(string capa, int versionCapa, int parte, int total,
        IReadOnlyList<DivisionRecibida> divisiones, CancellationToken cancelar) =>
        copias.GuardarParteDeCapaAsync(capa, versionCapa, parte, total, divisiones, cancelar);
}
