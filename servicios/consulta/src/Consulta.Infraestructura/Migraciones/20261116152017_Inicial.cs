using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Consulta.Infraestructura.Migraciones
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
                name: "p_desastres",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    registrado = table.Column<bool>(type: "boolean", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: true),
                    nombre = table.Column<string>(type: "text", nullable: true),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    instante_inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ubicacion = table.Column<Point>(type: "geometry(Point,4326)", nullable: true),
                    organismo_registra = table.Column<string>(type: "text", nullable: true),
                    severidad = table.Column<string>(type: "text", nullable: true),
                    severidad_version = table.Column<int>(type: "integer", nullable: false),
                    severidad_al = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    estado = table.Column<string>(type: "text", nullable: true),
                    estado_version = table.Column<int>(type: "integer", nullable: false),
                    estado_al = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    responsable = table.Column<string>(type: "text", nullable: true),
                    participantes = table.Column<string>(type: "jsonb", nullable: false),
                    organismos_version = table.Column<int>(type: "integer", nullable: false),
                    organismos_al = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    zona_version = table.Column<int>(type: "integer", nullable: false),
                    zona_al = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_p_desastres", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "p_evolucion",
                columns: table => new
                {
                    id_desastre = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    instante = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    anterior = table.Column<string>(type: "text", nullable: true),
                    nuevo = table.Column<string>(type: "text", nullable: true),
                    autor = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_p_evolucion", x => new { x.id_desastre, x.tipo, x.version });
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
                    version_capa = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_p_municipios", x => x.codigo);
                });

            migrationBuilder.CreateTable(
                name: "p_zonas",
                columns: table => new
                {
                    id_desastre = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    vigente_desde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    geometria = table.Column<Geometry>(type: "geometry(Geometry,4326)", nullable: true),
                    geometria_simplificada = table.Column<Geometry>(type: "geometry(Geometry,4326)", nullable: true),
                    codigos_municipio = table.Column<string[]>(type: "text[]", nullable: false),
                    fuente = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_p_zonas", x => new { x.id_desastre, x.version });
                });

            migrationBuilder.CreateIndex(
                name: "ix_p_desastres_estado",
                table: "p_desastres",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_p_evolucion_desastre_instante",
                table: "p_evolucion",
                columns: new[] { "id_desastre", "instante" });

            // Mínimo privilegio (DT-02): el servicio solo mantiene sus proyecciones y registra los eventos ya aplicados.
            migrationBuilder.Sql(@"
                GRANT SELECT, INSERT, UPDATE ON p_desastres, p_zonas, p_evolucion, p_municipios, p_integraciones TO consulta_app;
                GRANT SELECT, INSERT ON mensajes_procesados TO consulta_app;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mensajes_procesados");

            migrationBuilder.DropTable(
                name: "p_desastres");

            migrationBuilder.DropTable(
                name: "p_evolucion");

            migrationBuilder.DropTable(
                name: "p_integraciones");

            migrationBuilder.DropTable(
                name: "p_municipios");

            migrationBuilder.DropTable(
                name: "p_zonas");
        }
    }
}
