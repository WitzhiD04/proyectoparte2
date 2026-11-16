using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Mensajeria;

namespace Plataforma.Comun.Adaptadores;

/// <summary>
/// Marca de avance de un flujo de entrada (AD-14): el último cambio del sistema externo que ya quedó en la bandeja de
/// salida. Se guarda en la base del adaptador (DA-11: base "adaptadores", un esquema por adaptador) porque los
/// adaptadores son efímeros (AD-25) y no tienen estado local.
/// </summary>
public sealed class FilaMarcaAvance
{
    /// <summary>Nombre del flujo, por ejemplo "se7.cambios".</summary>
    public string Flujo { get; set; } = "";
    /// <summary>Marca opaca del sistema externo; nula antes del primer cambio.</summary>
    public string? Valor { get; set; }
    /// <summary>Cambios externos recibidos por el flujo (traducidos o no).</summary>
    public long Cambios { get; set; }
    public DateTimeOffset ActualizadaEn { get; set; }
}

public static class ModeloAdaptadores
{
    public const string TablaMarcas = "marcas_avance";

    /// <summary>Mapeo de la marca de avance en el DbContext del adaptador.</summary>
    public static ModelBuilder ConfigurarMarcasAvance(this ModelBuilder modelo)
    {
        modelo.Entity<FilaMarcaAvance>(e =>
        {
            e.ToTable(TablaMarcas);
            e.HasKey(x => x.Flujo);
            e.Property(x => x.Flujo).HasColumnName("flujo").HasMaxLength(100);
            e.Property(x => x.Valor).HasColumnName("valor").HasMaxLength(200);
            e.Property(x => x.Cambios).HasColumnName("cambios");
            e.Property(x => x.ActualizadaEn).HasColumnName("actualizada_en");
        });
        return modelo;
    }
}

/// <summary>Puerto del ciclo de entrada: la marca de avance y la bandeja de salida, en una sola unidad de trabajo.</summary>
public interface IAlmacenEntrada
{
    Task<string?> LeerMarcaAsync(string flujo, CancellationToken ct);

    /// <summary>
    /// En una transacción: deja los eventos en la bandeja de salida y mueve la marca de <paramref name="anterior"/> a
    /// <paramref name="nueva"/> (AD-45). Así un cambio no se pierde (la marca no avanza sin su evento) ni se duplica (el
    /// evento no se escribe sin que la marca avance). Devuelve falso, sin escribir nada, si la marca ya no era
    /// <paramref name="anterior"/>: otra instancia confirmó esa página.
    /// </summary>
    Task<bool> ConfirmarAsync(string flujo, string? anterior, string nueva, int cambios, IReadOnlyList<Sobre> eventos,
        CancellationToken ct);
}

/// <summary>Implementación con EF Core sobre el DbContext del adaptador (bandeja de comun/Mensajeria).</summary>
public sealed class AlmacenEntradaBandeja<TContexto> : IAlmacenEntrada where TContexto : DbContext
{
    private readonly TContexto _db;
    private readonly EscritorBandeja<TContexto> _bandeja;
    private readonly TimeProvider _reloj;

    public AlmacenEntradaBandeja(TContexto db, EscritorBandeja<TContexto> bandeja, TimeProvider? reloj = null)
    {
        _db = db;
        _bandeja = bandeja;
        _reloj = reloj ?? TimeProvider.System;
    }

    public async Task<string?> LeerMarcaAsync(string flujo, CancellationToken ct)
    {
        FilaMarcaAvance? fila = await _db.Set<FilaMarcaAvance>().AsNoTracking().SingleOrDefaultAsync(m => m.Flujo == flujo, ct);
        return fila?.Valor;
    }

    public async Task<bool> ConfirmarAsync(string flujo, string? anterior, string nueva, int cambios, IReadOnlyList<Sobre> eventos,
        CancellationToken ct)
    {
        string tabla = Tabla();
        DateTimeOffset ahora = _reloj.GetUtcNow();
        await using var transaccion = await _db.Database.BeginTransactionAsync(ct);

        // La fila se crea la primera vez; el bloqueo serializa a dos instancias que confirman a la vez. El nombre de la
        // tabla sale del modelo (no de un dato externo); los valores van como parámetros.
        string crear = "INSERT INTO " + tabla + " (flujo, valor, cambios, actualizada_en) VALUES ({0}, NULL, 0, {1}) ON CONFLICT (flujo) DO NOTHING";
        string bloquear = "SELECT * FROM " + tabla + " WHERE flujo = {0} FOR UPDATE";
        await _db.Database.ExecuteSqlRawAsync(crear, [flujo, ahora], ct);
        FilaMarcaAvance fila = (await _db.Set<FilaMarcaAvance>().FromSqlRaw(bloquear, flujo).ToListAsync(ct)).Single();
        if (fila.Valor != anterior)
        {
            await transaccion.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            return false;
        }

        // Un evento cuya clave ya está en la bandeja no se escribe otra vez (por ejemplo, si la marca se restauró de un
        // respaldo anterior): la clave de idempotencia sale del cambio externo, no de la marca.
        List<string> claves = eventos.Select(e => e.ClaveIdempotencia).ToList();
        HashSet<string> existentes = (await _db.Set<FilaBandeja>().Where(b => claves.Contains(b.ClaveIdempotencia))
            .Select(b => b.ClaveIdempotencia).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        foreach (Sobre evento in eventos.Where(e => !existentes.Contains(e.ClaveIdempotencia)))
        {
            _bandeja.Agregar(evento);
        }

        fila.Valor = nueva;
        fila.Cambios += cambios;
        fila.ActualizadaEn = ahora;
        await _db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
        return true;
    }

    private string Tabla()
    {
        IEntityType tipo = _db.Model.FindEntityType(typeof(FilaMarcaAvance))
            ?? throw new InvalidOperationException("El DbContext no mapea la marca de avance: llame a ConfigurarMarcasAvance().");
        string? esquema = tipo.GetSchema();
        string nombre = tipo.GetTableName()!;
        return esquema is null ? $"\"{nombre}\"" : $"\"{esquema}\".\"{nombre}\"";
    }
}
