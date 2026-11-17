# Encargo: pruebas de carga (k6) y de caos

Este archivo es **autocontenido**: un agente (o una persona) puede hacer el trabajo solo con este repositorio. Explica qué
medir, contra qué rutas, con qué identidad y cómo correrlo en la plataforma de compose.

## Objetivo

Cada escenario de calidad del SRS tiene una **medida numérica**, y la sustentación exige demostrarla (CON-7). Aquí van los
scripts que **producen esas medidas**:
- carga para QA-05, QA-06 y QA-07;
- caos para QA-01, QA-02 y QA-04.

**Alcance:** solo las carpetas nuevas `pruebas/carga-k6/` y `pruebas/caos/` y el archivo `pruebas/LEEME.md`. No modifique
los servicios, `comun/` ni `despliegue/`; si algo impide medir, repórtelo.

**Prohibido:** `docker compose down -v`, borrar volúmenes, `docker system prune` y `kubectl`.

## Las medidas (texto del SRS, `docs/SRS.pdf`, §5.3)

| Escenario | Estímulo y entorno | Medida |
|---|---|---|
| **QA-01** Fallo de un sistema externo (Cuadro 24) | Tres invocaciones consecutivas del adaptador agotan su tiempo de espera. El SRS usa SE-3 como ejemplo; **en esta entrega el sistema externo integrado es SE-7**, con su simulador `sim-se7` | (a) Ninguna función que no dependa del sistema externo queda indisponible, y su latencia no se degrada más de un 10 % respecto de su línea base. (b) La indisponibilidad se refleja en el camino de consulta en ≤ 30 s desde el tercer tiempo agotado. (c) Al volver, el 100 % de los mensajes encolados se procesa en ≤ 15 min, sin duplicados. (d) 0 mensajes perdidos |
| **QA-02** Disponibilidad de los servicios críticos (Cuadro 25) | Un componente de un servicio crítico deja de responder: registro de eventos, consulta del estado de un desastre, generación de alertas | Disponibilidad mensual ≥ 99,95 %. **Detección y reencaminamiento ≤ 30 s desde el fallo.** Un servicio cuenta como indisponible cuando dos sondeos consecutivos fallan o exceden la latencia de QA-06. En compose se mide la detección y el reencaminamiento al tumbar una réplica (cada servicio tiene 2) y un nodo del bus (son 3). El mes calendario se declara como no medible en el prototipo |
| **QA-04** Fallo de un componente interno (Cuadro 27) | Un componente responde con error o no responde durante 60 s; la plataforma sigue con carga | (a) Ninguna función crítica que no dependa del componente queda indisponible. (b) La propagación se corta en ≤ 60 s. (c) ≤ 1 % de errores en funciones críticas durante los 5 min posteriores. (d) **0 eventos encolados perdidos.** (e) Al restablecerse, la cola pendiente se procesa en ≤ 10 min y sin duplicados |
| **QA-05** Aumento repentino de usuarios (Cuadro 28) | La concurrencia pasa de 1000 a 10 000 usuarios en 10 min, sobre el camino de lectura (estado, tablero y mapa) | (a) Con 10 000 usuarios, p95 < 2 s y p99 < 4 s, a más tardar 5 min después del umbral de disparo. (b) El incremento de capacidad se completa en ≤ 5 min. (c) 0 líneas de código modificadas. (d) La escritura no crece más de un 20 %. (e) Bajo degradación, el 100 % de las peticiones de la consola se atiende dentro del objetivo |
| **QA-06** Consulta frecuente (Cuadro 29) | Usuario de la consola pide el estado consolidado de un desastre activo con su mapa, con 10 000 usuarios concurrentes | p95 < 2 s y p99 < 4 s en el punto de entrada (ventanas de 15 min). Antigüedad del dato ≤ 2 s en el p95. `datoAl` visible en el 100 % de las respuestas |
| **QA-07** Registro de un evento (Cuadro 30) | Registro de desastres nuevos con los 12 atributos, hasta 50 por minuto, con 10 000 usuarios concurrentes | p95 < 3 s y p99 < 6 s hasta la confirmación con identificador. 0 confirmaciones sin persistencia (conciliar las confirmaciones con lo almacenado) |

**La máquina de desarrollo tiene unos 16 GB de RAM.** Defina dos perfiles con una variable de entorno:
- `completo`: con los números del SRS;
- `local`: reducido, por ejemplo 1/20 de los usuarios.

Documente que la medida válida es la del perfil completo y cómo se declara la del local.

## La plataforma

- Se levanta con `docker compose -p plataforma up -d --build` desde `despliegue/compose`.
  - Puerto de entrada: `http://localhost:8080` (nginx hoy, el gateway después).
  - Red de compose: `plataforma_default`.
  - Nombres de los servicios: ver `docker-compose.yml`. Cada servicio de aplicación tiene 2 réplicas.
- **Administración de RabbitMQ:** `http://bus1:15672` desde un contenedor en esa red, usuario `admin`, clave `admin_local`.
  - Colas: `q.<servicio>`.
  - Colas de mensajes muertos: `dlq.<cola>`.
- **Ejemplos exactos de cuerpos e identidad** en `pruebas/humo/prueba-desastres.sh` y `pruebas/humo/prueba-humo.sh`. El
  segundo es el escenario E completo:
  1. Gestión de Desastres registra el desastre.
  2. Evaluación de Daños sincroniza y valida las evaluaciones.
  3. Reconstrucción crea el proyecto y lo lleva hasta el cierre.
  4. El desastre se cierra.

  Los cuerpos JSON con tildes deben ir por la entrada estándar de curl (`--data-binary @-`): en Git Bash, pasarlos como
  argumento daña la codificación.

### Rutas útiles

| Servicio | Rutas |
|---|---|
| Consulta (lectura de desastres) | `GET /v1/consulta/desastres`, `GET /v1/consulta/desastres/{id}`, `GET /v1/consulta/mapa?soloActivos=`, `GET /v1/consulta/desastres/{id}/evolucion?en=`, `GET /v1/integraciones` (estado de los sistemas externos, para QA-01 b) |
| Gestión de Desastres | `POST /v1/desastres` (QA-07), `PATCH /v1/desastres/{id}/severidad`, `PUT /v1/desastres/{id}/zona`, `POST /v1/desastres/{id}/transiciones`, `GET /v1/desastres/{id}`, `GET /v1/catalogos/tipos-desastre` |
| Consulta de Daños | `GET /v1/consulta-danos/mapa?desastre=`, `/consolidado?desastre=`, `/indicadores-revision`. Si una ruta responde 404, el script debe omitirla, no fallar |
| Evaluación de Daños | `GET /v1/descarga`, `POST /v1/sincronizacion/evaluaciones`, `POST /v1/evaluaciones/{id}/validacion` |
| Reconstrucción | `/v1/reconstruccion/desastres`, `/v1/reconstruccion/candidatas?desastre=`, `/v1/proyectos:lote`, `/v1/proyectos/{id}/...` |
| Consulta del Programa | `/v1/programa/consolidado?desastre=` |
| Simulador de SE-7 | `POST /admin/caida` con `{"modo":"on"|"off"|"lento"}`, en la red de compose, para QA-01 |

### Identidad

Use la variable `MODO_IDENTIDAD=encabezados|jwt`.

**Hoy (`encabezados`):**
- Gestión de Desastres, Consulta, Evaluación de Daños y Auditoría leen `X-Usuario`, `X-Roles` (separados por coma),
  `X-Organizacion` y `X-Ambito`.
- Reconstrucción lee `X-Usuario`, `X-Rol` y `X-Ambito`.
- Ejemplos en los scripts de humo.

**Con el gateway (`jwt`):**
- Keycloak en `http://localhost:8180`, realm `plataforma`.
- Token por *password grant* con el cliente `movil`, habilitado solo en el realm de desarrollo.
- Usuarios de prueba, con clave igual al usuario más `_local`:

| Usuario | Rol | Organización |
|---|---|---|
| `evaluador.armenia` | evaluador | `alcaldia-armenia` |
| `ungrd.operador` | autoridad | `ungrd`, ámbito NACIONAL |
| `bomberos.operador` | organismo | `bomberos` |
| `operador.danos` | operador | |
| `admin.catalogo` | administrador | |
| `auditor.1` | auditor | |
| `ing.perez` | responsable-proyecto | |

- Cabecera `Authorization: Bearer <token>`. Renueve el token si vence.

## Qué entregar

1. **`pruebas/carga-k6/`:** un script por escenario (QA-05, QA-06 y QA-07) con imagen `grafana/k6` de versión fija.
   - Los `thresholds` deben ser los de la tabla, con la cita del escenario en un comentario.
   - `setup()` crea sus datos por la API, con un prefijo propio, y nunca borra datos ajenos.
   - La mezcla de tráfico es realista: domina la lectura del mapa y del tablero.
   - Exporte el resumen en JSON (`--summary-export`) a `pruebas/resultados/`, ignorada por git.
2. **`pruebas/caos/`:** un script bash por escenario (QA-01, QA-02 y QA-04), ejecutable en Git Bash y en Linux. Cada uno
   sigue la estructura preparación → falla → carga → verificación → restauración → medida.
   - **Siempre restaura** lo que detuvo, con `trap ... EXIT`.
   - Imprime la medida en una línea, por ejemplo `MEDIDA QA-04: eventos_perdidos=0 recuperacion_s=12.3`.
   - Sale con un código distinto de 0 si no cumple.
   - Ejemplo de QA-04: detener las réplicas de `consulta`, generar N cambios, verificar que `q.consulta` acumula,
     levantarlas, y comprobar que la proyección refleja los N cambios y que `dlq.consulta` sigue en 0.
3. **`pruebas/LEEME.md`:** una tabla escenario → script → medida del SRS → cómo leer el resultado, más cómo correr cada
   uno con `docker run --rm --network plataforma_default ...`.
4. **Verificación mínima sin la plataforma:** `bash -n` sobre cada script de caos y `k6 inspect` sobre cada script de k6.
