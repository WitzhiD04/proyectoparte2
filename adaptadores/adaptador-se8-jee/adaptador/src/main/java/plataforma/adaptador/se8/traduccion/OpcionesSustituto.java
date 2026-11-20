package plataforma.adaptador.se8.traduccion;

/**
 * Modo sustituto de Notificación (L-11, AD-66). Mientras Notificación no exista, cada EV-03 y EV-04 que llega a
 * {@code q.salida.se8} produce una alerta a un destino de difusión. Retirarlo es quitar los enlaces ev03.# y ev04.# de
 * {@code q.salida.se8} y poner {@code Sustituto__Activo=false}.
 */
public record OpcionesSustituto(boolean activo, String canal, String destino, String organismo) {

    public static final OpcionesSustituto POR_OMISION =
            new OpcionesSustituto(true, "CORREO", "sala-crisis@ungrd.gov.co", "Sala de crisis (difusión)");
}
