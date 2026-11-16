using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace ConsultaDanos.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

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

            migrationBuilder.CreateTable(
                name: "p_bandeja",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    pendientes = table.Column<int>(type: "integer", nullable: false),
                    mas_viejo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dato_al = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_p_bandeja", x => x.id);
                    table.CheckConstraint("ck_p_bandeja_unica", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "p_conflictos",
                columns: table => new
                {
                    id_conflicto = table.Column<Guid>(type: "uuid", nullable: false),
                    id_activo = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo = table.Column<string>(type: "text", nullable: true),
                    id_evaluacion_entrante = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<string>(type: "text", nullable: false),
                    decision = table.Column<string>(type: "text", nullable: true),
                    id_activo_vigente = table.Column<Guid>(type: "uuid", nullable: true),
                    detectado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resuelto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_p_conflictos", x => x.id_conflicto);
                    table.CheckConstraint("ck_p_conflictos_estado", "estado IN ('PENDIENTE','RESUELTO')");
                });

            migrationBuilder.CreateTable(
                name: "p_dano_por_zona",
                columns: table => new
                {
                    id_desastre = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo_municipio = table.Column<string>(type: "text", nullable: false),
                    tipo_activo = table.Column<string>(type: "text", nullable: false),
                    nivel_dano = table.Column<string>(type: "text", nullable: false),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    actualizado_al = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_p_dano_por_zona", x => new { x.id_desastre, x.codigo_municipio, x.tipo_activo, x.nivel_dano });
                    table.CheckConstraint("ck_p_dano_por_zona_cantidad", "cantidad >= 0");
                    table.CheckConstraint("ck_p_dano_por_zona_nivel", "nivel_dano IN ('SIN_DANO','LEVE','MODERADO','GRAVE','DESTRUCCION_TOTAL')");
                });

            migrationBuilder.CreateTable(
                name: "p_desastres",
                columns: table => new
                {
                    id_desastre = table.Column<Guid>(type: "uuid", nullable: false),
                    dato_al = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_p_desastres", x => x.id_desastre);
                });

            migrationBuilder.CreateTable(
                name: "p_evaluacion_vigente",
                columns: table => new
                {
                    id_activo = table.Column<Guid>(type: "uuid", nullable: false),
                    id_evaluacion = table.Column<Guid>(type: "uuid", nullable: true),
                    id_desastre = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo_activo = table.Column<string>(type: "text", nullable: true),
                    nivel_dano = table.Column<string>(type: "text", nullable: true),
                    codigo_municipio = table.Column<string>(type: "text", nullable: true),
                    ubicacion = table.Column<Point>(type: "geometry(Point,4326)", nullable: true),
                    precision_m = table.Column<double>(type: "double precision", nullable: true),
                    estado_evaluacion = table.Column<string>(type: "text", nullable: true),
                    version_evaluacion = table.Column<int>(type: "integer", nullable: false),
                    version_activo = table.Column<int>(type: "integer", nullable: false),
                    origen_contenido = table.Column<short>(type: "smallint", nullable: false),
                    estado_validacion = table.Column<string>(type: "text", nullable: true),
                    validada = table.Column<bool>(type: "boolean", nullable: false),
                    fusionado = table.Column<bool>(type: "boolean", nullable: false),
                    fusionado_en = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_al = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_p_evaluacion_vigente", x => x.id_activo);
                    table.CheckConstraint("ck_p_evaluacion_vigente_nivel", "nivel_dano IS NULL OR nivel_dano IN ('SIN_DANO','LEVE','MODERADO','GRAVE','DESTRUCCION_TOTAL')");
                    table.CheckConstraint("ck_p_evaluacion_vigente_origen", "origen_contenido BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_p_evaluacion_vigente_validacion", "estado_validacion IS NULL OR estado_validacion IN ('VALIDADA','RECHAZADA','PENDIENTE')");
                    table.CheckConstraint("ck_p_evaluacion_vigente_version", "version_activo >= -1");
                });

            migrationBuilder.CreateTable(
                name: "p_historial_activo",
                columns: table => new
                {
                    id_activo = table.Column<Guid>(type: "uuid", nullable: false),
                    id_evaluacion = table.Column<Guid>(type: "uuid", nullable: false),
                    version_evaluacion = table.Column<int>(type: "integer", nullable: false),
                    origen = table.Column<string>(type: "text", nullable: false),
                    instante = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    nivel_dano = table.Column<string>(type: "text", nullable: false),
                    tipo_activo = table.Column<string>(type: "text", nullable: false),
                    estado_evaluacion = table.Column<string>(type: "text", nullable: false),
                    estado_validacion = table.Column<string>(type: "text", nullable: true),
                    en_conflicto = table.Column<bool>(type: "boolean", nullable: false),
                    id_desastre = table.Column<Guid>(type: "uuid", nullable: true),
                    codigo_municipio = table.Column<string>(type: "text", nullable: false),
                    autor = table.Column<string>(type: "text", nullable: true),
                    detalle = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_p_historial_activo", x => new { x.id_activo, x.id_evaluacion, x.version_evaluacion, x.origen });
                    table.CheckConstraint("ck_p_historial_activo_nivel", "nivel_dano IN ('SIN_DANO','LEVE','MODERADO','GRAVE','DESTRUCCION_TOTAL')");
                });

            migrationBuilder.CreateTable(
                name: "p_integraciones",
                columns: table => new
                {
                    sistema = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    desde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    mensajes_en_cola = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_p_integraciones", x => x.sistema);
                });

            migrationBuilder.CreateTable(
                name: "p_municipios",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    codigo_departamento = table.Column<string>(type: "text", nullable: true),
                    geometria = table.Column<Geometry>(type: "geometry(Geometry,4326)", nullable: false),
                    version_capa = table.Column<int>(type: "integer", nullable: false),
                    actualizado_al = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_p_municipios", x => x.codigo);
                });

            migrationBuilder.CreateTable(
                name: "p_tipos_activo",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_p_tipos_activo", x => x.codigo);
                });

            migrationBuilder.CreateTable(
                name: "p_validaciones",
                columns: table => new
                {
                    id_evaluacion = table.Column<Guid>(type: "uuid", nullable: false),
                    id_activo = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    validado_por = table.Column<string>(type: "text", nullable: false),
                    instante_validacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_p_validaciones", x => x.id_evaluacion);
                    table.CheckConstraint("ck_p_validaciones_estado", "estado IN ('VALIDADA','RECHAZADA','PENDIENTE')");
                });

            migrationBuilder.InsertData(
                table: "p_bandeja",
                columns: new[] { "id", "dato_al", "mas_viejo", "pendientes" },
                values: new object[] { (short)1, null, null, 0 });

            migrationBuilder.CreateIndex(
                name: "ix_p_conflictos_pendientes",
                table: "p_conflictos",
                column: "id_activo",
                filter: "estado = 'PENDIENTE'");

            migrationBuilder.CreateIndex(
                name: "ix_p_evaluacion_vigente_desastre",
                table: "p_evaluacion_vigente",
                columns: new[] { "id_desastre", "codigo_municipio" },
                filter: "version_activo >= 0 AND NOT fusionado");

            migrationBuilder.CreateIndex(
                name: "ix_p_historial_activo_instante",
                table: "p_historial_activo",
                columns: new[] { "id_activo", "instante" });

            // Mínimo privilegio (DT-02): el servicio mantiene sus proyecciones y registra los eventos ya aplicados. Ninguna
            // proyección necesita DELETE (el consolidado se ajusta con incrementos), y el historial del activo y el registro
            // de idempotencia son de solo inserción (AD-41).
            migrationBuilder.Sql(@"
                GRANT SELECT, INSERT, UPDATE ON p_evaluacion_vigente, p_validaciones, p_dano_por_zona, p_conflictos, p_bandeja,
                    p_desastres, p_municipios, p_tipos_activo, p_integraciones TO consulta_danos_app;
                GRANT SELECT, INSERT ON p_historial_activo, mensajes_procesados TO consulta_danos_app;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mensajes_procesados");

            migrationBuilder.DropTable(
                name: "p_bandeja");

            migrationBuilder.DropTable(
                name: "p_conflictos");

            migrationBuilder.DropTable(
                name: "p_dano_por_zona");

            migrationBuilder.DropTable(
                name: "p_desastres");

            migrationBuilder.DropTable(
                name: "p_evaluacion_vigente");

            migrationBuilder.DropTable(
                name: "p_historial_activo");

            migrationBuilder.DropTable(
                name: "p_integraciones");

            migrationBuilder.DropTable(
                name: "p_municipios");

            migrationBuilder.DropTable(
                name: "p_tipos_activo");

            migrationBuilder.DropTable(
                name: "p_validaciones");
        }
    }
}
