using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auditoria.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "huecos_secuencia",
                columns: table => new
                {
                    productor = table.Column<string>(type: "text", nullable: false),
                    secuencia = table.Column<long>(type: "bigint", nullable: false),
                    detectado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reportado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cubierto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_huecos_secuencia", x => new { x.productor, x.secuencia });
                    table.CheckConstraint("ck_huecos_cubierto", "cubierto_en IS NULL OR cubierto_en >= detectado_en");
                    table.CheckConstraint("ck_huecos_reportado", "reportado_en IS NULL OR reportado_en >= detectado_en");
                    table.CheckConstraint("ck_huecos_secuencia", "secuencia >= 1");
                });

            migrationBuilder.CreateTable(
                name: "mensajes_procesados",
                columns: table => new
                {
                    clave_idempotencia = table.Column<string>(type: "text", nullable: false),
                    id_evento = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    procesado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mensajes_procesados", x => x.clave_idempotencia);
                });

            // Registro de auditoría particionado por mes del instante de emisión (QA-13 e: retención de 5 años o más).
            // EF Core no sabe crear una tabla particionada, así que esta tabla se escribe a mano con exactamente las
            // columnas, la llave y las restricciones del modelo (el snapshot sigue describiéndola, y una prueba verifica
            // que el modelo no tenga cambios pendientes). Los índices de abajo sí los genera EF: en una tabla
            // particionada se crean en cada partición, también en las futuras.
            // Una partición mensual se puede separar (DETACH) y archivar sin bloquear las demás ni reescribir filas.
            migrationBuilder.Sql(@"
                CREATE TABLE registro (
                    id_evento uuid NOT NULL,
                    instante_emision timestamp with time zone NOT NULL,
                    tipo text NOT NULL,
                    nombre text NOT NULL,
                    version integer NOT NULL,
                    productor text NOT NULL,
                    secuencia_productor bigint NULL,
                    instante_registro timestamp with time zone NOT NULL,
                    id_correlacion text NULL,
                    clave_idempotencia text NOT NULL,
                    actor text NULL,
                    entidad text NULL,
                    id_entidad text NULL,
                    accion text NULL,
                    resultado text NULL,
                    valor_anterior text NULL,
                    valor_nuevo text NULL,
                    procedencia jsonb NOT NULL,
                    sobre jsonb NOT NULL,
                    CONSTRAINT pk_registro PRIMARY KEY (id_evento, instante_emision),
                    CONSTRAINT ck_registro_resultado CHECK (resultado IS NULL OR resultado IN ('Exitoso', 'Rechazado')),
                    CONSTRAINT ck_registro_secuencia CHECK (secuencia_productor IS NULL OR secuencia_productor >= 1),
                    CONSTRAINT ck_registro_version CHECK (version >= 1)
                ) PARTITION BY RANGE (instante_emision);

                -- Red de seguridad: un evento con una fecha sin partición (muy antiguo, o más allá de las creadas) cae
                -- aquí en vez de ser rechazado. Auditoría no debe perder eventos (SAD §7.1).
                CREATE TABLE registro_fuera_de_rango PARTITION OF registro DEFAULT;
            ");

            migrationBuilder.CreateTable(
                name: "seguimiento_productor",
                columns: table => new
                {
                    productor = table.Column<string>(type: "text", nullable: false),
                    mayor_contiguo = table.Column<long>(type: "bigint", nullable: false),
                    mayor_recibido = table.Column<long>(type: "bigint", nullable: false),
                    revisado_hasta = table.Column<long>(type: "bigint", nullable: false),
                    revisado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seguimiento_productor", x => x.productor);
                    table.CheckConstraint("ck_seguimiento_orden", "mayor_contiguo <= revisado_hasta AND mayor_recibido <= revisado_hasta");
                });

            migrationBuilder.CreateIndex(
                name: "ix_huecos_abiertos",
                table: "huecos_secuencia",
                column: "productor",
                filter: "cubierto_en IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_huecos_reportados",
                table: "huecos_secuencia",
                column: "reportado_en",
                filter: "reportado_en IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_registro_actor",
                table: "registro",
                columns: new[] { "actor", "instante_emision" });

            migrationBuilder.CreateIndex(
                name: "ix_registro_correlacion",
                table: "registro",
                column: "id_correlacion",
                filter: "id_correlacion IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_registro_entidad",
                table: "registro",
                columns: new[] { "entidad", "id_entidad", "instante_emision" });

            migrationBuilder.CreateIndex(
                name: "ix_registro_instante",
                table: "registro",
                column: "instante_emision");

            migrationBuilder.CreateIndex(
                name: "ix_registro_secuencia",
                table: "registro",
                columns: new[] { "productor", "secuencia_productor" },
                filter: "secuencia_productor IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_registro_tipo",
                table: "registro",
                columns: new[] { "tipo", "instante_emision" });

            // Particiones mensuales. crear_particiones_registro es interna (solo el dueño la ejecuta).
            // asegurar_particiones_registro es la única puerta del rol de la aplicación al DDL: SECURITY DEFINER, sin
            // parámetros de texto, solo crea particiones de "registro", solo desde el mes en curso hacia adelante y
            // como máximo 36 meses. Así el servicio puede crear por adelantado las que falten sin tener permisos de DDL.
            // Si una partición nueva choca con filas de la partición por omisión, se avisa y se sigue con las demás.
            migrationBuilder.Sql(@"
                CREATE FUNCTION crear_particiones_registro(desde date, meses integer) RETURNS integer
                LANGUAGE plpgsql SET search_path = public, pg_temp AS $$
                DECLARE
                    inicio date := date_trunc('month', desde)::date;
                    mes date;
                    nombre text;
                    creadas integer := 0;
                BEGIN
                    FOR i IN 0 .. meses - 1 LOOP
                        mes := (inicio + make_interval(months => i))::date;
                        nombre := 'registro_' || to_char(mes, 'YYYY_MM');
                        IF to_regclass(nombre) IS NULL THEN
                            BEGIN
                                EXECUTE format('CREATE TABLE %I PARTITION OF registro FOR VALUES FROM (%L) TO (%L)',
                                    nombre, mes::timestamp AT TIME ZONE 'UTC',
                                    (mes + interval '1 month')::timestamp AT TIME ZONE 'UTC');
                                creadas := creadas + 1;
                            EXCEPTION WHEN others THEN
                                RAISE WARNING 'No se pudo crear la partición %: %', nombre, SQLERRM;
                            END;
                        END IF;
                    END LOOP;
                    RETURN creadas;
                END $$;

                CREATE FUNCTION asegurar_particiones_registro(meses_adelante integer) RETURNS integer
                LANGUAGE plpgsql SECURITY DEFINER SET search_path = public, pg_temp AS $$
                BEGIN
                    IF meses_adelante IS NULL OR meses_adelante < 1 OR meses_adelante > 36 THEN
                        RAISE EXCEPTION 'meses_adelante debe estar entre 1 y 36 (llegó %)', meses_adelante;
                    END IF;
                    -- El mes en curso y los siguientes, en UTC.
                    RETURN crear_particiones_registro((now() AT TIME ZONE 'UTC')::date, meses_adelante + 1);
                END $$;

                REVOKE ALL ON FUNCTION crear_particiones_registro(date, integer) FROM PUBLIC;
                REVOKE ALL ON FUNCTION asegurar_particiones_registro(integer) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION asegurar_particiones_registro(integer) TO auditoria_app;

                -- Las del año en curso y del siguiente quedan creadas desde el despliegue.
                SELECT crear_particiones_registro(date_trunc('year', now() AT TIME ZONE 'UTC')::date, 24);
            ");

            // Mínimo privilegio (DT-02, AD-41, QA-13 b): el registro y la marca de mensajes procesados son de solo
            // inserción. El seguimiento de secuencias es estado operativo y se actualiza, pero en los huecos solo se
            // pueden completar los instantes de reporte y de cobertura: quién faltó y cuándo se detectó no se reescribe.
            // El rol no tiene DDL, ni permisos directos sobre las particiones: todo pasa por la tabla "registro".
            migrationBuilder.Sql(@"
                GRANT SELECT, INSERT ON registro TO auditoria_app;
                GRANT SELECT, INSERT ON mensajes_procesados TO auditoria_app;
                GRANT SELECT, INSERT ON seguimiento_productor TO auditoria_app;
                GRANT UPDATE (mayor_contiguo, mayor_recibido, revisado_hasta, revisado_en) ON seguimiento_productor TO auditoria_app;
                GRANT SELECT, INSERT ON huecos_secuencia TO auditoria_app;
                GRANT UPDATE (reportado_en, cubierto_en) ON huecos_secuencia TO auditoria_app;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP FUNCTION IF EXISTS asegurar_particiones_registro(integer);
                DROP FUNCTION IF EXISTS crear_particiones_registro(date, integer);
            ");

            migrationBuilder.DropTable(
                name: "huecos_secuencia");

            migrationBuilder.DropTable(
                name: "mensajes_procesados");

            migrationBuilder.DropTable(
                name: "registro");

            migrationBuilder.DropTable(
                name: "seguimiento_productor");
        }
    }
}
