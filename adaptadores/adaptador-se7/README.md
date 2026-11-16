# Adaptador de SE-7 (sistema de proyectos de infraestructura)

Integra la plataforma con SE-7 (Cuadro 14 del SAD) según **DA-11**: los proyectos creados y sus cambios salen hacia
SE-7, y el avance y el contratista que reporta SE-7 entran como **EV-24** (E-04). Regla de negocio del Diseño de
Reconstrucción: *si el sistema externo no responde, la operación se confirma igual y el envío queda en cola con
reintento*. Aísla el formato de SE-7 (AD-03): el resto de la plataforma solo conoce el contrato de `comun` (AD-09).

Una imagen, tres modos (primer argumento):

| Modo | Qué hace |
|---|---|
| `salida` | Consume `q.salida.se7` (EV-18 y EV-20) con el consumidor idempotente de `comun/Mensajeria` y entrega a SE-7 con `PUT /v1/obras/{idProyecto}/versiones/{version}` |
| `entrada` | Cada 30 s pide a SE-7 `GET /v1/cambios?desde={marca}` y publica un EV-24 por cambio (AVANCE o CONTRATISTA) |
| `migrar` | Crea el rol `adaptador_se7_app` y aplica las migraciones del esquema `se7` en la base `adaptadores` (DT-19) |

Las piezas genéricas (contrato `IContratoAdaptador`, marca de avance, resiliencia, EV-23, ciclo de entrada, compuerta
de la salida) están en [`comun/Plataforma.Comun.Adaptadores`](../../comun/Plataforma.Comun.Adaptadores) para que el
adaptador de SE-6 las reutilice. Aquí solo queda lo propio de SE-7: `Traduccion.cs` (sin base, bus ni HTTP),
`ClienteSe7.cs`, `Persistencia.cs`, `Registro.cs` y `Program.cs`.

## Cómo cumple QA-01

- **Resiliencia (DT-09, Cuadro 5):** Polly v8 con `Microsoft.Extensions.Http.Resilience`. Tiempo de espera por intento
  (3 s), reintento exponencial con jitter (2 reintentos) **solo para métodos idempotentes** (GET y PUT; POST y PATCH no
  se reintentan) y cortacircuitos con `MinimumThroughput = 3`, `FailureRatio = 1.0`, `SamplingDuration = 30 s` ("3 de
  3"), abierto 15 s antes de cada prueba.
- **Idempotencia hacia SE-7 (Cuadro 14):** la URL lleva el proyecto y la versión (la del proyecto en EV-18, la del
  historial en EV-20). Repetir la misma versión no tiene efecto en SE-7.
- **Estado visible (QA-01 b, E-15):** la apertura y el cierre del circuito publican **EV-23** por la bandeja de salida.
  Las pruebas del cortacircuitos mientras SE-7 sigue caído no publican nada: el "no responde desde" conserva el instante
  de la primera apertura.
- **Nada va a la cola de mensajes muertos por la caída (QA-01 c, d):** con SE-7 caído el manejador no lanza (el
  consumidor de `comun` mandaría el mensaje a la DLQ tras 5 entregas); la **compuerta** detiene el consumidor al abrirse
  el circuito, sondea SE-7 (`GET /v1/salud`) y lo vuelve a arrancar al cerrarse. Con `Bus:Prefetch = 1`, solo el mensaje
  en curso vuelve a la cola, una vez por caída; los demás nunca se toman y quedan listos en `q.salida.se7`, donde KEDA
  los cuenta. Un 4xx de SE-7 sí es permanente y va a la DLQ.
- **Eco:** un EV-20 con procedencia SE-7 (el avance o contratista que SE-7 reportó y Reconstrucción registró) no se
  reenvía a SE-7.
- **Marca de avance (AD-14):** se guarda en `se7.marcas_avance`. Cada página de cambios y su avance de la marca se
  confirman en la misma transacción que los EV-24 en la bandeja: un reinicio a mitad repite solo lo no confirmado, y
  dos réplicas no publican nada dos veces (bloqueo de la fila de la marca). La clave de cada EV-24 sale del cambio de
  SE-7 (`se7:<clase>:<secuencia>`). Al cerrarse el circuito, la entrada pide enseguida el rango pendiente.

## Configuración (variables de entorno; no tiene `appsettings.json`)

| Variable | Uso |
|---|---|
| `ConnectionStrings__AdaptadorSe7` | Base `adaptadores` con el rol `adaptador_se7_app` (la ruta de búsqueda se fija en `se7`) |
| `Bus__Nodos__0..n`, `Bus__Clave` | Bus. El usuario y el productor son `adaptador-se7`; la salida lee `q.salida.se7` |
| `Se7__Url` | API de SE-7 (por omisión `http://sim-se7:8080/`); `Se7__TamanoPagina` (100) |
| `Resiliencia__*` | `TiempoEsperaIntentoSegundos` (3), `TiempoEsperaTotalSegundos` (20), `Reintentos` (2), `EsperaBaseReintentoSegundos` (0.5), `MinimoLlamadas` (3), `ProporcionFallos` (1.0), `VentanaSegundos` (30), `DuracionAperturaSegundos` (15) |
| `Salida__IntervaloSondeoSegundos` | Sondeo de SE-7 con el consumo en pausa (5) |
| `Entrada__IntervaloSegundos` | Consulta periódica (30); `Entrada__MaximoPaginasPorRonda` (20) |
| `ConnectionStrings__AdaptadorSe7Dueno`, `Migracion__ClaveAdaptadorSe7App` | Solo para `migrar` |

## Despliegue

- **Compose:** `sim-se7`, `adaptador-se7-migrador`, `adaptador-se7-salida` (2 réplicas) y `adaptador-se7-entrada`
  (1 réplica), **siempre encendidos** (en compose no hay KEDA). Caída de SE-7 para QA-01:
  `docker compose -f despliegue/compose/docker-compose.yml exec sim-se7 curl -s -X POST localhost:8080/admin/caida -H 'Content-Type: application/json' -d '{"modo":"on"}'`
  (`on`, `off` o `lento`).
- **Kubernetes:** `despliegue/k8s/base/adaptador-se7.yaml`. KEDA (DT-10, instalado por `aplicar.sh`) escala la salida
  de 0 a 4 por los mensajes listos de `q.salida.se7` y enciende la entrada un minuto de cada dos (trigger `cron`).

## Probar

```bash
bash herramientas/dotnet.sh test adaptadores/adaptador-se7/tests/AdaptadorSe7.Pruebas      # sin Docker
bash herramientas/dotnet.sh test adaptadores/adaptador-se7/tests/AdaptadorSe7.Integracion  # PostgreSQL, RabbitMQ y el simulador
```
