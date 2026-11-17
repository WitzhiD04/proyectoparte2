# Servicio de Reconstrucción

Implementa el módulo de Reconstrucción según su documento de diseño (`docs/DiseñoReconstrucción.pdf`) y las
decisiones del SAD (`docs/SADArqui.pdf`). La misma imagen corre como dos microservicios (AD-01):

| Rol (`Reconstruccion__Rol`) | Microservicio | Base | Bus |
|---|---|---|---|
| `escritura` | Reconstrucción | `reconstruccion` | Publica EV-18, EV-19 y EV-20 por su bandeja de salida |
| `consulta` | Consulta del Programa (AD-42) | `programa` | Consume `q.consulta-programa` |

Con el argumento `migrar` la imagen es el migrador del esquema (DT-19). El rol es obligatorio: sin él, el proceso no
arranca.

Para ejecutar la plataforma, vea el [README de la raíz](../../README.md).

## Perfiles de prueba

Reconstrucción y la Consulta del Programa leen la identidad común (`IIdentidadActual` de
`comun/Plataforma.Comun.Seguridad`, DA-15), igual que los demás servicios. `Identidad:Modo` elige de dónde sale:

- **`Jwt`**: el token de Keycloak (`Authorization: Bearer`). Usuario = `preferred_username`; roles del realm; claims
  `ambito` y `territorio`. Los encabezados `X-*` de identidad se rechazan (401).
- **`Encabezados`** (transición): `X-Usuario`, `X-Roles`, `X-Organizacion`, `X-Ambito` y `X-Territorio`. El antiguo
  `X-Rol` ya no se lee.

Roles del realm → rol del dominio: `autoridad` → Autoridad, `responsable-proyecto` → Responsable, `auditor` → Auditor
(si alguien tiene varios, prima ese orden). Sin identidad: 401. Con identidad pero sin ninguno de esos roles: 403.

Usuarios de prueba del realm (clave = usuario + `_local`):

| Usuario | Rol, ámbito y territorio | Qué puede hacer |
|---|---|---|
| `alcaldia.armenia` | autoridad, LOCAL, 63001 | Crear, asignar, cancelar; aprobar los proyectos municipales de Armenia; consolidado de Armenia |
| `alcaldia.calarca` | autoridad, LOCAL, 63130 | Lo mismo para Calarcá |
| `gobernacion.quindio` | autoridad, REGIONAL, 63 | Aprobar los proyectos de varios municipios del Quindío; consolidado del departamento |
| `ungrd.operador` | autoridad, NACIONAL | Aprobar los proyectos de varios departamentos; consolidado completo |
| `ing.perez` | responsable-proyecto | Iniciar, registrar avance y costo, adjuntar documentos y finalizar sus proyectos |
| `auditor.1` | auditor | Ver historial y consolidado completo |

**Jurisdicción (DA-14, E-20).** El alcance sale de los municipios DIVIPOLA de las evaluaciones (los 2 primeros dígitos
son el departamento): un municipio es `Municipal` y lo aprueba la autoridad LOCAL de ese municipio; varios del mismo
departamento es `Regional` y lo aprueba la REGIONAL de ese departamento; varios departamentos es `Nacional` y lo aprueba
la NACIONAL.

**Lo que ve y usa cada autoridad (Cuadro 3 del SRS, E-39).** Una autoridad solo trabaja con su jurisdicción; la nacional y
el auditor ven todo, y el responsable sigue como antes (sus proyectos asignados):

| Ruta | Para una autoridad |
|---|---|
| `GET /v1/reconstruccion/candidatas` | Solo las evaluaciones de municipios de su jurisdicción |
| `POST /v1/proyectos:lote` | 403 si alguna evaluación del lote es de otro territorio; no se crea ninguno |
| `GET /v1/proyectos?desastre=` | Solo los proyectos que tocan su territorio |
| `GET /v1/proyectos/{id}` y `/historial` | 404 si el proyecto no es de su jurisdicción (no revela que existe) |
| `GET /v1/programa/consolidado` | Solo cuenta los proyectos que tocan su territorio |

Las reglas están en `Dominio/Jurisdiccion.cs` y `CreacionDeProyectos`, y las aplican los casos de uso.

Recorrido sugerido: como autoridad local, cree proyectos en *Evaluaciones candidatas*; asigne presupuesto,
cronograma y el responsable `ing.perez`, y apruebe. Cambie al perfil del responsable para iniciar, registrar
avance y finalizar. Cambie al auditor para ver el historial y el consolidado.

## Estructura (arquitectura limpia y hexagonal, AD-08)

Un proyecto por anillo; cada uno solo referencia a los de más adentro. Una prueba de arquitectura lo verifica
(`tests/Reconstruccion.Pruebas/Arquitectura`).

| Carpeta | Anillo | Contenido |
|---|---|---|
| `src/Reconstruccion.Dominio` | Entidades | `ProyectoReconstruccion`, `MaquinaEstadosProyecto`, objetos de valor, historial. Sin paquetes. |
| `src/Reconstruccion.Aplicacion` | Casos de uso y puertos | Un interactor por caso de uso, puertos de entrada y de salida (`IPublicadorHechos`, repositorios), hechos y DTO. Sin paquetes y sin conocer el bus. |
| `src/Reconstruccion.Infraestructura` | Adaptadores | EF Core y sus migraciones, MinIO, `PublicadorHechosBandeja` (hechos → eventos canónicos en la bandeja), `ManejadorEventosPrograma` (eventos → proyección). Usa `comun/`. |
| `src/Reconstruccion.Api` | Adaptadores de entrada | Controllers, presenters y `Program.cs`, donde se conecta cada puerto con su adaptador. |
| `tests/Reconstruccion.Pruebas` | | Entidades, casos de uso y arquitectura, con puertos en memoria. |
| `tests/Reconstruccion.Integracion` | | Migraciones, permisos, eventos contra el contrato y proyección, con PostgreSQL real (Testcontainers). |

## Dónde está cada parte del diseño

| Diseño | Código |
|---|---|
| Reglas de negocio | Métodos de `ProyectoReconstruccion` y `CreacionDeProyectos` (`Servicios.cs`) |
| Diagrama de transición de estados | `MaquinaEstadosProyecto.cs` |
| CU-01 y CU-02 | `CasosDeUso/CrearProyectosPorLote.cs` |
| CU-03, 04, 05, 06, 08 y 09 | `CasosDeUso/Operaciones.cs` |
| CU-07 | `CasosDeUso/AdjuntarDocumento.cs` |
| CU-10 y CU-11 | `CasosDeUso/Consultas.cs` |
| Puertos | `Aplicacion/Puertos/` |
| Presenter | `Api/Presenters/PresentadorHttp.cs` |
| Historial de solo inserción (AD-41) | `HistorialEf` y los `GRANT SELECT, INSERT` de la migración inicial: el rol del servicio no puede modificar ni borrar |
| Cambio, historial y evento en una transacción (AD-45) | `GuardadoDeProyectos` (`CasosDeUso/Comun.cs`), `PublicadorHechosBandeja` y `UnidadDeTrabajoEf` |
| Eventos EV-18, EV-19 y EV-20 con el sobre canónico (AD-09, E-10) | `Infraestructura/Mensajeria/PublicadorHechosBandeja.cs` y `comun/Plataforma.Comun.Eventos` |
| Relevo con secuencia sin huecos y consumidor idempotente | `comun/Plataforma.Comun.Mensajeria` |
| Copias locales (DA-01) | `ManejadorEventosReconstruccion` (cola `q.reconstruccion`) → `ActualizarCopiasLocales` → `EscrituraCopiasLocalesEf`, alimentadas por EV-03, EV-06, EV-08 y EV-21; reportes de SE-7 por EV-24 (`RegistrarReporteExterno`) |
| Lote atómico y sin repetidos | `CrearProyectosPorLote`, tabla `lotes_creacion`, índice `ux_evaluacion_vigente` |
| Consolidado en un servicio de lectura aparte (AD-42) con marca de tiempo (AD-16) | `ProyeccionProgramaEf`, `ManejadorEventosPrograma`, `ConsolidadoDto.DatoAl`, base `programa` |
| Identidad común y jurisdicción territorial (DA-14, DA-15) | `Api/Controllers/ControladorBase.cs` (identidad → `Actor`), `Dominio/Jurisdiccion.cs`, `ConsultarConsolidado` |
| Réplica continua de base de datos | `db` y `db-replica` (`despliegue/compose/docker-compose.yml` y `despliegue/k8s/base/10-datos.yaml`) |

## Rutas

| Método | Ruta | Caso de uso |
|---|---|---|
| GET | `/v1/reconstruccion/desastres` | Lista de desastres de la copia local (E-09) |
| GET | `/v1/reconstruccion/candidatas?desastre=` | CU-01 |
| POST | `/v1/proyectos:lote` | CU-02 (E-09) |
| GET | `/v1/proyectos?desastre=` y `/v1/proyectos/{id}` | Lista y detalle |
| PATCH | `/v1/proyectos/{id}/presupuesto` | CU-03: presupuesto, fuente y cronograma |
| PATCH | `/v1/proyectos/{id}/responsable` | CU-03: responsable |
| POST | `/v1/proyectos/{id}/transiciones` | CU-04, 05, 08 y 09 (`APROBAR`, `INICIAR`, `FINALIZAR`, `CANCELAR`) |
| POST | `/v1/proyectos/{id}/avance` | CU-06 |
| POST | `/v1/proyectos/{id}/documentos` | CU-07 |
| GET | `/v1/proyectos/{id}/historial?fecha=` | CU-10 |
| GET | `/v1/programa/consolidado?desastre=` | CU-11 (servicio de consulta), con `datoAl` |
| GET | `/salud/vivo` y `/salud/listo` | Sondeos del orquestador (AD-24) |

## Esquema de la base (migraciones)

Las migraciones están en `src/Reconstruccion.Infraestructura/Persistencia/Migraciones`. Para agregar una, desde la
raíz del repositorio:

```bash
P=servicios/reconstruccion/src/Reconstruccion.Infraestructura
bash herramientas/dotnet.sh ef migrations add <Nombre> --project $P --startup-project $P --context ReconstruccionDbContext --output-dir Persistencia/Migraciones/Reconstruccion
```

Si la migración crea tablas, agregue en ella los `GRANT` del rol del servicio (mínimo privilegio, DT-02).

## Lo que todavía no está

- **La consola en modo `Encabezados`** todavía envía `X-Rol`: Reconstrucción le responde 403 hasta que la consola pase
  al inicio de sesión con Keycloak (encargo `docs/encargos/consola.md`).
- **La jurisdicción en las operaciones de escritura:** asignar presupuesto o responsable y cancelar no revisan el
  territorio de la autoridad; solo la aprobación y la creación lo hacen.
- **El adaptador de SE-6** (sistema financiero): presupuesto y costos por lotes, con cola y reintento. Encargo:
  `docs/encargos/adaptador-se6.md`.
- **La entrada por el gateway** (YARP + Keycloak): hasta entonces la consola usa el selector de perfil.
