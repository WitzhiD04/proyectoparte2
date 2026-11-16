using Canonico = Plataforma.Comun.Eventos;
using Reconstruccion.Dominio;

namespace Reconstruccion.Infraestructura.Mensajeria;

/// <summary>
/// Traducción entre los valores del dominio de Reconstrucción y las enumeraciones canónicas del contrato (E-08).
/// El dominio conserva sus nombres; el formato del bus solo existe en este adaptador.
/// </summary>
public static class MapeoCanonico
{
    public static Canonico.EstadoProyecto AlContrato(EstadoProyecto estado) => estado switch
    {
        EstadoProyecto.Formulado => Canonico.EstadoProyecto.FORMULADO,
        EstadoProyecto.Aprobado => Canonico.EstadoProyecto.APROBADO,
        EstadoProyecto.EnEjecucion => Canonico.EstadoProyecto.EN_EJECUCION,
        EstadoProyecto.Finalizado => Canonico.EstadoProyecto.FINALIZADO,
        EstadoProyecto.Cancelado => Canonico.EstadoProyecto.CANCELADO,
        _ => throw new ArgumentOutOfRangeException(nameof(estado), estado, "Estado sin equivalente canónico."),
    };

    public static EstadoProyecto AlDominio(Canonico.EstadoProyecto estado) => estado switch
    {
        Canonico.EstadoProyecto.FORMULADO => EstadoProyecto.Formulado,
        Canonico.EstadoProyecto.APROBADO => EstadoProyecto.Aprobado,
        Canonico.EstadoProyecto.EN_EJECUCION => EstadoProyecto.EnEjecucion,
        Canonico.EstadoProyecto.FINALIZADO => EstadoProyecto.Finalizado,
        Canonico.EstadoProyecto.CANCELADO => EstadoProyecto.Cancelado,
        _ => throw new ArgumentOutOfRangeException(nameof(estado), estado, "Estado canónico desconocido."),
    };

    public static Canonico.AlcanceProyecto AlContrato(AlcanceTerritorial alcance) => alcance switch
    {
        AlcanceTerritorial.Municipal => Canonico.AlcanceProyecto.MUNICIPAL,
        AlcanceTerritorial.Regional => Canonico.AlcanceProyecto.REGIONAL,
        _ => throw new ArgumentOutOfRangeException(nameof(alcance), alcance, "Alcance sin equivalente canónico."),
    };

    public static Canonico.TipoCambioProyecto AlContrato(TipoCambio tipo) => tipo switch
    {
        TipoCambio.Creacion => Canonico.TipoCambioProyecto.CREACION,
        TipoCambio.Presupuesto => Canonico.TipoCambioProyecto.PRESUPUESTO,
        TipoCambio.Fuente => Canonico.TipoCambioProyecto.FUENTE,
        TipoCambio.Responsable => Canonico.TipoCambioProyecto.RESPONSABLE,
        TipoCambio.Cronograma => Canonico.TipoCambioProyecto.CRONOGRAMA,
        TipoCambio.Contratista => Canonico.TipoCambioProyecto.CONTRATISTA,
        TipoCambio.Estado => Canonico.TipoCambioProyecto.ESTADO,
        TipoCambio.Avance => Canonico.TipoCambioProyecto.AVANCE,
        TipoCambio.Costo => Canonico.TipoCambioProyecto.COSTO,
        TipoCambio.Documento => Canonico.TipoCambioProyecto.DOCUMENTO,
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo de cambio sin equivalente canónico."),
    };
}
