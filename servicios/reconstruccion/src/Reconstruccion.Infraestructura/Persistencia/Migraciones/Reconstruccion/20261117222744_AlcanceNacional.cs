using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reconstruccion.Infraestructura.Persistencia.Migraciones.Reconstruccion
{
    /// <inheritdoc />
    public partial class AlcanceNacional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_proyectos_alcance",
                table: "proyectos");

            migrationBuilder.AddCheckConstraint(
                name: "ck_proyectos_alcance",
                table: "proyectos",
                sql: "alcance IN ('Municipal', 'Regional', 'Nacional')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_proyectos_alcance",
                table: "proyectos");

            migrationBuilder.AddCheckConstraint(
                name: "ck_proyectos_alcance",
                table: "proyectos",
                sql: "alcance IN ('Municipal', 'Regional')");
        }
    }
}
