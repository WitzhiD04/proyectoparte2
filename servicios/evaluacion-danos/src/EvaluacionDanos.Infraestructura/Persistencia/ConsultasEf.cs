using EvaluacionDanos.Aplicacion;
using EvaluacionDanos.Aplicacion.CasosDeUso;
using EvaluacionDanos.Aplicacion.Puertos;
using EvaluacionDanos.Dominio;
using Microsoft.EntityFrameworkCore;

namespace EvaluacionDanos.Infraestructura.Persistencia;

/// <summary>Adaptador del puerto <see cref="IConsultasEvaluaciones"/>: lecturas sin seguimiento sobre EF Core.</summary>
public sealed class ConsultasEf(EvaluacionDanosDbContext db) : IConsultasEvaluaciones
{
    public async Task<List<TipoActivoDto>> TiposActivoAsync(CancellationToken cancelar)
    {
        var tipos = await (
            from t in db.TiposActivo.AsNoTracking().Include(t => t.Criterios)
            orderby t.Nombre
            select t).ToListAsync(cancelar);
        return tipos.Select(CatalogoDto.De).ToList();
    }

    public async Task<List<DesastreDto>> DesastresAsync(bool soloAbiertos, Guid? soloEste, CancellationToken cancelar)
    {
        var desastres = await (
            from d in db.Desastres.AsNoTracking()
            where d.Registrado
                  && (!soloAbiertos || d.Estado != EstadoDesastre.CERRADO)
                  && (soloEste == null || d.Id == soloEste)
            orderby d.Nombre
            select d).ToListAsync(cancelar);
        return desastres.Select(d => new DesastreDto(d.Id, d.Nombre, d.Tipo, d.Estado, d.CodigosMunicipio)).ToList();
    }

    public Task<List<ActivoDto>> ActivosAsync(IReadOnlyCollection<string>? municipios, string? departamento, DateTime? desde,
        CancellationToken cancelar)
    {
        var consulta = db.Activos.AsNoTracking().Where(a => a.FusionadoEn == null);
        if (municipios != null)
        {
            var lista = municipios.ToList();
            consulta = consulta.Where(a => lista.Contains(a.CodigoMunicipio));
        }
        if (departamento != null)
        {
            // Los dos primeros dígitos del código DIVIPOLA del municipio son el departamento.
            consulta = consulta.Where(a => a.CodigoMunicipio.StartsWith(departamento));
        }
        if (desde is DateTime instante)
        {
            consulta = consulta.Where(a => a.ActualizadoEn >= instante);
        }
        return (from a in consulta
                orderby a.Referencia
                select new ActivoDto(a.Id, a.TipoActivoCodigo, a.Lat, a.Lon, a.Referencia, a.Version, a.CodigoMunicipio))
            .ToListAsync(cancelar);
    }

    /// <summary>Tres consultas para toda la bandeja, sin importar cuántos conflictos tenga (corrige H-18).</summary>
    public async Task<List<ConflictoDto>> ConflictosAsync(EstadoConflicto estado, CancellationToken cancelar)
    {
        var conflictos = await (
            from c in db.Conflictos.AsNoTracking()
            where c.Estado == estado
            orderby c.Instante
            select c).ToListAsync(cancelar);

        var idsVersiones = conflictos.Select(c => c.VersionEntranteId).ToList();
        var entrantes = await (
            from v in db.EvaluacionVersiones.AsNoTracking()
            where idsVersiones.Contains(v.Id)
            select v).ToDictionaryAsync(v => v.Id, cancelar);

        var idsVigentes = conflictos.Where(c => c.EvaluacionVigenteId != null).Select(c => c.EvaluacionVigenteId!.Value)
            .Distinct().ToList();
        var versionesVigentes = (await (
            from v in db.EvaluacionVersiones.AsNoTracking()
            where idsVigentes.Contains(v.EvaluacionId)
            select v).ToListAsync(cancelar)).ToLookup(v => v.EvaluacionId);

        var resultado = new List<ConflictoDto>();
        foreach (var c in conflictos)
        {
            // La versión vigente es la última NO en conflicto de la evaluación vigente.
            EvaluacionVersion? vigente = null;
            if (c.EvaluacionVigenteId is Guid vigenteId)
            {
                var deLaVigente = versionesVigentes[vigenteId].OrderByDescending(v => v.NumeroVersion).ToList();
                vigente = deLaVigente.FirstOrDefault(v => !v.EnConflicto) ?? deLaVigente.FirstOrDefault();
            }
            resultado.Add(new ConflictoDto(c.Id, c.ActivoId, c.Tipo, c.Estado, c.Regla, c.Instante,
                vigente is null ? null : ADto(vigente), ADto(entrantes[c.VersionEntranteId]),
                c.Decision, c.DecididoPor, c.Justificacion, c.InstanteResolucion));
        }
        return resultado;
    }

    public Task<List<EvaluacionResumenDto>> EvaluacionesDeDispositivoAsync(string idDispositivo, CancellationToken cancelar)
    {
        var consulta =
            from e in db.Evaluaciones.AsNoTracking()
            where e.IdDispositivo == idDispositivo
            orderby e.InstanteServidor, e.Id
            select new EvaluacionResumenDto(
                e.Id, e.ActivoId, e.DesastreId, e.IdDispositivo, e.Estado, e.NivelDano,
                e.InstanteCampo, e.InstanteServidor,
                (from v in db.EvaluacionVersiones where v.EvaluacionId == e.Id select v).Count(),
                (from x in db.Evidencias where x.EvaluacionId == e.Id select x).Count(),
                (from x in db.Evidencias where x.EvaluacionId == e.Id && x.Subida select x).Count());
        return consulta.ToListAsync(cancelar);
    }

    private static VersionEvaluacionDto ADto(EvaluacionVersion v) =>
        new(v.Id, v.EvaluacionId, v.NumeroVersion, v.Autor, v.IdDispositivo, v.VersionActivoBase, v.EnConflicto,
            v.Instante, v.ContenidoJson, v.Motivo, v.Detalle);
}
