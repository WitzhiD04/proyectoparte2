namespace Plataforma.Comun.Eventos.Datos;

/// <summary>
/// EV-07, notificación despachada (SAD Cuadro 16). La publica Notificación cuando cambia el estado de entrega de una
/// notificación: al recibir el acuse de SE-8 (ENTREGADA) o al agotar los intentos (FALLIDA). Extensiones de E-37:
/// IdDesastre, Severidad e InstanteAcuse.
/// </summary>
public sealed record NotificacionDespachadaV1(
    Guid IdNotificacion,
    Guid IdEventoOrigen,
    IReadOnlyList<string> OrganismosDestino,
    CanalNotificacion Canal,
    EstadoEntrega EstadoEntrega,
    int Intentos,
    Guid IdDesastre,
    Severidad Severidad,
    DateTimeOffset? InstanteAcuse);

/// <summary>
/// EV-28 (E-37), envío solicitado: lo que viaja por la cola de salida de SE-8 hacia su adaptador (SAD, vista de
/// componentes de CU-1). Un mensaje por intento; el adaptador no conoce suscripciones ni organismos (AD-13).
/// </summary>
public sealed record EnvioNotificacionSolicitadoV1(
    Guid IdNotificacion,
    int Intento,
    Guid IdEventoOrigen,
    Guid IdDesastre,
    string Organismo,
    CanalNotificacion Canal,
    string Destino,
    string Asunto,
    string Mensaje,
    Severidad Severidad);

/// <summary>
/// EV-29 (E-37), acuse de SE-8: el adaptador informa el resultado de un intento. Notificación lo aplica y publica EV-07.
/// </summary>
public sealed record AcuseNotificacionV1(
    Guid IdNotificacion,
    int Intento,
    EstadoEntrega Resultado,
    string? Motivo,
    DateTimeOffset Instante,
    string? ClaveOrigen);
