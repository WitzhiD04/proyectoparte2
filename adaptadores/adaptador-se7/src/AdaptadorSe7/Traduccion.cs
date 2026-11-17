using Plataforma.Comun.Eventos;
using Plataforma.Comun.Eventos.Datos;

namespace AdaptadorSe7;

/// <summary>
/// Traducción entre el contrato de la plataforma y el formato de SE-7 (AD-03, AD-09). Sin base, bus ni HTTP.
/// <list type="bullet">
/// <item>Salida: EV-18 (proyecto creado) es el ALTA de la obra; EV-20 (estado, avance o datos cambiados) es una
/// MODIFICACION. La versión de la obra es la del proyecto en EV-18 y la del historial en EV-20, de modo que el par
/// (idProyecto, versión) es la clave de idempotencia hacia SE-7 (Cuadro 14).</item>
/// <item>Entrada: un cambio de SE-7 (AVANCE o CONTRATISTA) es un EV-24 (E-04) con la secuencia de SE-7 como marca de
/// avance y como origen de la clave de idempotencia.</item>
/// </list>
/// </summary>
public static class TraduccionSe7
{
    public const string Sistema = "SE-7";

    /// <summary>La petición a SE-7 para un evento de la cola de salida, o nula si el evento no se le envía.</summary>
    public static PeticionSe7? ASe7(Sobre sobre)
    {
        switch (sobre.Tipo)
        {
            case "EV-18":
                ProyectoCreadoV1 creado = sobre.LeerDatos<ProyectoCreadoV1>();
                return new PeticionSe7(creado.IdProyecto.ToString(), creado.Proyecto.Version, new VersionObraSe7(
                    Operacion: "ALTA",
                    Concepto: "ALTA",
                    Titulo: creado.Nombre,
                    CodigoDesastre: creado.IdDesastre.ToString(),
                    Situacion: Situacion(creado.Proyecto.Estado),
                    PorcentajeAvance: creado.Proyecto.Avance,
                    Presupuesto: creado.Presupuesto,
                    CostoEjecutado: creado.Proyecto.CostoEjecutado,
                    Municipios: creado.CodigosMunicipio,
                    Responsable: creado.Responsable,
                    FuenteRecursos: creado.FuenteFinanciacion,
                    FechaInicio: creado.Cronograma?.Inicio,
                    FechaFin: creado.Cronograma?.Fin));

            case "EV-20":
                if (EsEcoDeSe7(sobre))
                {
                    // El avance o el contratista que SE-7 reportó (EV-24) vuelve como EV-20 con procedencia SE-7. SE-7 ya
                    // lo tiene: reenviárselo no aporta nada y, si SE-7 lo reportara otra vez, haría un ciclo.
                    return null;
                }
                EstadoOAvanceCambiadoV1 cambio = sobre.LeerDatos<EstadoOAvanceCambiadoV1>();
                string concepto = cambio.TipoCambio.ToString();
                return new PeticionSe7(cambio.IdProyecto.ToString(), cambio.VersionHistorial, new VersionObraSe7(
                    Operacion: "MODIFICACION",
                    Concepto: concepto,
                    Titulo: cambio.Proyecto.Nombre,
                    CodigoDesastre: cambio.Proyecto.IdDesastre.ToString(),
                    Situacion: Situacion(cambio.EstadoNuevo),
                    PorcentajeAvance: cambio.Avance,
                    Presupuesto: cambio.Proyecto.Presupuesto,
                    CostoEjecutado: cambio.CostoEjecutado,
                    Responsable: cambio.TipoCambio == TipoCambioProyecto.RESPONSABLE ? cambio.ValorNuevo : null,
                    Contratista: cambio.TipoCambio == TipoCambioProyecto.CONTRATISTA ? cambio.ValorNuevo : null,
                    ValorAnterior: cambio.ValorAnterior,
                    ValorNuevo: cambio.ValorNuevo,
                    Nota: cambio.Justificacion));

            default:
                return null; // la cola solo recibe EV-18 y EV-20; cualquier otro tipo no es para SE-7
        }
    }

    /// <summary>EV-24 de un cambio de SE-7, o nulo si el cambio no es de una obra de la plataforma o no es válido.</summary>
    public static Sobre? AEvento(CambioObraSe7 cambio, DateTimeOffset instanteEmision)
    {
        if (!Guid.TryParse(cambio.CodigoObra, out Guid idProyecto))
        {
            return null; // obra que SE-7 administra por su cuenta: no corresponde a un proyecto de la plataforma
        }
        TipoReporteExterno tipo;
        switch (cambio.Clase)
        {
            case "AVANCE" when cambio.PorcentajeAvance is >= 0 and <= 100:
                tipo = TipoReporteExterno.AVANCE;
                break;
            case "CONTRATISTA" when !string.IsNullOrWhiteSpace(cambio.Contratista):
                tipo = TipoReporteExterno.CONTRATISTA;
                break;
            default:
                return null;
        }
        string marca = cambio.Secuencia.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string claveOrigen = $"se7:{cambio.Clase.ToLowerInvariant()}:{marca}";
        ReporteExternoProyectoV1 datos = new(idProyecto, tipo,
            tipo == TipoReporteExterno.AVANCE ? cambio.PorcentajeAvance : null,
            tipo == TipoReporteExterno.CONTRATISTA ? cambio.Contratista : null,
            cambio.OcurridoEn, claveOrigen, marca);
        // La clave sale del cambio de SE-7 (no de la marca guardada): el mismo cambio da siempre el mismo evento, y
        // Reconstrucción lo aplica una sola vez aunque llegue dos veces.
        return Sobre.Crear(CatalogoEventos.ReporteExternoProyecto, CatalogoEventos.AdaptadorSe7, claveOrigen, datos,
            instanteEmision, Procedencia.DeSistemaExterno(Sistema));
    }

    public static bool EsEcoDeSe7(Sobre sobre) =>
        sobre.Procedencia.Fuente == Procedencia.FuenteSistemaExterno && sobre.Procedencia.SistemaOrigen == Sistema;

    public static string Situacion(EstadoProyecto estado) => estado switch
    {
        EstadoProyecto.FORMULADO => SituacionSe7.Formulada,
        EstadoProyecto.APROBADO => SituacionSe7.Aprobada,
        EstadoProyecto.EN_EJECUCION => SituacionSe7.EnObra,
        EstadoProyecto.FINALIZADO => SituacionSe7.Terminada,
        EstadoProyecto.CANCELADO => SituacionSe7.Anulada,
        _ => throw new ArgumentOutOfRangeException(nameof(estado), estado, "Estado sin equivalente en SE-7."),
    };
}
