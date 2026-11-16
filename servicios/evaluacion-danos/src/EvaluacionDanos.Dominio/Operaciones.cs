namespace EvaluacionDanos.Dominio;

// Operaciones que el evaluador registró sin conexión (FP-06): la cola inmutable del dispositivo llega tal cual.
// Todo se declara con tipos que aceptan null: así un atributo faltante o un valor raro no tumba el lote entero
// con un 400, sino que la operación sale INVALIDA con su motivo (ValidadorEvaluacion).
// Los nombres de las propiedades son los del contrato con la app y los del contenido guardado en cada versión.

public class LoteSincronizacion
{
    public string? IdDispositivo { get; set; }
    public DateTimeOffset? InstanteEnvioDispositivo { get; set; }
    public List<Operacion>? Operaciones { get; set; }
}

public class Operacion
{
    public Guid? IdOperacion { get; set; }
    public string? Tipo { get; set; }
    public int? VersionBaseActivo { get; set; }
    public EvaluacionEntrante? Evaluacion { get; set; }
}

public class EvaluacionEntrante
{
    public Guid? Id { get; set; }
    public Guid? ActivoId { get; set; }
    public ActivoNuevo? ActivoNuevo { get; set; }
    public Guid? DesastreId { get; set; }
    public DateTimeOffset? InstanteCampo { get; set; }
    public string? Evaluador { get; set; }
    public string? Organizacion { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public double? PrecisionM { get; set; }
    public string? TipoActivoCodigo { get; set; }
    public string? NivelDano { get; set; }
    public string? Descripcion { get; set; }
    public List<EvidenciaDeclarada>? Evidencias { get; set; }
}

public class ActivoNuevo
{
    public Guid? Id { get; set; }
    public string? TipoCodigo { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public string? Referencia { get; set; }
}

public class EvidenciaDeclarada
{
    public Guid? Id { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public DateTimeOffset? InstanteCaptura { get; set; }
    public string? Sha256 { get; set; }
    public long? TamanoBytes { get; set; }
}
