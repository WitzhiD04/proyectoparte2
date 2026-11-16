namespace Reconstruccion.Infraestructura.Persistencia;

// Modelo de persistencia: clases planas que copian las tablas. Las entidades del dominio no se guardan
// directamente; los repositorios convierten entre estas filas y las entidades.

public sealed class ProyectoFila
{
    public Guid Id { get; set; }
    public Guid IdDesastre { get; set; }
    public Guid IdActivo { get; set; }
    public string Nombre { get; set; } = "";
    public string Alcance { get; set; } = "";
    public string Estado { get; set; } = "";
    public decimal? Presupuesto { get; set; }
    public string? FuenteFinanciacion { get; set; }
    public string? Responsable { get; set; }
    public string? Contratista { get; set; }
    public DateOnly? Inicio { get; set; }
    public DateOnly? Fin { get; set; }
    public decimal Avance { get; set; }
    public decimal CostoEjecutado { get; set; }
    public int Version { get; set; }
    public List<EvaluacionOrigenFila> Evaluaciones { get; set; } = new();
}

public sealed class EvaluacionOrigenFila
{
    public Guid IdProyecto { get; set; }
    public Guid IdEvaluacion { get; set; }
    public Guid IdActivo { get; set; }
    public string TipoActivo { get; set; } = "";
    public string NivelDano { get; set; } = "";
    public string CodigoMunicipio { get; set; } = "";
    public bool Vigente { get; set; }
}

public sealed class EntradaHistorialFila
{
    public Guid Id { get; set; }
    public Guid IdProyecto { get; set; }
    public int Version { get; set; }
    public string TipoCambio { get; set; } = "";
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
    public string Autor { get; set; } = "";
    public string Justificacion { get; set; } = "";
    public DateTimeOffset Instante { get; set; }
    public string Procedencia { get; set; } = "";
}

public sealed class RegistroAvanceFila
{
    public Guid Id { get; set; }
    public Guid IdProyecto { get; set; }
    public decimal Porcentaje { get; set; }
    public string Autor { get; set; } = "";
    public DateTimeOffset Instante { get; set; }
    public string Procedencia { get; set; } = "";
}

public sealed class RegistroCostoFila
{
    public Guid Id { get; set; }
    public Guid IdProyecto { get; set; }
    public decimal Monto { get; set; }
    public string Concepto { get; set; } = "";
    public string Autor { get; set; } = "";
    public DateTimeOffset Instante { get; set; }
}

public sealed class DocumentoFila
{
    public Guid Id { get; set; }
    public Guid IdProyecto { get; set; }
    public string Nombre { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Ubicacion { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Autor { get; set; } = "";
    public DateTimeOffset Instante { get; set; }
}

public sealed class LoteCreacionFila
{
    public string ClaveIdempotencia { get; set; } = "";
    public Guid IdDesastre { get; set; }
    public string Autor { get; set; } = "";
    public DateTimeOffset Instante { get; set; }
    public Guid[] IdsProyectos { get; set; } = Array.Empty<Guid>();
}

/// <summary>
/// Copia local de un desastre (DA-01), alimentada por EV-03 y EV-06. <see cref="Registrado"/> es falso mientras solo se
/// conoce un cambio de estado que llegó antes que el registro; la versión protege el estado contra el desorden.
/// </summary>
public sealed class DesastreLocalFila
{
    public Guid Id { get; set; }
    public bool Registrado { get; set; }
    public string Nombre { get; set; } = "";
    public string? Tipo { get; set; }
    public string Estado { get; set; } = "";
    public int Version { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; }
}

/// <summary>Nombre de un municipio de la capa de SE-2 (EV-21): las candidatas y los proyectos muestran el nombre.</summary>
public sealed class MunicipioLocalFila
{
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string? CodigoDepartamento { get; set; }
    public int VersionCapa { get; set; }
}

public sealed class EvaluacionValidadaFila
{
    public Guid Id { get; set; }
    public Guid IdDesastre { get; set; }
    public Guid IdActivo { get; set; }
    public string NombreActivo { get; set; } = "";
    public string TipoActivo { get; set; } = "";
    public string NivelDano { get; set; } = "";
    public string Municipio { get; set; } = "";
    public string CodigoMunicipio { get; set; } = "";
    public string Estado { get; set; } = "";
    /// <summary>Versión de la evaluación en Evaluación de Daños (EV-08): solo una más nueva cambia la copia.</summary>
    public int Version { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; }
}

public sealed class FilaProgramaFila
{
    public Guid IdProyecto { get; set; }
    public Guid IdDesastre { get; set; }
    public string Nombre { get; set; } = "";
    public string Estado { get; set; } = "";
    public decimal Presupuesto { get; set; }
    public decimal CostoEjecutado { get; set; }
    public decimal Avance { get; set; }
    public int Version { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; }
}
