# API Gateway (YARP)

Única entrada de la consola y de la API (AD-04). Publicado en el puerto 8080 del host (8081 la segunda réplica en
compose; en k8s, NodePort 30080).

| Pieza | Dónde | Decisión |
|---|---|---|
| Catálogo de rutas (55 rutas; 25 de modificación, 12 con regla de dominio) | `src/Plataforma.Gateway/rutas.json` | DA-12, E-09 |
| No arranca si una ruta de modificación no declara `reglaDominio` | `Catalogo/CatalogoRutas.cs` | R-07 |
| Valida el JWT (mismo validador que los servicios), aplica el rol de la ruta (401/403), reenvía el mismo token y descarta los `X-*` de identidad | `Seguridad/AutorizacionNivel1.cs`, `Registro.cs` | DA-15 |
| `traceparent` y `X-Id-Correlacion` en cada petición | `Correlacion/Correlacion.cs` | DA-20 |
| Limitador por clase de tráfico (consola reservada; portal y móvil comparten) y degradación de `GET /v1/consulta*` a la última respuesta (`X-Degradado: true`, `degradado: true`) | `Prioridad/Priorizacion.cs` | AD-19, DA-16, CON-6 |
| Vuelve a resolver los nombres de los servicios (`PooledConnectionLifetime` 1 min) | `Registro.cs` (`Gateway:Conexiones`) | — |

Metadatos de cada ruta: `servicio`, `roles` (roles del realm separados por coma, `*` = cualquier autenticado,
`publico` = solo la consola), `claseTrafico` (`consola`, `movil`, `portal`) y `reglaDominio` (`ninguna` o el nombre
de la regla de nivel 2 del servicio).

## Pruebas

- `bash herramientas/dotnet.sh test gateway/tests/Plataforma.Gateway.Pruebas/Plataforma.Gateway.Pruebas.csproj`
  (19 pruebas: catálogo y R-07, nivel 1, reenvío del JWT, correlación, saturación del portal con la consola dentro de
  su objetivo de 2 s).
- `python -I pruebas/humo/prueba-qa11.py`: QA-11 medido contra compose, con tokens reales. Imprime la tabla completa
  ruta → actor → código → nivel.
- `bash pruebas/humo/prueba-resolucion-gateway.sh`: reconstruye un servicio con IP nuevas y comprueba que el gateway lo
  encuentra sin recrearse.

## QA-11 (medido el 2026-11-17)

198 intentos de modificación por actores no autorizados sobre las 12 rutas con regla de dominio: 141 rechazados por el
gateway (nivel 1) y 57 por el servicio (nivel 2). 0 datos modificados.

| Ruta del catálogo | Intentos | Códigos |
|---|---|---|
| ed-actualizar | 20 | 403: 20 |
| ed-validacion | 18 | 403: 18 |
| gd-organismos-asignar | 10 | 403: 10 |
| gd-severidad | 20 | 403: 20 |
| gd-transiciones | 20 | 403: 16, **409: 4** |
| gd-zona | 10 | 403: 10 |
| rc-avance | 20 | 403: 20 |
| rc-documentos | 10 | 403: 10 |
| rc-lote | 8 | 403: 8 |
| rc-presupuesto | 16 | 403: 14, 404: 2 |
| rc-responsable | 8 | 403: 7, 404: 1 |
| rc-transiciones | 38 | 403: 36, 404: 2 |

**Pendiente en Gestión de Desastres:** los 4 casos 409 son de organismos no responsables que piden una transición que
no existe desde el estado actual. `Desastre.CambiarEstado` revisa la tabla de transiciones antes que
`ExigirResponsable`, así que responde `TRANSICION_NO_PERMITIDA` y revela el estado del evento (QA-11 d). No se modifica
nada, pero no es un rechazo de autorización. Corrección propuesta: llamar a `ExigirResponsable` (salvo en la
reapertura) antes de buscar la transición.
