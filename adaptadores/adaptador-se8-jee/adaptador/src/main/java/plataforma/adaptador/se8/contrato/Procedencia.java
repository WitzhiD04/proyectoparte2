package plataforma.adaptador.se8.contrato;

/** Procedencia de un dato (AD-11). Los que se originan en la plataforma tienen confianza alta. */
public record Procedencia(String fuente, String sistemaOrigen, String confianza) {

    public static final String FUENTE_PLATAFORMA = "plataforma";
    public static final String FUENTE_SISTEMA_EXTERNO = "sistema-externo";

    public static final Procedencia PLATAFORMA = new Procedencia(FUENTE_PLATAFORMA, null, "ALTA");

    public static Procedencia deSistemaExterno(String sistema) {
        return new Procedencia(FUENTE_SISTEMA_EXTERNO, sistema, "ALTA");
    }
}
