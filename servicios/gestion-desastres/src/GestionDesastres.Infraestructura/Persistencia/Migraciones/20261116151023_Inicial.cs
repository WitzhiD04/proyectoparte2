using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GestionDesastres.Infraestructura.Persistencia.Migraciones
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
                name: "bandeja_salida",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    secuencia = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    secuencia_productor = table.Column<long>(type: "bigint", nullable: true),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    clave_ruteo = table.Column<string>(type: "text", nullable: false),
                    clave_idempotencia = table.Column<string>(type: "text", nullable: false),
                    id_correlacion = table.Column<string>(type: "text", nullable: true),
                    sobre = table.Column<string>(type: "jsonb", nullable: false),
                    instante = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    publicado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bandeja_salida", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contador_productor",
                columns: table => new
                {
                    productor = table.Column<string>(type: "text", nullable: false),
                    ultimo = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contador_productor", x => x.productor);
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

            migrationBuilder.CreateTable(
                name: "municipios",
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
                    table.PrimaryKey("PK_municipios", x => x.codigo);
                });

            migrationBuilder.CreateTable(
                name: "perfiles_ciclo_vida",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    rango = table.Column<int>(type: "integer", nullable: false),
                    estados = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_perfiles_ciclo_vida", x => x.codigo);
                });

            migrationBuilder.CreateTable(
                name: "proyectos_desastre",
                columns: table => new
                {
                    id_proyecto = table.Column<Guid>(type: "uuid", nullable: false),
                    id_desastre = table.Column<Guid>(type: "uuid", nullable: false),
                    final = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proyectos_desastre", x => x.id_proyecto);
                });

            migrationBuilder.CreateTable(
                name: "perfil_por_nivel",
                columns: table => new
                {
                    nivel = table.Column<string>(type: "text", nullable: false),
                    codigo_perfil = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_perfil_por_nivel", x => x.nivel);
                    table.CheckConstraint("ck_perfil_por_nivel_nivel", "nivel IN ('Nivel1', 'Nivel2', 'Nivel3', 'Nivel4')");
                    table.ForeignKey(
                        name: "FK_perfil_por_nivel_perfiles_ciclo_vida_codigo_perfil",
                        column: x => x.codigo_perfil,
                        principalTable: "perfiles_ciclo_vida",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tipos_desastre",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    codigo_perfil = table.Column<string>(type: "text", nullable: false),
                    criterios = table.Column<string>(type: "jsonb", nullable: false),
                    organismos = table.Column<string>(type: "jsonb", nullable: false),
                    vigente_desde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    vigente_hasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tipos_desastre", x => x.codigo);
                    table.ForeignKey(
                        name: "FK_tipos_desastre_perfiles_ciclo_vida_codigo_perfil",
                        column: x => x.codigo_perfil,
                        principalTable: "perfiles_ciclo_vida",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "desastres",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo_tipo = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: false),
                    instante_inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ubicacion = table.Column<Point>(type: "geometry(Point,4326)", nullable: false),
                    organismos_registra = table.Column<string[]>(type: "text[]", nullable: false),
                    fuentes = table.Column<string[]>(type: "text[]", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    instante_actualizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_desastres", x => x.id);
                    table.ForeignKey(
                        name: "FK_desastres_tipos_desastre_codigo_tipo",
                        column: x => x.codigo_tipo,
                        principalTable: "tipos_desastre",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "historial",
                columns: table => new
                {
                    id_desastre = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    tipo_cambio = table.Column<string>(type: "text", nullable: false),
                    valor_anterior = table.Column<string>(type: "text", nullable: true),
                    valor_nuevo = table.Column<string>(type: "text", nullable: true),
                    autor = table.Column<string>(type: "text", nullable: false),
                    organizacion = table.Column<string>(type: "text", nullable: true),
                    justificacion = table.Column<string>(type: "text", nullable: false),
                    instante = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resultado = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_historial", x => new { x.id_desastre, x.version });
                    table.CheckConstraint("ck_historial_tipo", "tipo_cambio IN ('Registro', 'Severidad', 'Estado', 'Zona', 'Organismos', 'Perfil')");
                    table.ForeignKey(
                        name: "FK_historial_desastres_id_desastre",
                        column: x => x.id_desastre,
                        principalTable: "desastres",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "zonas",
                columns: table => new
                {
                    id_desastre = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    vigente_desde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    geometria = table.Column<Geometry>(type: "geometry(Geometry,4326)", nullable: true),
                    codigos_municipio = table.Column<string[]>(type: "text[]", nullable: false),
                    fuente = table.Column<string>(type: "text", nullable: false),
                    autor = table.Column<string>(type: "text", nullable: false),
                    justificacion = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zonas", x => new { x.id_desastre, x.version });
                    table.CheckConstraint("ck_zonas_contenido", "geometria IS NOT NULL OR cardinality(codigos_municipio) > 0");
                    table.ForeignKey(
                        name: "FK_zonas_desastres_id_desastre",
                        column: x => x.id_desastre,
                        principalTable: "desastres",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bandeja_por_numerar",
                table: "bandeja_salida",
                columns: new[] { "instante", "orden" },
                filter: "secuencia_productor IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bandeja_por_publicar",
                table: "bandeja_salida",
                column: "secuencia_productor",
                unique: true,
                filter: "secuencia_productor IS NOT NULL AND publicado_en IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_bandeja_salida_clave_idempotencia",
                table: "bandeja_salida",
                column: "clave_idempotencia",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_desastres_codigo_tipo",
                table: "desastres",
                column: "codigo_tipo");

            migrationBuilder.CreateIndex(
                name: "ix_historial_desastre_instante",
                table: "historial",
                columns: new[] { "id_desastre", "instante" });

            migrationBuilder.CreateIndex(
                name: "ix_municipios_geometria",
                table: "municipios",
                column: "geometria")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_perfil_por_nivel_codigo_perfil",
                table: "perfil_por_nivel",
                column: "codigo_perfil");

            migrationBuilder.CreateIndex(
                name: "ix_proyectos_desastre",
                table: "proyectos_desastre",
                column: "id_desastre");

            migrationBuilder.CreateIndex(
                name: "IX_tipos_desastre_codigo_perfil",
                table: "tipos_desastre",
                column: "codigo_perfil");

            migrationBuilder.CreateIndex(
                name: "ix_zonas_geometria",
                table: "zonas",
                column: "geometria")
                .Annotation("Npgsql:IndexMethod", "gist");

            // Catálogo inicial (SRS RN-1, Cuadro 17 y Cuadro 18). Es contenido de la organización, no datos de prueba:
            // después se administra en ejecución por la API del catálogo (QA-10). Los organismos por defecto siguen el
            // ámbito de coordinación de cada nivel (Cuadro 18) con los organismos que nombra el enunciado (§4.1).
            migrationBuilder.Sql(@"
                INSERT INTO perfiles_ciclo_vida (codigo, nombre, rango, estados) VALUES
                    ('SIMPLIFICADO', 'Simplificado', 1, ARRAY['Cerrado', 'Registrado']),
                    ('ESTANDAR', 'Estándar', 2, ARRAY['Cerrado', 'Detectado', 'EnAtencion', 'EnEvaluacion', 'EnRecuperacion', 'Registrado']),
                    ('EXTENDIDO', 'Extendido', 3, ARRAY['Cerrado', 'Detectado', 'EnAtencion', 'EnEvaluacion', 'EnReconstruccion', 'EnRecuperacion', 'Registrado']);
                INSERT INTO perfil_por_nivel (nivel, codigo_perfil) VALUES
                    ('Nivel1', 'SIMPLIFICADO'), ('Nivel2', 'ESTANDAR'), ('Nivel3', 'EXTENDIDO'), ('Nivel4', 'EXTENDIDO');
                INSERT INTO tipos_desastre (codigo, nombre, codigo_perfil, criterios, organismos, vigente_desde) VALUES
                    ('TERREMOTO', 'Terremoto', 'EXTENDIDO', '{""Nivel1"": ""Magnitud < 4,5 y sin daño estructural"", ""Nivel2"": ""Magnitud 4,5 a 5,9 o daño estructural en un municipio"", ""Nivel3"": ""Magnitud 6 a 6,9, daño estructural en varios municipios"", ""Nivel4"": ""Magnitud ≥ 7 o daño estructural extendido con víctimas""}'::jsonb, '{""Nivel1"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}], ""Nivel2"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}], ""Nivel3"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}, {""organismo"": ""autoridad-regional"", ""responsabilidad"": ""Coordinación regional y recursos""}, {""organismo"": ""policia"", ""responsabilidad"": ""Orden público y evacuación""}, {""organismo"": ""servicios-medicos"", ""responsabilidad"": ""Atención médica""}], ""Nivel4"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}, {""organismo"": ""autoridad-regional"", ""responsabilidad"": ""Coordinación regional y recursos""}, {""organismo"": ""policia"", ""responsabilidad"": ""Orden público y evacuación""}, {""organismo"": ""servicios-medicos"", ""responsabilidad"": ""Atención médica""}, {""organismo"": ""autoridad-nacional"", ""responsabilidad"": ""Coordinación nacional y asistencia internacional""}]}'::jsonb, TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                    ('INUNDACION', 'Inundación', 'ESTANDAR', '{""Nivel1"": ""Área inundada local, sin viviendas afectadas"", ""Nivel2"": ""Viviendas afectadas en un municipio"", ""Nivel3"": ""Varios municipios, personas evacuadas"", ""Nivel4"": ""Región extensa, evacuación masiva""}'::jsonb, '{""Nivel1"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}], ""Nivel2"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}], ""Nivel3"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}, {""organismo"": ""autoridad-regional"", ""responsabilidad"": ""Coordinación regional y recursos""}, {""organismo"": ""policia"", ""responsabilidad"": ""Orden público y evacuación""}, {""organismo"": ""servicios-medicos"", ""responsabilidad"": ""Atención médica""}], ""Nivel4"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}, {""organismo"": ""autoridad-regional"", ""responsabilidad"": ""Coordinación regional y recursos""}, {""organismo"": ""policia"", ""responsabilidad"": ""Orden público y evacuación""}, {""organismo"": ""servicios-medicos"", ""responsabilidad"": ""Atención médica""}, {""organismo"": ""autoridad-nacional"", ""responsabilidad"": ""Coordinación nacional y asistencia internacional""}]}'::jsonb, TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                    ('INCENDIO', 'Incendio', 'ESTANDAR', '{""Nivel1"": ""Conato controlado con recursos locales"", ""Nivel2"": ""Incendio forestal o estructural que requiere varios organismos"", ""Nivel3"": ""Incendio que supera la capacidad regional"", ""Nivel4"": ""Incendio de alcance nacional""}'::jsonb, '{""Nivel1"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}], ""Nivel2"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}], ""Nivel3"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}, {""organismo"": ""autoridad-regional"", ""responsabilidad"": ""Coordinación regional y recursos""}, {""organismo"": ""policia"", ""responsabilidad"": ""Orden público y evacuación""}, {""organismo"": ""servicios-medicos"", ""responsabilidad"": ""Atención médica""}], ""Nivel4"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}, {""organismo"": ""autoridad-regional"", ""responsabilidad"": ""Coordinación regional y recursos""}, {""organismo"": ""policia"", ""responsabilidad"": ""Orden público y evacuación""}, {""organismo"": ""servicios-medicos"", ""responsabilidad"": ""Atención médica""}, {""organismo"": ""autoridad-nacional"", ""responsabilidad"": ""Coordinación nacional y asistencia internacional""}]}'::jsonb, TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                    ('DESLIZAMIENTO', 'Deslizamiento', 'ESTANDAR', '{""Nivel1"": ""Sin viviendas afectadas"", ""Nivel2"": ""Viviendas o vías afectadas en un municipio"", ""Nivel3"": ""Varios municipios o víctimas"", ""Nivel4"": ""Afectación masiva de población""}'::jsonb, '{""Nivel1"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}], ""Nivel2"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}], ""Nivel3"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}, {""organismo"": ""autoridad-regional"", ""responsabilidad"": ""Coordinación regional y recursos""}, {""organismo"": ""policia"", ""responsabilidad"": ""Orden público y evacuación""}, {""organismo"": ""servicios-medicos"", ""responsabilidad"": ""Atención médica""}], ""Nivel4"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}, {""organismo"": ""autoridad-regional"", ""responsabilidad"": ""Coordinación regional y recursos""}, {""organismo"": ""policia"", ""responsabilidad"": ""Orden público y evacuación""}, {""organismo"": ""servicios-medicos"", ""responsabilidad"": ""Atención médica""}, {""organismo"": ""autoridad-nacional"", ""responsabilidad"": ""Coordinación nacional y asistencia internacional""}]}'::jsonb, TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                    ('HURACAN', 'Huracán', 'EXTENDIDO', '{""Nivel1"": ""Alerta sin impacto"", ""Nivel2"": ""Impacto local"", ""Nivel3"": ""Impacto regional con daño estructural"", ""Nivel4"": ""Impacto nacional""}'::jsonb, '{""Nivel1"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}], ""Nivel2"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}], ""Nivel3"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}, {""organismo"": ""autoridad-regional"", ""responsabilidad"": ""Coordinación regional y recursos""}, {""organismo"": ""policia"", ""responsabilidad"": ""Orden público y evacuación""}, {""organismo"": ""servicios-medicos"", ""responsabilidad"": ""Atención médica""}], ""Nivel4"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}, {""organismo"": ""autoridad-regional"", ""responsabilidad"": ""Coordinación regional y recursos""}, {""organismo"": ""policia"", ""responsabilidad"": ""Orden público y evacuación""}, {""organismo"": ""servicios-medicos"", ""responsabilidad"": ""Atención médica""}, {""organismo"": ""autoridad-nacional"", ""responsabilidad"": ""Coordinación nacional y asistencia internacional""}]}'::jsonb, TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                    ('ERUPCION_VOLCANICA', 'Erupción volcánica', 'EXTENDIDO', '{""Nivel1"": ""Actividad sin impacto"", ""Nivel2"": ""Caída de ceniza local"", ""Nivel3"": ""Evacuación de varios municipios"", ""Nivel4"": ""Erupción mayor con impacto nacional""}'::jsonb, '{""Nivel1"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}], ""Nivel2"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}], ""Nivel3"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}, {""organismo"": ""autoridad-regional"", ""responsabilidad"": ""Coordinación regional y recursos""}, {""organismo"": ""policia"", ""responsabilidad"": ""Orden público y evacuación""}, {""organismo"": ""servicios-medicos"", ""responsabilidad"": ""Atención médica""}], ""Nivel4"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}, {""organismo"": ""autoridad-regional"", ""responsabilidad"": ""Coordinación regional y recursos""}, {""organismo"": ""policia"", ""responsabilidad"": ""Orden público y evacuación""}, {""organismo"": ""servicios-medicos"", ""responsabilidad"": ""Atención médica""}, {""organismo"": ""autoridad-nacional"", ""responsabilidad"": ""Coordinación nacional y asistencia internacional""}]}'::jsonb, TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                    ('OTRO', 'Otro evento definido por la organización', 'ESTANDAR', '{""Nivel1"": ""Atención con recursos locales"", ""Nivel2"": ""Coordinación entre varios organismos"", ""Nivel3"": ""Recursos regionales o nacionales"", ""Nivel4"": ""Coordinación nacional""}'::jsonb, '{""Nivel1"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}], ""Nivel2"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}], ""Nivel3"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}, {""organismo"": ""autoridad-regional"", ""responsabilidad"": ""Coordinación regional y recursos""}, {""organismo"": ""policia"", ""responsabilidad"": ""Orden público y evacuación""}, {""organismo"": ""servicios-medicos"", ""responsabilidad"": ""Atención médica""}], ""Nivel4"": [{""organismo"": ""autoridad-local"", ""responsabilidad"": ""Coordinación local de la emergencia""}, {""organismo"": ""bomberos"", ""responsabilidad"": ""Búsqueda, rescate y control""}, {""organismo"": ""defensa-civil"", ""responsabilidad"": ""Evaluación de daños y apoyo""}, {""organismo"": ""cruz-roja"", ""responsabilidad"": ""Atención a personas afectadas""}, {""organismo"": ""autoridad-regional"", ""responsabilidad"": ""Coordinación regional y recursos""}, {""organismo"": ""policia"", ""responsabilidad"": ""Orden público y evacuación""}, {""organismo"": ""servicios-medicos"", ""responsabilidad"": ""Atención médica""}, {""organismo"": ""autoridad-nacional"", ""responsabilidad"": ""Coordinación nacional y asistencia internacional""}]}'::jsonb, TIMESTAMPTZ '2026-01-01 00:00:00+00');
            ");

            // Mínimo privilegio (DT-02). El historial y las zonas son de solo inserción (AD-20, RN-4): una versión nueva
            // sucede a la anterior y no la sobrescribe. Del desastre solo cambian la versión y la última modificación.
            migrationBuilder.Sql(@"
                GRANT SELECT, INSERT ON desastres, historial, zonas TO gestion_desastres_app;
                GRANT UPDATE (version, instante_actualizacion) ON desastres TO gestion_desastres_app;
                GRANT SELECT, INSERT, UPDATE ON tipos_desastre, perfiles_ciclo_vida, perfil_por_nivel TO gestion_desastres_app;
                GRANT SELECT, INSERT, UPDATE ON proyectos_desastre, municipios TO gestion_desastres_app;
                GRANT SELECT, INSERT, UPDATE ON bandeja_salida, contador_productor TO gestion_desastres_app;
                GRANT SELECT, INSERT ON mensajes_procesados TO gestion_desastres_app;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bandeja_salida");

            migrationBuilder.DropTable(
                name: "contador_productor");

            migrationBuilder.DropTable(
                name: "historial");

            migrationBuilder.DropTable(
                name: "mensajes_procesados");

            migrationBuilder.DropTable(
                name: "municipios");

            migrationBuilder.DropTable(
                name: "perfil_por_nivel");

            migrationBuilder.DropTable(
                name: "proyectos_desastre");

            migrationBuilder.DropTable(
                name: "zonas");

            migrationBuilder.DropTable(
                name: "desastres");

            migrationBuilder.DropTable(
                name: "tipos_desastre");

            migrationBuilder.DropTable(
                name: "perfiles_ciclo_vida");
        }
    }
}
