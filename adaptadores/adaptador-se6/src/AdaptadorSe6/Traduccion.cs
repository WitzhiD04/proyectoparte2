using System.Security.Cryptography;
using System.Text;
using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;

namespace AdaptadorSe6;

/// <summary>
/// Traducción entre el contrato de la plataforma y el formato de SE-6 (AD-03, AD-09, DA-11, E-10). Sin base, bus ni HTTP.
/// <list type="bullet">
/// <item>EV-18 (proyecto creado): apertura financiera del proyecto con la versión inicial (1), su presupuesto y su fuente.</item>
/// <item>EV-19 (presupuesto o fuente cambiados): movimiento de presupuesto o fuente con <c>versionHistorial</c> (E-10).</item>
/// <item>EV-20 (estado o avance cambiado): solo se envía lo que interesa al sistema financiero: el costo
/// (<c>costoRegistrado</c>, E-10) y el estado cuando el proyecto se finaliza o se cancela. Se ignoran el avance, el
/// contratista, el responsable, el cronograma, los documentos y los cambios de estado intermedios.</item>
/// </list>
/// </summary>
public static class TraduccionSe6
{
    public const string Sistema = "SE-6";

    /// <summary>El movimiento financiero de un evento para SE-6, o nulo si el evento no interesa al sistema financiero.</summary>
    public static MovimientoSe6? AMovimiento(Sobre sobre)
    {
        if (EsEcoDeSe6(sobre))
        {
            return null;
        }

        switch (sobre.Tipo)
        {
            case "EV-18":
                ProyectoCreadoV1 creado = sobre.LeerDatos<ProyectoCreadoV1>();
                return new MovimientoSe6(
                    CodigoProyecto: creado.IdProyecto.ToString(),
                    VersionHistorial: creado.Proyecto.Version,
                    Clase: ClaseMovimientoSe6.Apertura,
                    TituloProyecto: creado.Nombre,
                    CodigoDesastre: creado.IdDesastre.ToString(),
                    Situacion: Situacion(creado.Proyecto.Estado),
                    Presupuesto: creado.Presupuesto,
                    FuenteFinanciacion: creado.FuenteFinanciacion,
                    MontoMovimiento: creado.Presupuesto,
                    CostoAcumulado: creado.Proyecto.CostoEjecutado,
                    OcurridoEn: sobre.InstanteEmision,
                    Autor: creado.Autor);

            case "EV-19":
                PresupuestoCambiadoV1 presupuesto = sobre.LeerDatos<PresupuestoCambiadoV1>();
                string clasePresupuesto = presupuesto.TipoCambio == TipoCambioProyecto.FUENTE
                    ? ClaseMovimientoSe6.Fuente
                    : ClaseMovimientoSe6.Presupuesto;
                decimal? presupuestoActual = presupuesto.PresupuestoNuevo ?? presupuesto.Proyecto.Presupuesto;
                string? fuenteActual = presupuesto.FuenteNueva ?? presupuesto.FuenteAnterior;
                return new MovimientoSe6(
                    CodigoProyecto: presupuesto.IdProyecto.ToString(),
                    VersionHistorial: presupuesto.VersionHistorial,
                    Clase: clasePresupuesto,
                    TituloProyecto: presupuesto.Proyecto.Nombre,
                    CodigoDesastre: presupuesto.Proyecto.IdDesastre.ToString(),
                    Situacion: Situacion(presupuesto.Proyecto.Estado),
                    Presupuesto: presupuestoActual,
                    FuenteFinanciacion: fuenteActual,
                    MontoMovimiento: presupuesto.TipoCambio == TipoCambioProyecto.PRESUPUESTO ? presupuesto.PresupuestoNuevo : null,
                    CostoAcumulado: presupuesto.Proyecto.CostoEjecutado,
                    OcurridoEn: sobre.InstanteEmision,
                    Autor: presupuesto.Autor,
                    Nota: presupuesto.Justificacion);

            case "EV-20":
                EstadoOAvanceCambiadoV1 cambio = sobre.LeerDatos<EstadoOAvanceCambiadoV1>();
                string? claseCambio = cambio.TipoCambio switch
                {
                    TipoCambioProyecto.COSTO when cambio.CostoRegistrado is > 0 => ClaseMovimientoSe6.Costo,
                    TipoCambioProyecto.ESTADO when cambio.EstadoNuevo == EstadoProyecto.FINALIZADO => ClaseMovimientoSe6.Finalizacion,
                    TipoCambioProyecto.ESTADO when cambio.EstadoNuevo == EstadoProyecto.CANCELADO => ClaseMovimientoSe6.Cancelacion,
                    _ => null,
                };
                if (claseCambio is null)
                {
                    return null;
                }
                return new MovimientoSe6(
                    CodigoProyecto: cambio.IdProyecto.ToString(),
                    VersionHistorial: cambio.VersionHistorial,
                    Clase: claseCambio,
                    TituloProyecto: cambio.Proyecto.Nombre,
                    CodigoDesastre: cambio.Proyecto.IdDesastre.ToString(),
                    Situacion: Situacion(cambio.EstadoNuevo),
                    Presupuesto: cambio.Proyecto.Presupuesto,
                    FuenteFinanciacion: null,
                    MontoMovimiento: cambio.TipoCambio == TipoCambioProyecto.COSTO ? cambio.CostoRegistrado : null,
                    CostoAcumulado: cambio.CostoEjecutado,
                    OcurridoEn: sobre.InstanteEmision,
                    Autor: cambio.Autor,
                    Nota: cambio.Justificacion);

            default:
                return null;
        }
    }

    /// <summary>
    /// Arma la petición de lote a SE-6 a partir de una lista de sobres. Filtra los que no interesan a SE-6 y descarta
    /// elementos duplicados dentro del mismo lote por (<c>idProyecto</c>, <c>versionHistorial</c>). Devuelve nulo si ningún
    /// sobre del grupo produce un movimiento financiero.
    /// </summary>
    public static PeticionLoteSe6? ALote(IEnumerable<Sobre> sobres)
    {
        List<MovimientoSe6> movimientos = [];
        HashSet<string> vistos = new(StringComparer.OrdinalIgnoreCase);

        foreach (Sobre sobre in sobres)
        {
            MovimientoSe6? movimiento = AMovimiento(sobre);
            if (movimiento is not null && vistos.Add(movimiento.ClaveElemento))
            {
                movimientos.Add(movimiento);
            }
        }

        if (movimientos.Count == 0)
        {
            return null;
        }

        return new PeticionLoteSe6(CalcularIdLote(movimientos), new LoteMovimientosSe6(movimientos));
    }

    /// <summary>Identificador determinista del lote a partir de las claves (idProyecto + versionHistorial) de sus elementos.</summary>
    public static string CalcularIdLote(IReadOnlyList<MovimientoSe6> movimientos)
    {
        string union = string.Join("|", movimientos.Select(m => m.ClaveElemento));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(union));
        return Convert.ToHexStringLower(hash)[..24];
    }

    public static bool EsEcoDeSe6(Sobre sobre) =>
        sobre.Procedencia.Fuente == Procedencia.FuenteSistemaExterno && sobre.Procedencia.SistemaOrigen == Sistema;

    public static string Situacion(EstadoProyecto estado) => estado switch
    {
        EstadoProyecto.FORMULADO => SituacionSe6.Abierta,
        EstadoProyecto.APROBADO => SituacionSe6.Comprometida,
        EstadoProyecto.EN_EJECUCION => SituacionSe6.EnEjecucion,
        EstadoProyecto.FINALIZADO => SituacionSe6.Liquidada,
        EstadoProyecto.CANCELADO => SituacionSe6.Anulada,
        _ => throw new ArgumentOutOfRangeException(nameof(estado), estado, "Estado sin equivalente en SE-6."),
    };
}
