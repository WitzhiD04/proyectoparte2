package plataforma.adaptador.se8.migrador;

import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.util.Map;
import org.flywaydb.core.Flyway;
import org.flywaydb.core.api.output.MigrateResult;

/**
 * Migrador del adaptador de SE-8 (DT-19, convención *-migrador del compose). Se ejecuta una vez y termina:
 * <ol>
 * <li>Crea o actualiza el rol {@value #ROL_APP} (LOGIN, sin otros atributos). Idempotente.</li>
 * <li>Crea la base {@code adaptador_se8} si no existe. Quita el acceso de PUBLIC y da CONNECT solo al rol.</li>
 * <li>Aplica con Flyway las migraciones del esquema {@code se8} como dueño.</li>
 * <li>Concede al rol solo lo que el adaptador usa: SELECT, INSERT y UPDATE sobre las tablas y USAGE sobre las secuencias.
 *     No hay DELETE, TRUNCATE ni DDL (mínimo privilegio).</li>
 * </ol>
 * Variables: {@code ConnectionStrings__AdaptadorSe8Dueno} (formato .NET, con el usuario dueño) y
 * {@code Migracion__ClaveAdaptadorSe8App}.
 */
public final class Migrador {

    public static final String ROL_APP = "adaptador_se8_app";
    public static final String ESQUEMA = "se8";

    private Migrador() {
    }

    public static void main(String[] args) {
        try {
            CadenaConexion dueno = CadenaConexion.leer(System.getenv("ConnectionStrings__AdaptadorSe8Dueno"));
            String claveApp = System.getenv("Migracion__ClaveAdaptadorSe8App");
            MigrateResult resultado = ejecutar(dueno, claveApp);
            System.out.println("Migración terminada: " + resultado.migrationsExecuted + " migraciones aplicadas, versión "
                    + resultado.targetSchemaVersion + ".");
            System.exit(0);
        } catch (Exception e) {
            System.err.println("La migración falló: " + e.getMessage());
            e.printStackTrace(System.err);
            System.exit(1);
        }
    }

    public static MigrateResult ejecutar(CadenaConexion dueno, String claveApp) throws SQLException {
        if (claveApp == null || claveApp.isBlank()) {
            throw new IllegalArgumentException("Falta Migracion__ClaveAdaptadorSe8App.");
        }
        String base = dueno.base();
        if (!base.matches("[a-z_][a-z0-9_]*")) {
            throw new IllegalArgumentException("Nombre de base no válido: " + base);
        }
        // Rol y base: desde la base de mantenimiento, porque la del adaptador puede no existir todavía.
        try (Connection mantenimiento = abrir(dueno.conBase("postgres"))) {
            mantenimiento.setAutoCommit(true); // CREATE DATABASE no corre dentro de una transacción
            asegurarRol(mantenimiento, claveApp);
            if (!existeBase(mantenimiento, base)) {
                try (Statement s = mantenimiento.createStatement()) {
                    s.execute("CREATE DATABASE " + base);
                }
                System.out.println("Base " + base + " creada.");
            }
            try (Statement s = mantenimiento.createStatement()) {
                s.execute("REVOKE ALL ON DATABASE " + base + " FROM PUBLIC");
                s.execute("GRANT CONNECT ON DATABASE " + base + " TO " + ROL_APP);
            }
        }

        Flyway flyway = Flyway.configure()
                .dataSource(dueno.urlJdbc(), dueno.usuario(), dueno.clave())
                .schemas(ESQUEMA)
                .createSchemas(true)
                .locations("classpath:db/migration")
                .placeholders(Map.of("rolApp", ROL_APP, "esquema", ESQUEMA))
                .load();
        MigrateResult resultado = flyway.migrate();

        // Permisos después de migrar: cubren también las tablas que agregue una migración nueva.
        try (Connection conexion = abrir(dueno); Statement s = conexion.createStatement()) {
            s.execute("REVOKE CREATE ON SCHEMA public FROM PUBLIC");
            s.execute("GRANT USAGE ON SCHEMA " + ESQUEMA + " TO " + ROL_APP);
            s.execute("GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA " + ESQUEMA + " TO " + ROL_APP);
            s.execute("GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA " + ESQUEMA + " TO " + ROL_APP);
            s.execute("REVOKE ALL ON " + ESQUEMA + ".flyway_schema_history FROM " + ROL_APP);
            s.execute("ALTER ROLE " + ROL_APP + " IN DATABASE " + base + " SET search_path = " + ESQUEMA);
        }
        return resultado;
    }

    private static void asegurarRol(Connection conexion, String clave) throws SQLException {
        boolean existe;
        try (PreparedStatement p = conexion.prepareStatement("SELECT 1 FROM pg_roles WHERE rolname = ?")) {
            p.setString(1, ROL_APP);
            try (ResultSet r = p.executeQuery()) {
                existe = r.next();
            }
        }
        // CREATE ROLE no admite parámetros: la clave se escapa como literal.
        String literal = "'" + clave.replace("'", "''") + "'";
        try (Statement s = conexion.createStatement()) {
            s.execute((existe ? "ALTER ROLE " : "CREATE ROLE ") + ROL_APP
                    + " LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD " + literal);
        }
        System.out.println("Rol " + ROL_APP + " listo.");
    }

    private static boolean existeBase(Connection conexion, String base) throws SQLException {
        try (PreparedStatement p = conexion.prepareStatement("SELECT 1 FROM pg_database WHERE datname = ?")) {
            p.setString(1, base);
            try (ResultSet r = p.executeQuery()) {
                return r.next();
            }
        }
    }

    private static Connection abrir(CadenaConexion cadena) throws SQLException {
        SQLException ultimo = null;
        // La base puede tardar en aceptar conexiones aunque compose ya la marque sana: hasta 10 intentos.
        for (int intento = 1; intento <= 10; intento++) {
            try {
                return DriverManager.getConnection(cadena.urlJdbc(), cadena.usuario(), cadena.clave());
            } catch (SQLException e) {
                ultimo = e;
                try {
                    Thread.sleep(2000);
                } catch (InterruptedException interrupcion) {
                    Thread.currentThread().interrupt();
                    throw e;
                }
            }
        }
        throw ultimo;
    }
}
