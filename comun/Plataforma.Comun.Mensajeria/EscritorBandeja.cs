using Microsoft.EntityFrameworkCore;
using Plataforma.Comun.Eventos;

namespace Plataforma.Comun.Mensajeria;

/// <summary>
/// Agrega eventos a la bandeja de salida del DbContext del servicio, sin guardar. El servicio los confirma en su
/// propia unidad de trabajo, en la misma transacción que el cambio que los origina (AD-45, QA-15 b).
/// </summary>
public sealed class EscritorBandeja<TContexto> where TContexto : DbContext
{
    private readonly TContexto _db;
    private readonly ValidadorContrato _validador = ValidadorContrato.Instancia;
    // El escritor vive lo que la unidad de trabajo (ámbito de la petición): el contador da el orden de los hechos.
    private int _orden;

    public EscritorBandeja(TContexto db)
    {
        _db = db;
    }

    /// <summary>Valida el sobre contra el contrato y lo deja listo para guardarse. Un evento inválido no sale nunca.</summary>
    public void Agregar(Sobre sobre)
    {
        if (sobre.SecuenciaProductor is not null)
        {
            throw new InvalidOperationException("La secuencia del productor la asigna el relevo, no quien escribe el evento.");
        }
        _validador.Validar(sobre);
        DefinicionEvento definicion = CatalogoEventos.PorTipo(sobre.Tipo);
        _db.Set<FilaBandeja>().Add(new FilaBandeja
        {
            Id = sobre.IdEvento,
            Tipo = sobre.Tipo,
            ClaveRuteo = definicion.ClaveRuteo,
            ClaveIdempotencia = sobre.ClaveIdempotencia,
            IdCorrelacion = sobre.IdCorrelacion,
            Sobre = sobre.Serializar(),
            Instante = sobre.InstanteEmision,
            Orden = _orden++,
        });
    }
}
