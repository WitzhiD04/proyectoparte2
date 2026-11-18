# Simulador de SE-6 (sistema financiero)

API ASP.NET pequeña con estado en memoria (DA-11) que hace de SE-6 para el adaptador de SE-6 y para QA-01. Es "el otro
sistema": no referencia nada de la plataforma y usa su propio vocabulario (cuentas de proyecto, situación financiera y
lotes de movimientos).

| Ruta | Uso |
|---|---|
| `PUT /v1/lotes/{idLote}` | Registra un lote de movimientos financieros. Idempotente por elemento (`codigoProyecto` + `versionHistorial`): 201 si al menos un movimiento es nuevo, 200 si todos ya estaban registrados. 422 si el lote es inválido |
| `POST /v1/lotes` | Igual que `PUT /v1/lotes/{idLote}`, generando un identificador de lote |
| `GET /v1/movimientos?proyecto={codigo}` | Movimientos financieros recibidos (todos o los de un proyecto) |
| `GET /v1/proyectos`, `GET /v1/proyectos/{codigo}` | Cuentas financieras de los proyectos registrados en SE-6 |
| `GET /v1/salud` | Sondeo del adaptador (sujeto a la caída) |
| `POST /admin/caida` | `{"modo":"on"}` responde 503 en `/v1`, `{"modo":"lento"}` responde después de `RetrasoLentoMs` (tiempos de espera agotados, el estímulo de QA-01), `{"modo":"off"}` vuelve a la normalidad |
| `GET /admin/estado` | Proyectos, movimientos recibidos, lotes recibidos y entregas repetidas |
| `GET /salud/vivo` | Sondeo del contenedor (no le afecta la caída) |
