# Simulador de SE-7 (sistema de proyectos de infraestructura)

API ASP.NET pequeña con estado en memoria (DA-11) que hace de SE-7 para el adaptador de SE-7 y para QA-01. Es "el otro
sistema": no referencia nada de la plataforma y usa su propio vocabulario (obras, versiones, situación). Se descartó
WireMock porque no modela el estado (el avance cambia con el tiempo) ni la marca de avance.

| Ruta | Uso |
|---|---|
| `PUT /v1/obras/{codigoObra}/versiones/{version}` | Registra una versión de una obra. Idempotente por obra y versión: 201 si es nueva, 200 si ya la tenía. 422 si el cuerpo es inválido |
| `GET /v1/cambios?desde={marca}&limite={n}` | Cambios de SE-7 (AVANCE, CONTRATISTA) posteriores a la marca, en orden. La marca es la secuencia del último cambio de la página. La misma marca devuelve siempre los mismos cambios |
| `GET /v1/salud` | Sondeo del adaptador (sujeto a la caída) |
| `POST /admin/caida` | `{"modo":"on"}` responde 503 en `/v1`, `{"modo":"lento"}` responde después de `RetrasoLentoMs` (tiempos de espera agotados, el estímulo de QA-01), `{"modo":"off"}` vuelve a la normalidad |
| `POST /admin/cambios` | Agrega un cambio a mano: `{"codigoObra":"…","clase":"AVANCE","porcentajeAvance":40}` o `{"codigoObra":"…","clase":"CONTRATISTA","contratista":"…"}` |
| `POST /admin/generar` | Ejecuta una ronda de generación |
| `GET /admin/estado` | Obras, versiones recibidas, entregas repetidas y cambios generados |
| `GET /salud/vivo` | Sondeo del contenedor (no le afecta la caída) |

Con el tiempo (`Simulador__IntervaloGeneracionSegundos`, 20 s; 0 lo apaga) una obra **APROBADA** sin contratista
recibe uno, y una obra **EN_OBRA** avanza entre 5 y 15 puntos hasta 100. Lo que llega de la plataforma no se reporta de
vuelta como cambio: SE-7 solo reporta lo que nace en él.
