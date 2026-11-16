using Microsoft.EntityFrameworkCore;
using Npgsql;
using Reconstruccion.Aplicacion.CasosDeUso;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio;

namespace Reconstruccion.Infraestructura.Persistencia;

// Todos los gateways de escritura comparten el mismo DbContext dentro de una petición. Ninguno guarda
// por su cuenta: UnidadDeTrabajoEf confirma todo junto, y por eso el cambio, su historial y su evento
// quedan en una sola transacción.

public sealed class RepositorioProyectosEf : IRepositorioProyectos
{
    private readonly ReconstruccionDbContext _db;

    public RepositorioProyectosEf(ReconstruccionDbContext db)
    {
        _db = db;
    }

    public async Task<ProyectoReconstruccion?> BuscarAsync(Guid id, CancellationToken ct = default)
    {
        ProyectoFila? fila = await _db.Proyectos.Include(p => p.Evaluaciones).FirstOrDefaultAsync(p => p.Id == id, ct);
        return fila is null ? null : ADominio(fila);
    }

    public async Task<IReadOnlyList<ProyectoReconstruccion>> DeDesastreAsync(Guid idDesastre, CancellationToken ct = default)
    {
        List<ProyectoFila> filas = await _db.Proyectos.AsNoTracking().Include(p => p.Evaluaciones)
            .Where(p => p.IdDesastre == idDesastre).OrderBy(p => p.Nombre).ToListAsync(ct);
        List<ProyectoReconstruccion> proyectos = new();
        foreach (ProyectoFila fila in filas)
        {
            proyectos.Add(ADominio(fila));
        }
        return proyectos;
    }

    public async Task<IReadOnlySet<Guid>> EvaluacionesVinculadasAsync(Guid idDesastre, CancellationToken ct = default)
    {
        List<Guid> ids = await _db.Proyectos.AsNoTracking()
            .Where(p => p.IdDesastre == idDesastre)
            .SelectMany(p => p.Evaluaciones)
            .Where(e => e.Vigente)
            .Select(e => e.IdEvaluacion)
            .ToListAsync(ct);
        return new HashSet<Guid>(ids);
    }

    public Task GuardarAsync(ProyectoReconstruccion proyecto, CancellationToken ct = default)
    {
        InstantaneaProyecto datos = proyecto.AInstantanea();
        // Si el proyecto se cargó en esta petición su fila ya está en memoria; si no, es nuevo.
        ProyectoFila? fila = _db.Proyectos.Local.FirstOrDefault(p => p.Id == datos.Id);
        if (fila is null)
        {
            fila = new ProyectoFila { Id = datos.Id };
            _db.Proyectos.Add(fila);
        }
        fila.IdDesastre = datos.IdDesastre;
        fila.IdActivo = datos.IdActivo;
        fila.Nombre = datos.Nombre;
        fila.Alcance = datos.Alcance.ToString();
        fila.Estado = datos.Estado.ToString();
        fila.Presupuesto = datos.Presupuesto;
        fila.FuenteFinanciacion = datos.FuenteFinanciacion;
        fila.Responsable = datos.Responsable;
        fila.Contratista = datos.Contratista;
        fila.Inicio = datos.Inicio;
        fila.Fin = datos.Fin;
        fila.Avance = datos.Avance;
        fila.CostoEjecutado = datos.CostoEjecutado;
        fila.Version = datos.Version;

        foreach (EvaluacionOrigen evaluacion in datos.Evaluaciones)
        {
            EvaluacionOrigenFila? existente = fila.Evaluaciones.FirstOrDefault(e => e.IdEvaluacion == evaluacion.IdEvaluacion);
            if (existente is null)
            {
                fila.Evaluaciones.Add(new EvaluacionOrigenFila
                {
                    IdProyecto = datos.Id,
                    IdEvaluacion = evaluacion.IdEvaluacion,
                    IdActivo = evaluacion.IdActivo,
                    TipoActivo = evaluacion.TipoActivo,
                    NivelDano = evaluacion.NivelDano.ToString(),
                    CodigoMunicipio = evaluacion.CodigoMunicipio,
                    Vigente = evaluacion.Vigente,
                });
            }
            else
            {
                existente.Vigente = evaluacion.Vigente;
            }
        }

        foreach (RegistroAvance a in proyecto.AvancesNuevos)
        {
            _db.Avances.Add(new RegistroAvanceFila { Id = a.Id, IdProyecto = a.IdProyecto, Porcentaje = a.Porcentaje, Autor = a.Autor, Instante = a.Instante, Procedencia = a.Procedencia });
        }
        foreach (RegistroCosto c in proyecto.CostosNuevos)
        {
            _db.Costos.Add(new RegistroCostoFila { Id = c.Id, IdProyecto = c.IdProyecto, Monto = c.Monto.Monto, Concepto = c.Concepto, Autor = c.Autor, Instante = c.Instante });
        }
        foreach (Documento d in proyecto.DocumentosNuevos)
        {
            _db.Documentos.Add(new DocumentoFila { Id = d.Id, IdProyecto = d.IdProyecto, Nombre = d.Nombre, Tipo = d.Tipo, Ubicacion = d.Ubicacion, Hash = d.Hash, Autor = d.Autor, Instante = d.Instante });
        }
        return Task.CompletedTask;
    }

    public async Task<LoteCreacion?> BuscarLoteAsync(string claveIdempotencia, CancellationToken ct = default)
    {
        LoteCreacionFila? fila = await _db.Lotes.AsNoTracking().FirstOrDefaultAsync(l => l.ClaveIdempotencia == claveIdempotencia, ct);
        return fila is null ? null : new LoteCreacion(fila.ClaveIdempotencia, fila.IdDesastre, fila.Autor, fila.Instante, fila.IdsProyectos);
    }

    public Task GuardarLoteAsync(LoteCreacion lote, CancellationToken ct = default)
    {
        _db.Lotes.Add(new LoteCreacionFila
        {
            ClaveIdempotencia = lote.ClaveIdempotencia,
            IdDesastre = lote.IdDesastre,
            Autor = lote.Autor,
            Instante = lote.Instante,
            IdsProyectos = lote.IdsProyectos.ToArray(),
        });
        return Task.CompletedTask;
    }

    private static ProyectoReconstruccion ADominio(ProyectoFila fila)
    {
        List<EvaluacionOrigen> evaluaciones = new();
        foreach (EvaluacionOrigenFila e in fila.Evaluaciones)
        {
            evaluaciones.Add(new EvaluacionOrigen(e.IdEvaluacion, e.IdActivo, e.TipoActivo, Enum.Parse<NivelDano>(e.NivelDano),
                e.CodigoMunicipio, e.Vigente));
        }
        return ProyectoReconstruccion.Rehidratar(new InstantaneaProyecto(
            fila.Id, fila.IdDesastre, fila.IdActivo, fila.Nombre,
            Enum.Parse<AlcanceTerritorial>(fila.Alcance), Enum.Parse<EstadoProyecto>(fila.Estado),
            fila.Presupuesto, fila.FuenteFinanciacion, fila.Responsable, fila.Contratista,
            fila.Inicio, fila.Fin, fila.Avance, fila.CostoEjecutado, fila.Version, evaluaciones));
    }
}

/// <summary>Historial de solo inserción. Además, el usuario de la base no tiene permiso de modificar ni borrar esta tabla.</summary>
public sealed class HistorialEf : IHistorial
{
    private readonly ReconstruccionDbContext _db;

    public HistorialEf(ReconstruccionDbContext db)
    {
        _db = db;
    }

    public Task AgregarAsync(IEnumerable<EntradaHistorial> entradas, CancellationToken ct = default)
    {
        foreach (EntradaHistorial e in entradas)
        {
            _db.Historial.Add(new EntradaHistorialFila
            {
                Id = e.Id, IdProyecto = e.IdProyecto, Version = e.Version, TipoCambio = e.TipoCambio.ToString(),
                ValorAnterior = e.ValorAnterior, ValorNuevo = e.ValorNuevo, Autor = e.Autor,
                Justificacion = e.Justificacion, Instante = e.Instante, Procedencia = e.Procedencia,
            });
        }
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<EntradaHistorial>> DeProyectoAsync(Guid idProyecto, DateTimeOffset? hasta = null, CancellationToken ct = default)
    {
        IQueryable<EntradaHistorialFila> consulta = _db.Historial.AsNoTracking().Where(e => e.IdProyecto == idProyecto);
        if (hasta.HasValue)
        {
            DateTimeOffset limite = hasta.Value.ToUniversalTime();
            consulta = consulta.Where(e => e.Instante <= limite);
        }
        List<EntradaHistorialFila> filas = await consulta.OrderBy(e => e.Version).ToListAsync(ct);
        List<EntradaHistorial> entradas = new();
        foreach (EntradaHistorialFila f in filas)
        {
            entradas.Add(new EntradaHistorial(f.Id, f.IdProyecto, f.Version, Enum.Parse<TipoCambio>(f.TipoCambio),
                f.ValorAnterior, f.ValorNuevo, f.Autor, f.Justificacion, f.Instante, f.Procedencia));
        }
        return entradas;
    }
}

/// <summary>Copias locales de los datos de otros módulos. Por ahora se cargan con la siembra de datos de prueba;
/// en el paso 4 las alimentan EV-03, EV-06 y EV-08 (DA-01).</summary>
public sealed class CopiasLocalesEf : ICopiasLocales
{
    private readonly ReconstruccionDbContext _db;

    public CopiasLocalesEf(ReconstruccionDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<DesastreLocal>> DesastresAsync(CancellationToken ct = default)
    {
        List<DesastreLocalFila> filas = await _db.Desastres.AsNoTracking().Where(d => d.Registrado).OrderBy(d => d.Nombre).ToListAsync(ct);
        List<DesastreLocal> desastres = new();
        foreach (DesastreLocalFila f in filas)
        {
            desastres.Add(new DesastreLocal(f.Id, f.Nombre, f.Estado));
        }
        return desastres;
    }

    public async Task<DesastreLocal?> DesastreAsync(Guid idDesastre, CancellationToken ct = default)
    {
        DesastreLocalFila? f = await _db.Desastres.AsNoTracking().FirstOrDefaultAsync(d => d.Id == idDesastre && d.Registrado, ct);
        return f is null ? null : new DesastreLocal(f.Id, f.Nombre, f.Estado);
    }

    public async Task<IReadOnlyList<EvaluacionValidada>> EvaluacionesDeDesastreAsync(Guid idDesastre, CancellationToken ct = default)
    {
        List<EvaluacionValidadaFila> filas = await _db.Evaluaciones.AsNoTracking()
            .Where(e => e.IdDesastre == idDesastre).OrderBy(e => e.NombreActivo).ToListAsync(ct);
        List<EvaluacionValidada> evaluaciones = new();
        foreach (EvaluacionValidadaFila f in filas)
        {
            evaluaciones.Add(new EvaluacionValidada(f.Id, f.IdDesastre, f.IdActivo, f.NombreActivo, f.TipoActivo,
                Enum.Parse<NivelDano>(f.NivelDano), f.Municipio, f.CodigoMunicipio, f.Estado));
        }
        return evaluaciones;
    }
}

public sealed class UnidadDeTrabajoEf : IUnidadDeTrabajo
{
    private readonly ReconstruccionDbContext _db;

    public UnidadDeTrabajoEf(ReconstruccionDbContext db)
    {
        _db = db;
    }

    public async Task ConfirmarAsync(CancellationToken ct = default)
    {
        try
        {
            // Un solo SaveChanges = una sola transacción para todo lo pendiente.
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictoDeConcurrenciaException("Otro usuario modificó el proyecto al mismo tiempo. Vuelva a cargarlo e intente de nuevo.");
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Dos peticiones simultáneas intentaron usar la misma evaluación o la misma clave de lote.
            throw new ConflictoDeConcurrenciaException("La operación chocó con otra igual que se registró primero.");
        }
    }
}

/// <summary>Modelo de lectura del programa, en su propia base de datos.</summary>
public sealed class ProyeccionProgramaEf : IProyeccionPrograma
{
    private readonly ProgramaDbContext _db;

    public ProyeccionProgramaEf(ProgramaDbContext db)
    {
        _db = db;
    }

    public async Task AplicarAsync(FilaPrograma fila, CancellationToken ct = default)
    {
        string estado = fila.Estado.ToString();
        // Inserta o actualiza, pero solo si la versión que llega es más nueva: un evento repetido o atrasado no cambia nada.
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO proyeccion_proyectos (id_proyecto, id_desastre, nombre, estado, presupuesto, costo_ejecutado, avance, version, actualizado_en)
            VALUES ({fila.IdProyecto}, {fila.IdDesastre}, {fila.Nombre}, {estado}, {fila.Presupuesto}, {fila.CostoEjecutado}, {fila.Avance}, {fila.Version}, {fila.ActualizadoEn})
            ON CONFLICT (id_proyecto) DO UPDATE SET
                nombre = EXCLUDED.nombre, estado = EXCLUDED.estado, presupuesto = EXCLUDED.presupuesto,
                costo_ejecutado = EXCLUDED.costo_ejecutado, avance = EXCLUDED.avance, version = EXCLUDED.version,
                actualizado_en = EXCLUDED.actualizado_en
            WHERE proyeccion_proyectos.version < EXCLUDED.version", ct);
    }

    public async Task<IReadOnlyList<FilaPrograma>> DeDesastreAsync(Guid idDesastre, CancellationToken ct = default)
    {
        List<FilaProgramaFila> filas = await _db.Proyectos.AsNoTracking().Where(p => p.IdDesastre == idDesastre).ToListAsync(ct);
        List<FilaPrograma> resultado = new();
        foreach (FilaProgramaFila f in filas)
        {
            resultado.Add(new FilaPrograma(f.IdProyecto, f.IdDesastre, f.Nombre, Enum.Parse<EstadoProyecto>(f.Estado),
                f.Presupuesto, f.CostoEjecutado, f.Avance, f.Version, f.ActualizadoEn));
        }
        return resultado;
    }
}

public sealed class RelojDelSistema : IReloj
{
    public DateTimeOffset Ahora => DateTimeOffset.UtcNow;
}
