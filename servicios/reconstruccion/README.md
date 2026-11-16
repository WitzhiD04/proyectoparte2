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

El inicio de sesión será del API Gateway (paso del gateway, YARP + Keycloak). Mientras tanto, la consola trae un
selector de perfil y envía el usuario, el rol y el ámbito en los encabezados `X-Usuario`, `X-Rol` y `X-Ambito`.

| Perfil | Qué puede hacer |
|---|---|
| Autoridad local (alcaldia.armenia) | Crear proyectos, asignar, aprobar proyectos municipales, cancelar, ver historial y consolidado |
| Autoridad regional (gobernacion.quindio) | Lo mismo, y aprueba los proyectos que abarcan varios municipios |
| Responsable (ing.perez, ing.gomez) | Iniciar, registrar avance y costo, adjuntar documentos y finalizar sus proyectos |
| Auditor (contraloria) | Ver historial y consolidado |

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
| Copias locales (DA-01) | `CopiasLocalesEf`; hoy se cargan con la siembra, en el paso 4 las alimentan EV-03, EV-06 y EV-08 |
| Lote atómico y sin repetidos | `CrearProyectosPorLote`, tabla `lotes_creacion`, índice `ux_evaluacion_vigente` |
| Consolidado en un servicio de lectura aparte (AD-42) con marca de tiempo (AD-16) | `ProyeccionProgramaEf`, `ManejadorEventosPrograma`, `ConsolidadoDto.DatoAl`, base `programa` |
| Réplica continua de base de datos | `db` y `db-replica` (`despliegue/compose/docker-compose.yml` y `despliegue/k8s/base/10-datos.yaml`) |

## Rutas

| Método | Ruta | Caso de uso |
|---|---|---|
| GET | `/v1/desastres` | Lista de desastres de la copia local (pasa a `/v1/reconstruccion/desastres` con el gateway, E-09) |
| GET | `/v1/reconstruccion/candidatas?desastre=` | CU-01 |
| POST | `/v1/proyectos/lote` | CU-02 (pasa a `/v1/proyectos:lote` con el gateway, E-09) |
| GET | `/v1/proyectos?desastre=` y `/v1/proyectos/{id}` | Lista y detalle |
| PATCH | `/v1/proyectos/{id}/presupuesto` | CU-03: presupuesto, fuente y cronograma |
| PATCH | `/v1/proyectos/{id}/responsable` | CU-03: responsable |
| POST | `/v1/proyectos/{id}/transiciones` | CU-04, 05, 08 y 09 (`APROBAR`, `INICIAR`, `FINALIZAR`, `CANCELAR`) |
| POST | `/v1/proyectos/{id}/avances` | CU-06 |
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

- **Consumir los eventos de otros módulos** (paso 4): EV-03 y EV-06 de Gestión de Desastres, EV-08 de Evaluación de
  Daños y EV-24 de SE-7. La cola `q.reconstruccion` ya existe y acumula lo que llegue.
- **El adaptador de SE-7** (cola `q.salida.se7` ya declarada) y la jurisdicción territorial exacta (DA-14).
- **El gateway con identidad real** (YARP + Keycloak): hasta entonces la identidad viaja en encabezados.
