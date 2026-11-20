# Plataforma para la Gestión de Desastres y Reconstrucción

> **Estado del proyecto y lo que falta:** [`docs/encargos/LEEME.md`](docs/encargos/LEEME.md). Las decisiones de arquitectura y las
> enmiendas a los documentos están en [`docs/decisiones/`](docs/decisiones/).


Monorepo de la segunda entrega. La arquitectura está en `docs/` (SRS, SAD, ATAM y el diseño de Reconstrucción).
La app móvil de campo vive en su propio repositorio (`danos`, rama `main`) y se integra por el gateway con OIDC.

## Alcance de la implementación

El diseño (SRS, SAD y ATAM) cubre la plataforma completa: 12 microservicios, 8 adaptadores y dos regiones. La
implementación cubre los cuatro mínimos de la entrega y lo que ellos necesitan para funcionar de punta a punta:

| Mínimo | Piezas |
|---|---|
| a) Móvil: recolección y caracterización de un desastre | App `danos` (registro del desastre en campo y evaluaciones sin conexión), `servicios/gestion-desastres`, `gateway/` |
| b) Reconstrucción física | `servicios/reconstruccion` (escritura y Consulta del Programa), módulo `reconstruccion` de la consola, adaptadores de SE-6 y SE-7 |
| c) Mapa y visualización | `servicios/consulta`, `servicios/consulta-danos`, `adaptadores/adaptador-se2`, módulo `mapa` de la consola |
| d) Evaluación de daños | `servicios/evaluacion-danos`, módulo `evaluaciones` de la consola |

**Jakarta EE y .NET (CON-9).** El núcleo está en .NET 10 (AD-50). El adaptador de SE-8 está en Jakarta EE 11 sobre
WildFly 42 (`adaptadores/adaptador-se8-jee`, AD-66): la segunda pila vive en la frontera, donde AD-03 ubica la
heterogeneidad. Incorporarlo modificó 0 componentes existentes (QA-09).

Transversales: `servicios/auditoria`, Keycloak, el bus de eventos y `comun/`. Quedan fuera de la implementación, como
diseño: Consolidación, Notificación, Gestión de Recursos, Personas Afectadas, Coordinación de Organismos, el Portal
público, los adaptadores SE-1, SE-3, SE-4 y SE-5, y la segunda región. Mientras Notificación no exista, el adaptador de
SE-8 recibe EV-03 y EV-04 como sustituto temporal (límite L-11).

## Estructura

| Carpeta | Contenido |
|---|---|
| `docs/` | SRS, SAD, ATAM y documento de diseño de Reconstrucción (PDF); decisiones y enmiendas en `docs/decisiones/` |
| `comun/Plataforma.Comun.Eventos` | **Contrato de eventos** (AD-09): sobre canónico, enumeraciones, catálogo EV-01 a EV-29, tipos de datos y esquemas JSON |
| `comun/Plataforma.Comun.Mensajeria` | Bandeja de salida, relevo con secuencia sin huecos, consumidor idempotente con cola de mensajes muertos (AD-02, AD-45) |
| `comun/Plataforma.Comun.Seguridad` | Identidad común `IIdentidadActual`: validación de JWT de Keycloak y modo de transición por encabezados (DA-15) |
| `comun/Plataforma.Comun.Salud` | Sondeos `/salud/vivo` y `/salud/listo` (AD-24) |
| `comun/Plataforma.Comun.Migraciones` | Migrador: roles de mínimo privilegio, migraciones de EF Core y siembra (DT-19) |
| `comun/Plataforma.Comun.Adaptadores` | Piezas de los adaptadores de sistemas externos (DA-11): `IContratoAdaptador`, marca de avance, resiliencia con Polly (DT-09) y EV-23 |
| `servicios/gestion-desastres` | Gestión de Desastres: registro, clasificación, ciclo de vida y delimitación versionada (RN-1 a RN-4) |
| `servicios/consulta` | Consulta: lectura de desastres, mapa y evolución en un instante, caché de geometrías (AD-15 a AD-17) |
| `servicios/consulta-danos` | Consulta de Daños: mapa de tres capas, priorización, evolución por activo y por zona, tablero Razor ([README](servicios/consulta-danos/README.md)) |
| `servicios/evaluacion-danos` | Evaluación de Daños: sincronización sin conexión, revisión y catálogo ([README](servicios/evaluacion-danos/README.md)) |
| `servicios/reconstruccion` | Reconstrucción y Consulta del Programa ([README](servicios/reconstruccion/README.md)) |
| `servicios/auditoria` | Auditoría: registro inmutable de todos los eventos y detección de huecos (AD-21, DA-17) ([README](servicios/auditoria/README.md)) |
| `adaptadores/adaptador-se2` | Adaptador de SE-2: publica la capa de municipios en EV-21 ([README](adaptadores/adaptador-se2/README.md)) |
| `adaptadores/adaptador-se6` | Adaptador de SE-6: presupuesto y costos hacia el sistema financiero por lotes ([README](adaptadores/adaptador-se6/README.md)) |
| `adaptadores/adaptador-se7` | Adaptador de SE-7: EV-18 y EV-20 hacia SE-7 desde `q.salida.se7`, EV-24 desde su marca de avance ([README](adaptadores/adaptador-se7/README.md)) |
| `adaptadores/adaptador-se8-jee` | Adaptador de SE-8 en **Jakarta EE** (WildFly 42, Java 21, Maven): alertas a SE-8 desde `q.salida.se8`, estado de entrega con acuse, EV-23 y EV-29 ([README](adaptadores/adaptador-se8-jee/README.md)) |
| `simuladores/simulador-qa14` | Simulador de QA-14 (120 evaluaciones, 5 conflictos, lote enviado dos veces) |
| `simuladores/sim-se6` | Simulador de SE-6 ([README](simuladores/sim-se6/README.md)) |
| `simuladores/sim-se7` | Simulador de SE-7 con estado y caída simulada para QA-01 ([README](simuladores/sim-se7/README.md)) |
| `simuladores/sim-se8` | Simulador de SE-8: alertas idempotentes, acuse por aviso y por consulta, caída ([README](simuladores/sim-se8/README.md)) |
| `datos/geo` | Capa de municipios que entrega SE-2 |
| `gateway/` | API Gateway (YARP): catálogo de rutas, JWT, priorización y degradación, correlación ([README](gateway/README.md)) |
| `consola/` | Consola de coordinación (Angular), servida por nginx detrás del gateway ([README](consola/README.md)) |
| `despliegue/bus` | Topología del bus declarada al desplegar: exchange, colas por consumidor, políticas y usuarios (DA-03) |
| `despliegue/keycloak` | Realm `plataforma` (clientes `consola`, `movil`, `gateway`, usuarios de prueba) y tema de la pantalla de ingreso |
| `despliegue/compose` | Plataforma en un computador |
| `despliegue/k8s` | Kubernetes local (Minikube en WSL) con HPA y KEDA |
| `pruebas/humo` | Pruebas de humo de punta a punta por el gateway, QA-11 medido y resolución de nombres del gateway |
| `pruebas/ENCARGO-carga-y-caos.md` | Encargo de carga (k6) y caos; no ejecutado en esta entrega (ver límites en `docs/decisiones/REVISION_ARQUITECTURA.md`) |

## Ejecutar la plataforma

Requiere Docker Desktop.

```bash
docker compose -f despliegue/compose/docker-compose.yml up -d --build
bash pruebas/humo/prueba-humo.sh
bash pruebas/humo/prueba-desastres.sh
python -I pruebas/humo/prueba-qa11.py
bash pruebas/humo/prueba-se8.sh
python -I pruebas/medidas/latencias.py
dotnet run --project simuladores/simulador-qa14 -- --url http://localhost:8080 --keycloak http://localhost:8180
```

- Gateway (consola y API): http://localhost:8080 (y 8081, la segunda réplica). La API exige
  `Authorization: Bearer` con un token de Keycloak; las pruebas de humo lo piden solas.
- Keycloak: http://localhost:8180 (realm `plataforma`; administración `admin` / `admin_local`). Usuarios de prueba en
  `docs/encargos/convenciones.md`, con clave usuario + `_local`.
- Panel de RabbitMQ: http://localhost:15672 (`admin` / `admin_local`). Clúster de 3 nodos; las colas quorum tienen 3 miembros.
- Panel de MinIO: http://localhost:9001 (`minioadmin` / `minioadmin`)

Las claves son de desarrollo. Para cambiarlas o cambiar los puertos, copie `despliegue/compose/.env.ejemplo` a
`despliegue/compose/.env`. Detener: `docker compose -f despliegue/compose/docker-compose.yml down` (con `-v` borra
también los datos).

Orden de arranque: PostgreSQL → migrador (roles, esquema, datos de prueba) → bus → servicios (2 réplicas de cada
uno) → consola y gateway. Cada servicio solo está listo cuando su base y su topología del bus responden.

Se puede reconstruir un solo servicio (`docker compose -p plataforma up -d --build <servicio>`) sin tocar nada más: el
gateway vuelve a resolver los nombres de los servicios cada minuto y encuentra las IP nuevas
(`pruebas/humo/prueba-resolucion-gateway.sh` lo comprueba).

Si se recrea Keycloak, reinicie también el gateway y los servicios (`docker compose -p plataforma restart`): guardan en
caché las llaves de firma y rechazarían los tokens nuevos hasta por una hora.

## App móvil contra la plataforma

1. Emulador de Android encendido: `adb install -r ../danos/entrega/DanosCampo.apk` y `adb reverse tcp:8180 tcp:8180`
   (el emisor de los tokens es `http://localhost:8180` y el `iss` debe coincidir con el que valida el gateway).
2. En la pantalla **Ingreso** de la app: URL `http://10.0.2.2:8080` y **Probar conexión** (consulta `/salud/listo`).
3. **Iniciar sesión**: `bomberos.operador` registra desastres desde campo; `evaluador.armenia` hace evaluaciones.
4. Si el 8080 del computador está ocupado por otro programa, levante el gateway en otro puerto
   (`PUERTO_WEB=18080 PUERTO_WEB_REPLICA=18081 docker compose ...`) y use `http://10.0.2.2:18080` en la app. Verifique que
   el puerto esté libre también en `127.0.0.1`. La consola solo inicia sesión en `http://localhost:8080`.

El recorrido completo, con la lista de capturas, está en [`docs/RECORRIDO.md`](docs/RECORRIDO.md). Las medidas por
escenario de calidad y los límites declarados están en `docs/decisiones/REVISION_ARQUITECTURA.md`.

## Compilar y probar

No hace falta instalar el SDK: `herramientas/dotnet.sh` lo ejecuta en un contenedor (con acceso a Docker para
Testcontainers).

```bash
bash herramientas/dotnet.sh test Plataforma.sln
mvn -f adaptadores/adaptador-se8-jee/pom.xml verify   # adaptador de SE-8 (Java 21 y Maven; las de integración usan Docker)
```

Las pruebas de integración levantan PostgreSQL 18 y RabbitMQ 4.4 reales con la topología de `despliegue/bus`.
Verifican, entre otras cosas, 0 eventos perdidos con el consumidor detenido, secuencia sin huecos con dos relevos,
mensajes envenenados apartados sin bloquear la cola, cola llena sin pérdida, y que el servicio no puede modificar el
historial.

## Bus de eventos

- Un solo exchange `plataforma.eventos` (topic) con claves `evNN.agregado.hecho`. El catálogo está en
  `comun/Plataforma.Comun.Eventos/CatalogoEventos.cs`.
- Una cola quorum acotada por consumidor (`q.<servicio>`), con su cola de mensajes muertos (`dlq.<servicio>`).
  Al llenarse rechaza la publicación y el evento se queda en la bandeja del productor: nunca se descarta.
- La topología se genera con `python despliegue/bus/generar_definiciones.py` (colas, enlaces, políticas y usuarios
  con mínimo privilegio). Los servicios no la declaran; solo la verifican.

### Regla del contrato

El contrato solo admite cambios **aditivos** (ATAM R-03): agregar un campo o un evento. Un cambio de significado es una
versión nueva del evento, que convive con la anterior. Cada esquema tiene un ejemplo válido en
`comun/pruebas/Plataforma.Comun.Pruebas/Contrato/EjemplosContrato.cs`.

## Kubernetes (local)

```bash
bash despliegue/k8s/aplicar.sh
```

Solo trabaja contra el contexto `minikube` y lo pasa explícito en cada comando, para no desplegar por error en
otro clúster. Se ejecuta dentro de WSL, donde corre Minikube; ver las instrucciones al inicio del script. Instala el Cluster Operator de RabbitMQ (v2.23.2) y KEDA (v2.17.0, escala los adaptadores de 0 a N),
crea el namespace `plataforma` y deja la consola en `http://<minikube ip>:30080`.

> **Límite conocido.** Keycloak solo admite redirecciones a `http://localhost:30080/*` y fija el emisor en
> `http://localhost:8180`, y su Service no se expone fuera del clúster. Para iniciar sesión desde el navegador del
> host hace falta reenviar ambos puertos a `localhost` (`kubectl port-forward` de `web` a 30080 y de `keycloak` a 8180).
> El despliegue en Kubernetes no se verificó de punta a punta en esta entrega; la plataforma verificada es la de compose.
