using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reconstruccion.Infraestructura.Persistencia.Migraciones.Programa
{
    /// <inheritdoc />
    public partial class MunicipiosProyeccion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "codigos_municipio",
                table: "proyeccion_proyectos",
                type: "text[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "codigos_municipio",
                table: "proyeccion_proyectos");
        }
    }
}
