# Convenciones para todos los agentes

> En estos encargos, "el orquestador" es quien coordina el trabajo y fusiona las ramas: la persona del equipo o su
> agente principal. El estado y el orden de los encargos están en `docs/encargos/LEEME.md`.

## Alcance de esta entrega

Esta entrega implementa **solo Móvil, Evaluación de Daños, Mapa y visualización de desastres, y Reconstrucción**. El
detalle y el reparto están en la sección "Alcance de esta entrega y reparto" de `docs/encargos/LEEME.md` y en la nota al
inicio de cada encargo. `docs/decisiones/A_IMPLEMENTAR.md` permite implementar más ítems del enunciado, pero en esta
entrega no se hace nada fuera de ese alcance: si un encargo lo pide, se reporta en vez de hacerlo.

Lea esto completo antes de empezar. Si algo de su encargo contradice este archivo, gana este archivo; repórtelo.

## Fuentes de verdad, en orden

1. `el repositorio\docs\` — los PDF del SRS, SAD, ATAM y el diseño de Reconstrucción. Para leer un PDF
   use `pdftotext -layout <pdf> <salida>`, que está instalado en Git Bash. **Los PDF no se modifican.**
2. `docs/decisiones/REVISION_ARQUITECTURA.md` — hallazgos (H-nn), decisiones de arquitectura (DA-nn) y de tecnología
   (DT-nn) ya tomadas con el usuario.
3. `docs/decisiones/ENMIENDAS_DOCUMENTOS.md` — correcciones aditivas a los documentos (E-nn): eventos nuevos (EV-21 a
   EV-27), campos agregados, consumidores.
4. `README.md` de la raíz del repositorio y de `servicios/reconstruccion`.

Si una decisión que necesita **no está** en esas fuentes, tómela con el criterio de los atributos de calidad (CON-7),
anótela en su reporte con su justificación y la alternativa descartada, y siga. Si es una decisión que cambia el alcance
o contradice una fuente, **no la tome**: repórtela como pregunta.

## Repositorio y herramientas

- Repositorio: `proyectoparte2` (monorepo). Usted trabaja en **su propio worktree y su propia rama**. Haga commits en su
  rama, en español, descriptivos y cortos, terminados con la línea de coautoría de su modelo, por ejemplo
  `Co-Authored-By: <su modelo> <noreply@...>`.
  **No haga push, no fusione con otras ramas, no toque `main`.**
- No hay SDK de .NET instalado: use `bash herramientas/dotnet.sh <comando>` (corre el SDK 10 en un contenedor, con acceso
  a Docker para Testcontainers). Ejemplos: `bash herramientas/dotnet.sh test Plataforma.sln`,
  `bash herramientas/dotnet.sh ef migrations add Inicial --project <p> --startup-project <p> --context <Ctx> --output-dir <dir>`.
- Python: use `python -I` (el `python3` de Windows es un acceso directo de la tienda).
- Paquetes NuGet: versiones centralizadas en `Directory.Packages.props`. Ya están los que se prevén (PostGIS con
  NetTopologySuite, GeoJSON, Redis, YARP, JwtBearer, Http.Resilience, Testcontainers de PostgreSQL, RabbitMQ, Redis y
  Keycloak, Mvc.Testing). Si necesita otro, use una versión publicada hace **más de dos semanas** y anótelo en el reporte.

## Lo que NO puede tocar (si necesita un cambio, repórtelo y lo hace el orquestador)

- `comun/` completo: el contrato de eventos (`Plataforma.Comun.Eventos`) está **congelado**; la mensajería, el migrador,
  la salud y la seguridad son compartidos.
- `servicios/` de otros, `consola/`, `despliegue/bus/` (la topología ya tiene sus colas y usuarios), los PDF de `docs/`.
- Docker: puede usar Testcontainers libremente. **No levante el compose completo** (`docker compose ... up` del proyecto
  `plataforma`): sus puertos chocan con los del orquestador. Nunca `down -v` sobre `plataforma`. **Nunca `kubectl` ni
  Minikube.**

## Arquitectura de cada servicio (AD-08, limpia y hexagonal)

Use `servicios/reconstruccion` como plantilla exacta:

| Proyecto | Contenido | Puede referenciar |
|---|---|---|
| `<Servicio>.Dominio` | Entidades, reglas, objetos de valor. Sin paquetes. | nada |
| `<Servicio>.Aplicacion` | Casos de uso (interactores), puertos de entrada y de salida, DTO, hechos. Sin paquetes. | Dominio |
| `<Servicio>.Infraestructura` | EF Core y migraciones, adaptadores de puertos, traducción de hechos al contrato (`EscritorBandeja`), adaptador de entrada de eventos (`IManejadorEvento`) | Aplicacion, `comun/*` |
| `<Servicio>.Api` | Controllers, presenters, `Program.cs` (cablea puertos y adaptadores), lectura de identidad con `IIdentidadActual` | Aplicacion, Infraestructura, `comun/Salud`, `comun/Seguridad` |
| `tests/<Servicio>.Pruebas` | Dominio, casos de uso con puertos en memoria, **prueba de arquitectura** (copie `Reconstruccion.Pruebas/Arquitectura`) | Dominio, Aplicacion |
| `tests/<Servicio>.Integracion` | Testcontainers: migraciones, permisos, eventos contra el contrato, consumidores | todo |

- El formato del bus (sobre, enumeraciones canónicas) **solo existe en Infraestructura**. La aplicación emite hechos
  propios por un puerto (ver `Reconstruccion.Aplicacion/Hechos.cs` y `PublicadorHechosBandeja`).
- Comentarios en español que expliquen **por qué**, citando AD-nn, RF-xx-nn, QA-nn o E-nn cuando aplique. Identificadores
  en español, como el resto del código.

## Mensajería (ver `comun/Plataforma.Comun.Mensajeria` y `README.md`)

- Publicar: `services.AgregarBus(config)` + `services.AgregarPublicacion<TuDbContext>()`; en el modelo
  `modelo.ConfigurarBandejaSalida()`; escriba con `EscritorBandeja<TuDbContext>.Agregar(Sobre.Crear(...))` dentro de la
  misma unidad de trabajo que el cambio (AD-45). El escritor valida contra el esquema: un evento inválido no sale.
- Consumir: `services.AgregarConsumo<TuDbContext, TuManejador>()` + `modelo.ConfigurarMensajesProcesados()`; su
  `IManejadorEvento` traduce el sobre a un caso de uso y no guarda por su cuenta (el consumidor confirma en una
  transacción con el registro de idempotencia). Ignore los tipos que no maneja.
- `claveIdempotencia`: `<agregado>:<id>:v<version>` o equivalente estable para el hecho.
- Ejemplos válidos de cada evento: `comun/pruebas/Plataforma.Comun.Pruebas/Contrato/EjemplosContrato.cs`.
- Usuarios y colas del bus ya declarados (`despliegue/bus/generar_definiciones.py`): el usuario es el nombre del servicio
  y su clave de desarrollo es `<servicio con guiones bajos>_local` (por ejemplo `evaluacion_danos_local`). La cola es
  `q.<servicio>`. Los servicios no declaran topología.

## Identidad (transición hasta el gateway)

En la capa Api use `IIdentidadActual` de `comun/Plataforma.Comun.Seguridad` (registro: `services.AgregarIdentidad(config)`).
Hoy lee los encabezados `X-Usuario`, `X-Roles` (separados por coma: `autoridad`, `responsable-proyecto`, `auditor`,
`evaluador`, `operador`, `organismo`, `administrador`), `X-Organizacion`, `X-Ambito`, `X-Territorio`, `X-Dispositivo`.
Con el gateway se cambia el adaptador a JWT y su servicio no se toca. Sin identidad: 401. Sin el rol: 403.
La autorización de nivel 2 (reglas que dependen de datos del dominio, AD-22) va en el dominio o en el caso de uso.

## Persistencia

- Una base por servicio (AD-01) en el servidor compartido `db` (imagen `postgis/postgis:18-3.6`, PostgreSQL 18 con
  PostGIS). Nombre de la base: el del servicio con guiones bajos. Rol de la aplicación: `<base>_app`.
- Esquema con migraciones de EF Core generadas desde el modelo (el modelo declara tipos, precisiones, checks e
  índices). En la migración, `GRANT` de mínimo privilegio al rol de la aplicación; los historiales son **solo
  `SELECT, INSERT`** (AD-41, DT-02). Si usa PostGIS: `CREATE EXTENSION IF NOT EXISTS postgis` en la migración.
- Modo migrador en el mismo ejecutable: argumento `migrar [--sembrar]`, con `Migrador.EjecutarAsync` de
  `comun/Plataforma.Comun.Migraciones` (ver `Reconstruccion.Infraestructura/Persistencia/Migracion.cs`).
- Sondeos: `app.MapearSalud(...)` y `AddDbContextCheck<...>("base", tags: ["listo"])`.

## Despliegue (escríbalo; el orquestador lo prueba)

- **Compose** (`despliegue/compose/docker-compose.yml`): agregue `<servicio>-migrador` (corre una vez) y `<servicio>`
  con 2 réplicas, `healthcheck` en `/salud/listo` y las variables con el patrón de `reconstruccion`. Sin puertos
  publicados al host. Imagen `plataforma/<servicio>:local` construida desde la raíz con
  `servicios/<servicio>/Dockerfile` (copie el de Reconstrucción).
- **Kubernetes**: un archivo propio `despliegue/k8s/base/<servicio>.yaml` (Job del migrador, Deployment de 2 réplicas
  con sondeos `/salud/listo` y `/salud/vivo`, PDB, Service) y agréguelo a `despliegue/k8s/base/kustomization.yaml`.
  Secretos en `00-base.yaml` con valores de desarrollo. **No ejecute nada de k8s.**
- Agregue sus proyectos a `Plataforma.sln` con `bash herramientas/dotnet.sh sln Plataforma.sln add --solution-folder <carpeta> <proyecto>`.

## Reporte final (lo que me devuelve al terminar)

1. Qué hizo, por requisito o decisión (RF/AD/DA/E).
2. Decisiones que tomó y que no estaban en las fuentes: justificación y alternativa descartada.
3. Desviaciones del encargo y por qué.
4. Pruebas ejecutadas y su resultado exacto (números).
5. Lo que quedó pendiente o no pudo verificar, sin maquillarlo.
6. Preguntas para el orquestador.
7. Rama y último commit.

## Contrato de identidad y de entrada (fijado el 2026-11-16; lo implementan el gateway y la app)

- **Keycloak 26**, realm `plataforma`, publicado en compose en el puerto **8180** del host
  (emisor `http://localhost:8180/realms/plataforma`, **el mismo para todos**: Keycloak fija `KC_HOSTNAME`, los contenedores
  descubren las llaves por `http://keycloak:8080` y el emulador de Android llega con `adb reverse tcp:8180 tcp:8180`).
  - Roles de realm: `autoridad`, `responsable-proyecto`, `auditor`, `evaluador`, `operador`, `organismo`, `administrador`.
  - Atributos de usuario emitidos como claims de primer nivel: `organizacion`, `ambito` (`LOCAL|REGIONAL|NACIONAL`), `territorio`.
  - Clientes: `consola` (público, PKCE S256), `movil` (público, PKCE S256, `offline_access`, redirección
    `co.plataforma.danos:/oauth2redirect`), `gateway` (audiencia: los tokens de `consola` y `movil` llevan `aud: gateway`).
  - Identidad en el token: `preferred_username` → usuario; `realm_access.roles` → roles; `organizacion`, `ambito`, `territorio`.
  - Usuarios de prueba (clave = nombre de usuario + `_local`): `evaluador.armenia` (evaluador, organizacion `alcaldia-armenia`),
    `ungrd.operador` (autoridad, `ungrd`, NACIONAL), `bomberos.operador` (organismo, `bomberos`), `operador.danos` (operador),
    `admin.catalogo` (administrador), `auditor.1` (auditor), `ing.perez` (responsable-proyecto),
    `alcaldia.armenia` (autoridad, LOCAL, territorio `63001`), `alcaldia.calarca` (autoridad, LOCAL, `63130`) y
    `gobernacion.quindio` (autoridad, REGIONAL, `63`).
- **Gateway (YARP)** publicado en el puerto **8080** del host: es la única entrada (consola y API). Las rutas de la API
  conservan su ruta pública (`/v1/descarga`, `/v1/sincronizacion/evaluaciones`, ...). Exige `Authorization: Bearer`.
- El encabezado `X-Dispositivo` **no es identidad**: la app lo sigue enviando como dato del dispositivo y se permite.
  `X-Usuario`, `X-Roles`, `X-Organizacion`, `X-Ambito` y `X-Territorio` dejan de aceptarse cuando `Identidad:Modo = Jwt`.
- Solo en el realm de desarrollo, el cliente `movil` admite *Direct Access Grants* (contraseña) para las pruebas automáticas de la app; la app nunca lo usa.
