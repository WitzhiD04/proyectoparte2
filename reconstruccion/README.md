# Módulo de Reconstrucción

Implementación del documento de diseño del módulo de Reconstrucción con las tecnologías elegidas:
C# (.NET 10), ASP.NET Core, Entity Framework Core, PostgreSQL, RabbitMQ, MinIO, Angular, Docker y Kubernetes.

## Ejecutar

Requiere Docker Desktop.

```bash
docker compose up -d --build
```

- Consola: http://localhost:8080 (si el puerto 8080 está ocupado, use otro: en PowerShell `$env:PUERTO_WEB=8090; docker compose up -d --build`; en bash `PUERTO_WEB=8090 docker compose up -d --build`)
- Panel de RabbitMQ: http://localhost:15672 (guest / guest)
- Panel de MinIO: http://localhost:9001 (minioadmin / minioadmin)

Para detener: `docker compose down`. Para borrar también los datos: `docker compose down -v`.

Prueba de todo el ciclo por la API: `bash scripts/prueba-humo.sh`

## Perfiles de prueba

El inicio de sesión es del API Gateway de la plataforma, que todavía no existe. La consola trae un selector
de perfil y envía el usuario, el rol y el ámbito en los encabezados `X-Usuario`, `X-Rol` y `X-Ambito`.

| Perfil | Qué puede hacer |
|---|---|
| Autoridad local (alcaldia.armenia) | Crear proyectos, asignar, aprobar proyectos municipales, cancelar, ver historial y consolidado |
| Autoridad regional (gobernacion.quindio) | Lo mismo, y aprueba los proyectos que abarcan varios municipios |
| Responsable (ing.perez, ing.gomez) | Iniciar, registrar avance y costo, adjuntar documentos y finalizar sus proyectos |
| Auditor (contraloria) | Ver historial y consolidado |

Recorrido sugerido: como autoridad local cree proyectos en *Evaluaciones candidatas*, asigne presupuesto,
cronograma y el responsable `ing.perez`, y apruebe. Cambie al perfil del responsable para iniciar, registrar
avance y finalizar. Cambie al auditor para ver el historial y el consolidado.

## Estructura

Un proyecto por anillo de la arquitectura limpia. Cada proyecto solo referencia a los de más adentro.

| Carpeta | Anillo | Contenido |
|---|---|---|
| `src/Reconstruccion.Dominio` | Entidades | `ProyectoReconstruccion`, `MaquinaEstadosProyecto`, objetos de valor, historial. Sin paquetes. |
| `src/Reconstruccion.Aplicacion` | Casos de uso | Un interactor por caso de uso, puertos de entrada, de salida y de gateways, DTO. Sin paquetes. |
| `src/Reconstruccion.Infraestructura` | Adaptadores de salida | Repositorios con EF Core, bandeja de salida, relevo y consumidor de RabbitMQ, MinIO. |
| `src/Reconstruccion.Api` | Adaptadores de entrada | Controllers, presenters y `Program.cs`, donde se conecta cada puerto con su implementación. |
| `tests/Reconstruccion.Pruebas` | | Pruebas de entidades y casos de uso con puertos en memoria (43). |
| `consola-web` | Frameworks y drivers | Aplicación Angular con las tres consolas. |
| `db` | | Tablas, permisos y datos de prueba. |
| `k8s` | | Manifiestos de Kubernetes. |

La misma imagen del API corre con dos roles (`Reconstruccion__Rol`): `escritura` es el servicio de
Reconstrucción y `consulta` es el servicio de Consulta del Programa.

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
| Historial de solo inserción | `HistorialEf` y los permisos de `db/01-reconstruccion.sql` |
| Cambio, historial y evento en una transacción | `GuardadoDeProyectos` (`CasosDeUso/Comun.cs`) y `UnidadDeTrabajoEf` |
| Bandeja de salida y relevo | `BandejaSalidaEf` y `RelevoEventos` (`Mensajeria/Rabbit.cs`) |
| Copias locales | `CopiasLocalesEf`, cargadas con `db/03-datos-prueba.sql` |
| Lote atómico y sin repetidos | `CrearProyectosPorLote`, tabla `lotes_creacion`, índice `ux_evaluacion_vigente` |
| Consolidado en un servicio de lectura aparte | `ProyeccionProgramaEf`, `ConsumidorProyeccion`, base `programa` |
| Réplica continua de base de datos | `db` y `db-replica` (`docker-compose.yml` y `k8s/10-datos.yaml`) |

## Rutas

| Método | Ruta | Caso de uso |
|---|---|---|
| GET | `/v1/desastres` | Lista de desastres (apoyo a la consola) |
| GET | `/v1/reconstruccion/candidatas?desastre=` | CU-01 |
| POST | `/v1/proyectos/lote` | CU-02 |
| GET | `/v1/proyectos?desastre=` y `/v1/proyectos/{id}` | Lista y detalle (apoyo a la consola) |
| PATCH | `/v1/proyectos/{id}/presupuesto` | CU-03: presupuesto, fuente y cronograma |
| PATCH | `/v1/proyectos/{id}/responsable` | CU-03: responsable |
| POST | `/v1/proyectos/{id}/transiciones` | CU-04, 05, 08 y 09 (`APROBAR`, `INICIAR`, `FINALIZAR`, `CANCELAR`) |
| POST | `/v1/proyectos/{id}/avances` | CU-06 |
| POST | `/v1/proyectos/{id}/documentos` | CU-07 |
| GET | `/v1/proyectos/{id}/historial?fecha=` | CU-10 |
| GET | `/v1/programa/consolidado?desastre=` | CU-11 (servicio de consulta) |

## Pruebas

```bash
dotnet test
```

## Desarrollo sin Docker para el API

Levante solo las dependencias (`docker compose up -d db bus archivos`, exponiendo sus puertos) y ejecute
`dotnet run --project src/Reconstruccion.Api`. En `consola-web`, `npm install` y `npm start`.

## Kubernetes

Con el Kubernetes de Docker Desktop:

```bash
docker build -t reconstruccion-api:local .
docker build -t reconstruccion-web:local consola-web
kubectl apply -k k8s/
```

La consola queda en http://localhost:30080.

`k8s/05-esquema.yaml` es una copia de los archivos de `db/`, porque Kubernetes no deja leer archivos fuera
de la carpeta `k8s`. Si cambia algo en `db/`, regenérelo con `bash scripts/generar-esquema-k8s.sh`. El servicio de Reconstrucción corre con 2 copias y el de
Consulta aumenta copias con la carga (necesita metrics-server).

## Lo que no está implementado

- Los otros módulos (Gestión de Desastres, Evaluación de Daños, Auditoría), el API Gateway y los sistemas
  externos financiero y de proyectos. Las copias locales se llenan con datos de prueba y los eventos del
  módulo quedan publicados en RabbitMQ para quien los consuma después.
- Recibir eventos de esos módulos y del sistema de proyectos externo (avance y contratista). Se agrega
  cuando existan.
- El clúster de RabbitMQ con varios nodos: los manifiestos usan un nodo con colas de tipo quorum.
- La segunda ubicación es simulada: la réplica de PostgreSQL corre en la misma máquina o clúster.
