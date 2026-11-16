using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reconstruccion.Infraestructura.Persistencia.Migraciones.Reconstruccion
{
    /// <inheritdoc />
    public partial class CopiasPorEventos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_evaluaciones_validadas_desastres_locales_id_desastre",
                table: "evaluaciones_validadas");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "actualizado_en",
                table: "evaluaciones_validadas",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "evaluaciones_validadas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "actualizado_en",
                table: "desastres_locales",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<bool>(
                name: "registrado",
                table: "desastres_locales",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "tipo",
                table: "desastres_locales",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "desastres_locales",
                type: "integer",
                nullable: false,
                defaultValue: 0);

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
                name: "municipios_locales",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    codigo_departamento = table.Column<string>(type: "text", nullable: true),
                    version_capa = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_municipios_locales", x => x.codigo);
                });

            // Las filas que ya existían (cargadas por la siembra de desarrollo anterior) se conservan visibles.
            migrationBuilder.Sql("UPDATE desastres_locales SET registrado = true WHERE nombre <> '';");

            // Desde el paso 4 el servicio mantiene sus copias locales con los eventos (DA-01) y registra los eventos ya
            // aplicados (DA-03). Mínimo privilegio (DT-02): no borra copias ni marcas de procesado.
            migrationBuilder.Sql(@"
                GRANT INSERT, UPDATE ON desastres_locales, evaluaciones_validadas TO reconstruccion_app;
                GRANT SELECT, INSERT, UPDATE ON municipios_locales TO reconstruccion_app;
                GRANT SELECT, INSERT ON mensajes_procesados TO reconstruccion_app;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mensajes_procesados");

            migrationBuilder.DropTable(
                name: "municipios_locales");

            migrationBuilder.DropColumn(
                name: "actualizado_en",
                table: "evaluaciones_validadas");

            migrationBuilder.DropColumn(
                name: "version",
                table: "evaluaciones_validadas");

            migrationBuilder.DropColumn(
                name: "actualizado_en",
                table: "desastres_locales");

            migrationBuilder.DropColumn(
                name: "registrado",
                table: "desastres_locales");

            migrationBuilder.DropColumn(
                name: "tipo",
                table: "desastres_locales");

            migrationBuilder.DropColumn(
                name: "version",
                table: "desastres_locales");

            migrationBuilder.AddForeignKey(
                name: "FK_evaluaciones_validadas_desastres_locales_id_desastre",
                table: "evaluaciones_validadas",
                column: "id_desastre",
                principalTable: "desastres_locales",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
