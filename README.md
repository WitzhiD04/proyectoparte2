# Plataforma para la Gestión de Desastres y Reconstrucción

Monorepo de la segunda entrega. La arquitectura está en `docs/` (SRS, SAD, ATAM y el diseño de Reconstrucción).
La app móvil de evaluación de daños vive en su propio repositorio (`danos`) y se integra por el contrato de la API.

## Estructura

| Carpeta | Contenido |
|---|---|
| `docs/` | SRS, SAD, ATAM y documento de diseño de Reconstrucción |
| `comun/Plataforma.Comun.Eventos` | **Contrato de eventos** (AD-09): sobre canónico, enumeraciones, catálogo EV-01 a EV-27, tipos de datos y esquemas JSON |
| `comun/Plataforma.Comun.Mensajeria` | Bandeja de salida, relevo con secuencia sin huecos, consumidor idempotente con cola de mensajes muertos (AD-02, AD-45) |
| `comun/Plataforma.Comun.Salud` | Sondeos `/salud/vivo` y `/salud/listo` (AD-24) |
| `comun/Plataforma.Comun.Migraciones` | Migrador: roles de mínimo privilegio, migraciones de EF Core y siembra (DT-19) |
| `servicios/reconstruccion` | Reconstrucción y Consulta del Programa ([README](servicios/reconstruccion/README.md)) |
| `consola/` | Consola de coordinación (Angular) |
| `despliegue/bus` | Topología del bus declarada al desplegar: exchange, colas por consumidor, políticas y usuarios (DA-03) |
| `despliegue/compose` | Plataforma en un computador |
| `despliegue/k8s` | Kubernetes local (Docker Desktop) |
| `pruebas/humo` | Prueba de humo de punta a punta por la API |
| `herramientas/` | `dotnet.sh`: el SDK de .NET 10 en un contenedor, sin instalarlo |

## Ejecutar la plataforma

Requiere Docker Desktop.

```bash
docker compose -f despliegue/compose/docker-compose.yml up -d --build
bash pruebas/humo/prueba-humo.sh
```

- Consola: http://localhost:8080
- Panel de RabbitMQ: http://localhost:15672 (`admin` / `admin_local`). Clúster de 3 nodos; las colas quorum tienen 3 miembros.
- Panel de MinIO: http://localhost:9001 (`minioadmin` / `minioadmin`)

Las claves son de desarrollo. Para cambiarlas o cambiar los puertos, copie `despliegue/compose/.env.ejemplo` a
`despliegue/compose/.env`. Detener: `docker compose -f despliegue/compose/docker-compose.yml down` (con `-v` borra
también los datos).

Orden de arranque: PostgreSQL → migrador (roles, esquema, datos de prueba) → bus → servicios (2 réplicas de cada
uno) → consola. Cada servicio solo está listo cuando su base y su topología del bus responden.

## Compilar y probar

No hace falta instalar el SDK: `herramientas/dotnet.sh` lo ejecuta en un contenedor (con acceso a Docker para
Testcontainers).

```bash
bash herramientas/dotnet.sh test Plataforma.sln
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

Solo trabaja contra el contexto `docker-desktop` y lo pasa explícito en cada comando, para no desplegar por error en
otro clúster. Requiere activar Kubernetes en Docker Desktop. Instala el Cluster Operator de RabbitMQ (v2.23.2),
crea el namespace `plataforma` y deja la consola en http://localhost:30080.
