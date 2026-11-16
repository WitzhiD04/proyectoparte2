using Microsoft.EntityFrameworkCore;
using Reconstruccion.Aplicacion.CasosDeUso;

namespace Reconstruccion.Infraestructura.Persistencia;

/// <summary>
/// Escritura de las copias locales (DA-01) con inserción o actualización protegida por versión: un evento repetido o
/// que llega después de uno más nuevo no cambia nada. Corre dentro de la transacción del consumidor idempotente.
/// </summary>
public sealed class EscrituraCopiasLocalesEf : IEscrituraCopiasLocales
{
    private readonly ReconstruccionDbContext _db;
    public EscrituraCopiasLocalesEf(ReconstruccionDbContext db) { _db = db; }

    public Task AplicarAsync(DesastreRecibido d, CancellationToken ct = default) => _db.Database.ExecuteSqlInterpolatedAsync($@"
        INSERT INTO desastres_locales (id, registrado, nombre, tipo, estado, version, actualizado_en)
        VALUES ({d.Id}, true, {d.Nombre}, {d.Tipo}, {d.Estado}, {d.Version}, {d.Instante})
        ON CONFLICT (id) DO UPDATE SET registrado = true, nombre = EXCLUDED.nombre, tipo = EXCLUDED.tipo,
            estado = CASE WHEN desastres_locales.version < EXCLUDED.version THEN EXCLUDED.estado ELSE desastres_locales.estado END,
            version = GREATEST(desastres_locales.version, EXCLUDED.version),
            actualizado_en = GREATEST(desastres_locales.actualizado_en, EXCLUDED.actualizado_en)", ct);

    // Si el cambio de estado llega antes que el registro, la fila queda sin registrar (no se ofrece) hasta que llegue EV-03.
    public Task AplicarAsync(EstadoDesastreRecibido e, CancellationToken ct = default) => _db.Database.ExecuteSqlInterpolatedAsync($@"
        INSERT INTO desastres_locales (id, registrado, nombre, estado, version, actualizado_en)
        VALUES ({e.Id}, false, '', {e.Estado}, {e.Version}, {e.Instante})
        ON CONFLICT (id) DO UPDATE SET
            estado = CASE WHEN desastres_locales.version < EXCLUDED.version THEN EXCLUDED.estado ELSE desastres_locales.estado END,
            version = GREATEST(desastres_locales.version, EXCLUDED.version),
            actualizado_en = GREATEST(desastres_locales.actualizado_en, EXCLUDED.actualizado_en)", ct);

    public Task AplicarAsync(EvaluacionRecibida e, CancellationToken ct = default)
    {
        string nivel = e.NivelDano.ToString();
        return _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO evaluaciones_validadas (id, id_desastre, id_activo, nombre_activo, tipo_activo, nivel_dano, municipio,
                codigo_municipio, estado, version, actualizado_en)
            VALUES ({e.Id}, {e.IdDesastre}, {e.IdActivo}, {e.ReferenciaActivo}, {e.TipoActivo}, {nivel},
                COALESCE((SELECT nombre FROM municipios_locales WHERE codigo = {e.CodigoMunicipio}), {e.CodigoMunicipio}),
                {e.CodigoMunicipio}, {e.Estado}, {e.Version}, {e.Instante})
            ON CONFLICT (id) DO UPDATE SET id_desastre = EXCLUDED.id_desastre, id_activo = EXCLUDED.id_activo,
                nombre_activo = EXCLUDED.nombre_activo, tipo_activo = EXCLUDED.tipo_activo, nivel_dano = EXCLUDED.nivel_dano,
                municipio = EXCLUDED.municipio, codigo_municipio = EXCLUDED.codigo_municipio, estado = EXCLUDED.estado,
                version = EXCLUDED.version, actualizado_en = EXCLUDED.actualizado_en
            WHERE evaluaciones_validadas.version < EXCLUDED.version
               OR (evaluaciones_validadas.version = EXCLUDED.version AND evaluaciones_validadas.actualizado_en < EXCLUDED.actualizado_en)", ct);
    }

    public async Task AplicarAsync(IReadOnlyList<MunicipioRecibido> municipios, int versionCapa, CancellationToken ct = default)
    {
        foreach (MunicipioRecibido m in municipios)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO municipios_locales (codigo, nombre, codigo_departamento, version_capa)
                VALUES ({m.Codigo}, {m.Nombre}, {m.CodigoDepartamento}, {versionCapa})
                ON CONFLICT (codigo) DO UPDATE SET nombre = EXCLUDED.nombre, codigo_departamento = EXCLUDED.codigo_departamento,
                    version_capa = EXCLUDED.version_capa", ct);
            // Las evaluaciones que llegaron antes que la capa muestran el código; desde ahora, el nombre.
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE evaluaciones_validadas SET municipio = {m.Nombre} WHERE codigo_municipio = {m.Codigo} AND municipio <> {m.Nombre}", ct);
        }
    }
}
