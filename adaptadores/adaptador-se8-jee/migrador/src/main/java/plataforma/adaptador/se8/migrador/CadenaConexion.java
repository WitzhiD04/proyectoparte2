package plataforma.adaptador.se8.migrador;

import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;

/**
 * Cadena de conexión con el formato de los servicios .NET ("Host=db;Port=5432;Database=x;Username=y;Password=z").
 * El adaptador de SE-8 recibe la misma variable (ConnectionStrings__AdaptadorSe8) que recibiría un adaptador .NET,
 * para que el compose y Kubernetes sigan una sola convención. Aquí se traduce a una URL JDBC.
 */
public record CadenaConexion(String host, int puerto, String base, String usuario, String clave) {

    public static CadenaConexion leer(String cadena) {
        if (cadena == null || cadena.isBlank()) {
            throw new IllegalArgumentException("La cadena de conexión está vacía.");
        }
        Map<String, String> partes = new LinkedHashMap<>();
        for (String parte : cadena.split(";")) {
            int igual = parte.indexOf('=');
            if (igual > 0) {
                partes.put(parte.substring(0, igual).trim().toLowerCase(Locale.ROOT), parte.substring(igual + 1).trim());
            }
        }
        String host = exigir(partes, "host");
        int puerto = Integer.parseInt(partes.getOrDefault("port", "5432"));
        String base = exigir(partes, "database");
        String usuario = partes.containsKey("username") ? partes.get("username") : exigir(partes, "user id");
        String clave = partes.getOrDefault("password", "");
        return new CadenaConexion(host, puerto, base, usuario, clave);
    }

    public String urlJdbc() {
        return "jdbc:postgresql://" + host + ":" + puerto + "/" + base;
    }

    public CadenaConexion conBase(String otra) {
        return new CadenaConexion(host, puerto, otra, usuario, clave);
    }

    private static String exigir(Map<String, String> partes, String clave) {
        String valor = partes.get(clave);
        if (valor == null || valor.isBlank()) {
            throw new IllegalArgumentException("Falta " + clave + " en la cadena de conexión.");
        }
        return valor;
    }
}
