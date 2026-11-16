using System.Text.Json;
using Auditoria.Aplicacion;
using Auditoria.Aplicacion.Puertos;
using Auditoria.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Auditoria.Infraestructura.Persistencia;

/// <summary>
/// Registro de auditoría sobre la tabla particionada. Solo agrega y lee: el puerto no ofrece más y el rol del servicio
/// tampoco tiene permiso para más (AD-41).
/// </summary>
public sealed class RegistroAuditoriaEf : IRegistroAuditoria, ISecuenciasRegistradas
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly AuditoriaDbContext _db;

    public RegistroAuditoriaEf(AuditoriaDbContext db)
    {
        _db = db;
    }

    public async Task<bool> ExisteAsync(Guid idEvento, CancellationToken ct = default)
    {
        // Una entrada agregada en esta misma unidad de trabajo también cuenta.
        if (_db.Registro.Local.Any(r => r.IdEvento == idEvento))
        {
            return true;
        }
        return await _db.Registro.AsNoTracking().AnyAsync(r => r.IdEvento == idEvento, ct);
    }

    public void Agregar(EntradaAuditoria e)
    {
        _db.Registro.Add(new FilaRegistro
        {
            IdEvento = e.IdEvento,
            Tipo = e.Tipo,
            Nombre = e.Nombre,
            Version = e.Version,
            Productor = e.Productor,
            SecuenciaProductor = e.SecuenciaProductor,
            InstanteEmision = e.InstanteEmision,
            InstanteRegistro = e.InstanteRegistro,
            IdCorrelacion = e.IdCorrelacion,
            ClaveIdempotencia = e.ClaveIdempotencia,
            Actor = e.Actor,
            Entidad = e.Entidad,
            IdEntidad = e.IdEntidad,
            Accion = e.Accion,
            Resultado = e.Resultado?.ToString(),
            ValorAnterior = e.ValorAnterior,
            ValorNuevo = e.ValorNuevo,
            Procedencia = JsonSerializer.Serialize(e.Procedencia, Json),
            Sobre = e.Sobre,
        });
    }

    public async Task<PaginaEntradas> ConsultarAsync(FiltroRegistro f, int pagina, int tamano, CancellationToken ct = default)
    {
        IQueryable<FilaRegistro> consulta = _db.Registro.AsNoTracking();
        if (f.Entidad is not null) consulta = consulta.Where(r => r.Entidad == f.Entidad);
        if (f.IdEntidad is not null) consulta = consulta.Where(r => r.IdEntidad == f.IdEntidad);
        if (f.Actor is not null) consulta = consulta.Where(r => r.Actor == f.Actor);
        if (f.Tipo is not null) consulta = consulta.Where(r => r.Tipo == f.Tipo);
        if (f.IdCorrelacion is not null) consulta = consulta.Where(r => r.IdCorrelacion == f.IdCorrelacion);
        // El rango usa la columna de partición: PostgreSQL solo lee las particiones de esos meses.
        if (f.Desde is not null) consulta = consulta.Where(r => r.InstanteEmision >= f.Desde.Value.ToUniversalTime());
        if (f.Hasta is not null) consulta = consulta.Where(r => r.InstanteEmision <= f.Hasta.Value.ToUniversalTime());

        long total = await consulta.LongCountAsync(ct);
        List<FilaRegistro> filas = await consulta
            // Orden cronológico estable: entre eventos del mismo instante decide la secuencia de su productor.
            .OrderBy(r => r.InstanteEmision).ThenBy(r => r.Productor).ThenBy(r => r.SecuenciaProductor).ThenBy(r => r.IdEvento)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync(ct);
        return new PaginaEntradas(filas.Select(ADominio).ToList(), total);
    }

    public async Task<IReadOnlyList<string>> ProductoresAsync(CancellationToken ct = default)
    {
        // Recorrido por saltos sobre ix_registro_secuencia: una lectura del índice por productor, no un barrido de la
        // tabla, que crece sin límite durante los años de retención.
        return await _db.Database.SqlQuery<string>($@"
            WITH RECURSIVE p(productor) AS (
                (SELECT productor FROM registro WHERE secuencia_productor IS NOT NULL ORDER BY productor LIMIT 1)
                UNION ALL
                SELECT (SELECT r.productor FROM registro r
                        WHERE r.secuencia_productor IS NOT NULL AND r.productor > p.productor
                        ORDER BY r.productor LIMIT 1)
                FROM p WHERE p.productor IS NOT NULL)
            SELECT productor AS ""Value"" FROM p WHERE productor IS NOT NULL").ToListAsync(ct);
    }

    public async Task<IReadOnlyList<long>> MayoresQueAsync(string productor, long desde, int limite, CancellationToken ct = default)
    {
        return await _db.Registro.AsNoTracking()
            .Where(r => r.Productor == productor && r.SecuenciaProductor != null && r.SecuenciaProductor > desde)
            .OrderBy(r => r.SecuenciaProductor)
            .Select(r => r.SecuenciaProductor!.Value)
            .Take(limite)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlySet<long>> RegistradasAsync(string productor, IReadOnlyCollection<long> secuencias, CancellationToken ct = default)
    {
        long[] buscadas = secuencias.ToArray();
        List<long> encontradas = await _db.Registro.AsNoTracking()
            .Where(r => r.Productor == productor && r.SecuenciaProductor != null && buscadas.Contains(r.SecuenciaProductor.Value))
            .Select(r => r.SecuenciaProductor!.Value)
            .Distinct()
            .ToListAsync(ct);
        return encontradas.ToHashSet();
    }

    private static EntradaAuditoria ADominio(FilaRegistro r) => EntradaAuditoria.Reconstituir(r.IdEvento, r.Tipo, r.Nombre,
        r.Version, r.Productor, r.SecuenciaProductor, r.InstanteEmision, r.InstanteRegistro, r.IdCorrelacion,
        r.ClaveIdempotencia,
        new Interpretacion(r.Actor, r.Entidad, r.IdEntidad, r.Accion,
            r.Resultado is null ? null : Enum.Parse<ResultadoOperacion>(r.Resultado), r.ValorAnterior, r.ValorNuevo),
        JsonSerializer.Deserialize<ProcedenciaDato>(r.Procedencia, Json) ?? new ProcedenciaDato("desconocida", null, "BAJA"),
        r.Sobre);
}

/// <summary>Estado de la secuencia de cada productor y sus huecos (R-02).</summary>
public sealed class SeguimientosEf : ISeguimientos
{
    private readonly AuditoriaDbContext _db;

    public SeguimientosEf(AuditoriaDbContext db)
    {
        _db = db;
    }

    public async Task<SeguimientoProductor?> BuscarAsync(string productor, CancellationToken ct = default)
    {
        FilaSeguimiento? fila = await _db.Seguimientos.AsNoTracking().FirstOrDefaultAsync(s => s.Productor == productor, ct);
        if (fila is null)
        {
            return null;
        }
        List<FilaHueco> abiertos = await _db.Huecos.AsNoTracking()
            .Where(h => h.Productor == productor && h.CubiertoEn == null).ToListAsync(ct);
        return Reconstituir(fila, abiertos);
    }

    public async Task GuardarAsync(SeguimientoProductor s, IReadOnlyList<HuecoSecuencia> cambiados, CancellationToken ct = default)
    {
        FilaSeguimiento? fila = await _db.Seguimientos.FirstOrDefaultAsync(x => x.Productor == s.Productor, ct);
        if (fila is null)
        {
            fila = new FilaSeguimiento { Productor = s.Productor };
            _db.Seguimientos.Add(fila);
        }
        fila.MayorContiguo = s.MayorContiguo;
        fila.MayorRecibido = s.MayorRecibido;
        fila.RevisadoHasta = s.RevisadoHasta;
        fila.RevisadoEn = s.RevisadoEn;

        if (cambiados.Count > 0)
        {
            long[] numeros = cambiados.Select(h => h.Secuencia).ToArray();
            Dictionary<long, FilaHueco> existentes = await _db.Huecos
                .Where(h => h.Productor == s.Productor && numeros.Contains(h.Secuencia))
                .ToDictionaryAsync(h => h.Secuencia, ct);
            foreach (HuecoSecuencia hueco in cambiados)
            {
                if (existentes.TryGetValue(hueco.Secuencia, out FilaHueco? existente))
                {
                    // Solo se completan los instantes que faltaban: el rol del servicio no puede cambiar los demás.
                    existente.ReportadoEn ??= hueco.ReportadoEn;
                    existente.CubiertoEn ??= hueco.CubiertoEn;
                }
                else
                {
                    _db.Huecos.Add(new FilaHueco
                    {
                        Productor = hueco.Productor,
                        Secuencia = hueco.Secuencia,
                        DetectadoEn = hueco.DetectadoEn,
                        ReportadoEn = hueco.ReportadoEn,
                        CubiertoEn = hueco.CubiertoEn,
                    });
                }
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SeguimientoProductor>> TodosAsync(CancellationToken ct = default)
    {
        List<FilaSeguimiento> filas = await _db.Seguimientos.AsNoTracking().ToListAsync(ct);
        ILookup<string, FilaHueco> abiertos = (await _db.Huecos.AsNoTracking().Where(h => h.CubiertoEn == null).ToListAsync(ct))
            .ToLookup(h => h.Productor);
        return filas.Select(f => Reconstituir(f, abiertos[f.Productor])).ToList();
    }

    public async Task<IReadOnlyList<HuecoSecuencia>> ReportadosAsync(int limite, CancellationToken ct = default)
    {
        List<FilaHueco> filas = await _db.Huecos.AsNoTracking()
            .Where(h => h.ReportadoEn != null)
            .OrderByDescending(h => h.ReportadoEn).ThenBy(h => h.Productor).ThenBy(h => h.Secuencia)
            .Take(limite)
            .ToListAsync(ct);
        return filas.Select(AHueco).ToList();
    }

    private static SeguimientoProductor Reconstituir(FilaSeguimiento f, IEnumerable<FilaHueco> abiertos) =>
        SeguimientoProductor.Reconstituir(f.Productor, f.MayorContiguo, f.MayorRecibido, f.RevisadoHasta, f.RevisadoEn,
            abiertos.Select(AHueco));

    private static HuecoSecuencia AHueco(FilaHueco h) =>
        HuecoSecuencia.Reconstituir(h.Productor, h.Secuencia, h.DetectadoEn, h.ReportadoEn, h.CubiertoEn);
}

public sealed class RelojDelSistema : IReloj
{
    public DateTimeOffset Ahora => DateTimeOffset.UtcNow;
}
