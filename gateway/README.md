# API Gateway (YARP)

Única entrada de la consola y de la API (AD-04). Publicado en el puerto 8080 del host (8081 la segunda réplica en
compose; en k8s, NodePort 30080).

| Pieza | Dónde | Decisión |
|---|---|---|
| Catálogo de rutas (57 rutas; 25 de modificación, 12 con regla de dominio) | `src/Plataforma.Gateway/rutas.json` | DA-12, E-09 |
| No arranca si una ruta de modificación no declara `reglaDominio` | `Catalogo/CatalogoRutas.cs` | R-07 |
| Valida el JWT (mismo validador que los servicios), aplica el rol de la ruta (401/403), reenvía el mismo token y descarta los `X-*` de identidad | `Seguridad/AutorizacionNivel1.cs`, `Registro.cs` | DA-15 |
| `traceparent` y `X-Id-Correlacion` en cada petición | `Correlacion/Correlacion.cs` | DA-20 |
| Limitador por clase de tráfico (consola reservada; portal y móvil comparten) y degradación de `GET /v1/consulta*` a la última respuesta (`X-Degradado: true`, `degradado: true`) | `Prioridad/Priorizacion.cs` | AD-19, DA-16, CON-6 |
| Vuelve a resolver los nombres de los servicios (`PooledConnectionLifetime` 1 min) | `Registro.cs` (`Gateway:Conexiones`) | — |

Metadatos de cada ruta: `servicio`, `roles` (roles del realm separados por coma, `*` = cualquier autenticado,
`publico` = sin token: la consola y el tablero de daños), `claseTrafico` (`consola`, `movil`, `portal`) y `reglaDominio` (`ninguna` o el nombre
de la regla de nivel 2 del servicio).

## Decisiones de implementación

- **`/tablero-danos/{**resto}` es `publico` y solo `GET` (E-41):** es una página con sesión propia. El navegador manda su
  cookie, no un `Bearer`, y Consulta de Daños autoriza por rol. El catálogo prohíbe rutas públicas de modificación, así que
  el regreso de OIDC llega por `GET` (`response_mode=query`) y no hizo falta relajar R-07.
- **Validación del JWT con `ValidadorJwt` de `Plataforma.Comun.Seguridad` en lugar del middleware `JwtBearer` (DT-07, DA-15):**
  el gateway reutiliza exactamente la misma clase `ValidadorJwt` (`JsonWebTokenHandler` + `ConfigurationManager<OpenIdConnectConfiguration>`)
  que usan todos los servicios de dominio. Así se garantiza por construcción que el emisor (`iss`), la audiencia (`aud: gateway`),
  la extracción de `preferred_username`, `realm_access.roles`, `organizacion`, `ambito` y `territorio`, y la caché de llaves
  públicas (JWKS) ante caídas cortas de Keycloak se comporten de forma idéntica en el nivel 1 (gateway) y en el nivel 2 (servicios),
  y que los errores 401/403 respondan siempre con el cuerpo `{ codigo, mensaje }` de la plataforma sin revelar detalles del recurso (QA-11 d).
  *Alternativa descartada:* configurar el middleware estándar `AddAuthentication().AddJwtBearer()` de ASP.NET Core en el gateway,
  descartada porque duplicaría la lógica de extracción de claims de `realm_access.roles` y el manejo de caché ante fallos del IdP
  que ya están centralizados y probados en `Plataforma.Comun.Seguridad`.
- **Dos réplicas del gateway en Docker Compose (`8080` y `8081`):** Docker Compose no reparte un mismo puerto del host entre
  varias réplicas de un servicio con `deploy.replicas: 2`. Por ello el rango `${PUERTO_WEB:-8080}-${PUERTO_WEB_REPLICA:-8081}:8080`
  publica la primera réplica en `8080` (entrada principal usada por clientes y pruebas) y la segunda en `8081` (disponible para
  verificar alta disponibilidad en local). En Kubernetes, el `Service` `gateway` balancea ambas réplicas detrás del mismo `NodePort` (`30080`).
- **Renovación de conexiones (`PooledConnectionLifetime = 1 min`):** el `SocketsHttpHandler` de YARP limita la vida de las conexiones
  en el pool a 1 minuto (`Gateway:Conexiones:VidaConexion`) para volver a resolver por DNS los nombres de los contenedores cuando
  Docker Compose o Kubernetes recrean un servicio con otra IP, evitando que el gateway quede apuntando a una IP vieja.

## Pruebas

- `bash herramientas/dotnet.sh test gateway/tests/Plataforma.Gateway.Pruebas/Plataforma.Gateway.Pruebas.csproj`
  (19 pruebas: catálogo y R-07, nivel 1, reenvío del JWT, correlación, saturación del portal con la consola dentro de
  su objetivo de 2 s).
- `python -I pruebas/humo/prueba-qa11.py`: QA-11 medido contra compose, con tokens reales. Imprime la tabla completa
  ruta → actor → código → nivel.
- `bash pruebas/humo/prueba-resolucion-gateway.sh`: reconstruye un servicio con IP nuevas y comprueba que el gateway lo
  encuentra sin recrearse.

## QA-11 (medido el 2026-11-17)

198 intentos de modificación por actores no autorizados sobre las 12 rutas con regla de dominio: **198 de 198 rechazados
(100,0 %)** — 141 por el gateway (nivel 1, `403`) y 57 por el servicio (nivel 2, 52 con `403` y 5 con `404` fuera de
jurisdicción según E-39). Falsos negativos: 0. Datos modificados: 0.

### Resumen por ruta del catálogo

| Ruta del catálogo | Intentos | Códigos |
|---|---|---|
| ed-actualizar | 20 | 403: 20 |
| ed-validacion | 18 | 403: 18 |
| gd-organismos-asignar | 10 | 403: 10 |
| gd-severidad | 20 | 403: 20 |
| gd-transiciones | 20 | 403: 20 |
| gd-zona | 10 | 403: 10 |
| rc-avance | 20 | 403: 20 |
| rc-documentos | 10 | 403: 10 |
| rc-lote | 8 | 403: 8 |
| rc-presupuesto | 16 | 403: 14, 404: 2 |
| rc-responsable | 8 | 403: 7, 404: 1 |
| rc-transiciones | 38 | 403: 36, 404: 2 |

### Tabla completa ruta → código (198 intentos)

| Ruta | Método | Regla de dominio | Operación | Actor | Código | Rechazó |
|---|---|---|---|---|---|---|
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_4 | bomberos.operador | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_1 | bomberos.operador | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_4 | evaluador.armenia | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_1 | evaluador.armenia | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_4 | operador.danos | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_1 | operador.danos | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_4 | admin.catalogo | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_1 | admin.catalogo | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_4 | auditor.1 | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_1 | auditor.1 | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_4 | ing.perez | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_1 | ing.perez | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_4 | ing.gomez | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_1 | ing.gomez | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_4 | alcaldia.armenia | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_1 | alcaldia.armenia | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_4 | alcaldia.calarca | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_1 | alcaldia.calarca | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_4 | gobernacion.quindio | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/severidad` | PATCH | organismo-responsable | NIVEL_1 | gobernacion.quindio | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/zona` | PUT | organismo-responsable | geometria, fuente | bomberos.operador | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/zona` | PUT | organismo-responsable | geometria, fuente | evaluador.armenia | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/zona` | PUT | organismo-responsable | geometria, fuente | operador.danos | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/zona` | PUT | organismo-responsable | geometria, fuente | admin.catalogo | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/zona` | PUT | organismo-responsable | geometria, fuente | auditor.1 | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/zona` | PUT | organismo-responsable | geometria, fuente | ing.perez | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/zona` | PUT | organismo-responsable | geometria, fuente | ing.gomez | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/zona` | PUT | organismo-responsable | geometria, fuente | alcaldia.armenia | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/zona` | PUT | organismo-responsable | geometria, fuente | alcaldia.calarca | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/zona` | PUT | organismo-responsable | geometria, fuente | gobernacion.quindio | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | CERRADO | bomberos.operador | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | EN_ATENCION | bomberos.operador | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | CERRADO | evaluador.armenia | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | EN_ATENCION | evaluador.armenia | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | CERRADO | operador.danos | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | EN_ATENCION | operador.danos | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | CERRADO | admin.catalogo | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | EN_ATENCION | admin.catalogo | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | CERRADO | auditor.1 | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | EN_ATENCION | auditor.1 | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | CERRADO | ing.perez | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | EN_ATENCION | ing.perez | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | CERRADO | ing.gomez | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | EN_ATENCION | ing.gomez | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | CERRADO | alcaldia.armenia | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | EN_ATENCION | alcaldia.armenia | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | CERRADO | alcaldia.calarca | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | EN_ATENCION | alcaldia.calarca | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | CERRADO | gobernacion.quindio | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/transiciones` | POST | organismo-responsable | EN_ATENCION | gobernacion.quindio | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/organismos` | PUT | organismo-responsable | responsable, participantes | bomberos.operador | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/organismos` | PUT | organismo-responsable | responsable, participantes | evaluador.armenia | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/organismos` | PUT | organismo-responsable | responsable, participantes | operador.danos | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/organismos` | PUT | organismo-responsable | responsable, participantes | admin.catalogo | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/organismos` | PUT | organismo-responsable | responsable, participantes | auditor.1 | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/organismos` | PUT | organismo-responsable | responsable, participantes | ing.perez | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/organismos` | PUT | organismo-responsable | responsable, participantes | ing.gomez | 403 | gateway (1) |
| `/v1/desastres/{id:guid}/organismos` | PUT | organismo-responsable | responsable, participantes | alcaldia.armenia | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/organismos` | PUT | organismo-responsable | responsable, participantes | alcaldia.calarca | 403 | servicio (2) |
| `/v1/desastres/{id:guid}/organismos` | PUT | organismo-responsable | responsable, participantes | gobernacion.quindio | 403 | servicio (2) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | nivelDano | ungrd.operador | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | descripcion | ungrd.operador | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | nivelDano | bomberos.operador | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | descripcion | bomberos.operador | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | nivelDano | admin.catalogo | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | descripcion | admin.catalogo | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | nivelDano | auditor.1 | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | descripcion | auditor.1 | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | nivelDano | ing.perez | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | descripcion | ing.perez | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | nivelDano | ing.gomez | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | descripcion | ing.gomez | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | nivelDano | alcaldia.armenia | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | descripcion | alcaldia.armenia | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | nivelDano | alcaldia.calarca | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | descripcion | alcaldia.calarca | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | nivelDano | gobernacion.quindio | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | descripcion | gobernacion.quindio | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | nivelDano | evaluador.armenia (otro-dispositivo) | 403 | servicio (2) |
| `/v1/evaluaciones/{id:guid}` | PATCH | dispositivo-autor | descripcion | evaluador.armenia (otro-dispositivo) | 403 | servicio (2) |
| `/v1/evaluaciones/{id:guid}/validacion` | POST | organizacion-participante | RECHAZADA | evaluador.armenia | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}/validacion` | POST | organizacion-participante | PENDIENTE | evaluador.armenia | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}/validacion` | POST | organizacion-participante | RECHAZADA | operador.danos | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}/validacion` | POST | organizacion-participante | PENDIENTE | operador.danos | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}/validacion` | POST | organizacion-participante | RECHAZADA | admin.catalogo | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}/validacion` | POST | organizacion-participante | PENDIENTE | admin.catalogo | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}/validacion` | POST | organizacion-participante | RECHAZADA | auditor.1 | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}/validacion` | POST | organizacion-participante | PENDIENTE | auditor.1 | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}/validacion` | POST | organizacion-participante | RECHAZADA | ing.perez | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}/validacion` | POST | organizacion-participante | PENDIENTE | ing.perez | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}/validacion` | POST | organizacion-participante | RECHAZADA | ing.gomez | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}/validacion` | POST | organizacion-participante | PENDIENTE | ing.gomez | 403 | gateway (1) |
| `/v1/evaluaciones/{id:guid}/validacion` | POST | organizacion-participante | RECHAZADA | alcaldia.armenia | 403 | servicio (2) |
| `/v1/evaluaciones/{id:guid}/validacion` | POST | organizacion-participante | PENDIENTE | alcaldia.armenia | 403 | servicio (2) |
| `/v1/evaluaciones/{id:guid}/validacion` | POST | organizacion-participante | RECHAZADA | alcaldia.calarca | 403 | servicio (2) |
| `/v1/evaluaciones/{id:guid}/validacion` | POST | organizacion-participante | PENDIENTE | alcaldia.calarca | 403 | servicio (2) |
| `/v1/evaluaciones/{id:guid}/validacion` | POST | organizacion-participante | RECHAZADA | gobernacion.quindio | 403 | servicio (2) |
| `/v1/evaluaciones/{id:guid}/validacion` | POST | organizacion-participante | PENDIENTE | gobernacion.quindio | 403 | servicio (2) |
| `/v1/proyectos:lote` | POST | jurisdiccion-territorial | idDesastre, idsEvaluaciones, claveIdempotencia | bomberos.operador | 403 | gateway (1) |
| `/v1/proyectos:lote` | POST | jurisdiccion-territorial | idDesastre, idsEvaluaciones, claveIdempotencia | evaluador.armenia | 403 | gateway (1) |
| `/v1/proyectos:lote` | POST | jurisdiccion-territorial | idDesastre, idsEvaluaciones, claveIdempotencia | operador.danos | 403 | gateway (1) |
| `/v1/proyectos:lote` | POST | jurisdiccion-territorial | idDesastre, idsEvaluaciones, claveIdempotencia | admin.catalogo | 403 | gateway (1) |
| `/v1/proyectos:lote` | POST | jurisdiccion-territorial | idDesastre, idsEvaluaciones, claveIdempotencia | auditor.1 | 403 | gateway (1) |
| `/v1/proyectos:lote` | POST | jurisdiccion-territorial | idDesastre, idsEvaluaciones, claveIdempotencia | ing.perez | 403 | gateway (1) |
| `/v1/proyectos:lote` | POST | jurisdiccion-territorial | idDesastre, idsEvaluaciones, claveIdempotencia | ing.gomez | 403 | gateway (1) |
| `/v1/proyectos:lote` | POST | jurisdiccion-territorial | idDesastre, idsEvaluaciones, claveIdempotencia | alcaldia.calarca | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/presupuesto` | PATCH | jurisdiccion-territorial | presupuesto | bomberos.operador | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/presupuesto` | PATCH | jurisdiccion-territorial | fuenteFinanciacion | bomberos.operador | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/presupuesto` | PATCH | jurisdiccion-territorial | presupuesto | evaluador.armenia | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/presupuesto` | PATCH | jurisdiccion-territorial | fuenteFinanciacion | evaluador.armenia | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/presupuesto` | PATCH | jurisdiccion-territorial | presupuesto | operador.danos | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/presupuesto` | PATCH | jurisdiccion-territorial | fuenteFinanciacion | operador.danos | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/presupuesto` | PATCH | jurisdiccion-territorial | presupuesto | admin.catalogo | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/presupuesto` | PATCH | jurisdiccion-territorial | fuenteFinanciacion | admin.catalogo | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/presupuesto` | PATCH | jurisdiccion-territorial | presupuesto | auditor.1 | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/presupuesto` | PATCH | jurisdiccion-territorial | fuenteFinanciacion | auditor.1 | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/presupuesto` | PATCH | jurisdiccion-territorial | presupuesto | ing.perez | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/presupuesto` | PATCH | jurisdiccion-territorial | fuenteFinanciacion | ing.perez | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/presupuesto` | PATCH | jurisdiccion-territorial | presupuesto | ing.gomez | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/presupuesto` | PATCH | jurisdiccion-territorial | fuenteFinanciacion | ing.gomez | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/presupuesto` | PATCH | jurisdiccion-territorial | presupuesto | alcaldia.calarca | 404 | servicio (2) |
| `/v1/proyectos/{id:guid}/presupuesto` | PATCH | jurisdiccion-territorial | fuenteFinanciacion | alcaldia.calarca | 404 | servicio (2) |
| `/v1/proyectos/{id:guid}/responsable` | PATCH | jurisdiccion-territorial | responsable | bomberos.operador | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/responsable` | PATCH | jurisdiccion-territorial | responsable | evaluador.armenia | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/responsable` | PATCH | jurisdiccion-territorial | responsable | operador.danos | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/responsable` | PATCH | jurisdiccion-territorial | responsable | admin.catalogo | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/responsable` | PATCH | jurisdiccion-territorial | responsable | auditor.1 | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/responsable` | PATCH | jurisdiccion-territorial | responsable | ing.perez | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/responsable` | PATCH | jurisdiccion-territorial | responsable | ing.gomez | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/responsable` | PATCH | jurisdiccion-territorial | responsable | alcaldia.calarca | 404 | servicio (2) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | CANCELAR | bomberos.operador | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | CANCELAR | evaluador.armenia | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | CANCELAR | operador.danos | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | CANCELAR | admin.catalogo | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | CANCELAR | auditor.1 | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | CANCELAR | ing.perez | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | CANCELAR | ing.gomez | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | CANCELAR | alcaldia.calarca | 404 | servicio (2) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | APROBAR | ungrd.operador | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | APROBAR | bomberos.operador | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | APROBAR | evaluador.armenia | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | APROBAR | operador.danos | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | APROBAR | admin.catalogo | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | APROBAR | auditor.1 | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | APROBAR | ing.perez | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | APROBAR | ing.gomez | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | APROBAR | alcaldia.calarca | 404 | servicio (2) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | APROBAR | gobernacion.quindio | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | INICIAR | ungrd.operador | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | FINALIZAR | ungrd.operador | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | INICIAR | bomberos.operador | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | FINALIZAR | bomberos.operador | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | INICIAR | evaluador.armenia | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | FINALIZAR | evaluador.armenia | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | INICIAR | operador.danos | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | FINALIZAR | operador.danos | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | INICIAR | admin.catalogo | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | FINALIZAR | admin.catalogo | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | INICIAR | auditor.1 | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | FINALIZAR | auditor.1 | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | INICIAR | ing.gomez | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | FINALIZAR | ing.gomez | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | INICIAR | alcaldia.armenia | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | FINALIZAR | alcaldia.armenia | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | INICIAR | alcaldia.calarca | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | FINALIZAR | alcaldia.calarca | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | INICIAR | gobernacion.quindio | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/transiciones` | POST | jurisdiccion-o-responsable-asignado | FINALIZAR | gobernacion.quindio | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | avance | ungrd.operador | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | costo, concepto | ungrd.operador | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | avance | bomberos.operador | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | costo, concepto | bomberos.operador | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | avance | evaluador.armenia | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | costo, concepto | evaluador.armenia | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | avance | operador.danos | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | costo, concepto | operador.danos | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | avance | admin.catalogo | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | costo, concepto | admin.catalogo | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | avance | auditor.1 | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | costo, concepto | auditor.1 | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | avance | ing.gomez | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | costo, concepto | ing.gomez | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | avance | alcaldia.armenia | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | costo, concepto | alcaldia.armenia | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | avance | alcaldia.calarca | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | costo, concepto | alcaldia.calarca | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | avance | gobernacion.quindio | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/avance` | POST | responsable-asignado | costo, concepto | gobernacion.quindio | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/documentos` | POST | responsable-asignado | archivo | ungrd.operador | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/documentos` | POST | responsable-asignado | archivo | bomberos.operador | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/documentos` | POST | responsable-asignado | archivo | evaluador.armenia | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/documentos` | POST | responsable-asignado | archivo | operador.danos | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/documentos` | POST | responsable-asignado | archivo | admin.catalogo | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/documentos` | POST | responsable-asignado | archivo | auditor.1 | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/documentos` | POST | responsable-asignado | archivo | ing.gomez | 403 | servicio (2) |
| `/v1/proyectos/{id:guid}/documentos` | POST | responsable-asignado | archivo | alcaldia.armenia | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/documentos` | POST | responsable-asignado | archivo | alcaldia.calarca | 403 | gateway (1) |
| `/v1/proyectos/{id:guid}/documentos` | POST | responsable-asignado | archivo | gobernacion.quindio | 403 | gateway (1) |
