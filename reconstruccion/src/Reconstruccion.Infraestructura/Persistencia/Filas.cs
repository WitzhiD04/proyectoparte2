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

public sealed class MensajeSalidaFila
{
    public Guid Id { get; set; }
    public long Secuencia { get; set; }
    public string Tipo { get; set; } = "";
    public string ClaveIdempotencia { get; set; } = "";
    public string Contenido { get; set; } = "";
    public DateTimeOffset Instante { get; set; }
    public DateTimeOffset? PublicadoEn { get; set; }
}

public sealed class DesastreLocalFila
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Estado { get; set; } = "";
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
    public string Estado { get; set; } = "";
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
}
