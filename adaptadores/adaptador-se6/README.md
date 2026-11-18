# Adaptador de SE-6 (sistema financiero)

Integra la plataforma con SE-6 (Cuadro 14 del SAD) según **DA-11** y **E-10**: los proyectos creados (EV-18), los
cambios de presupuesto y fuente (EV-19) y los costos y cierres de proyecto (EV-20) salen hacia SE-6 **por lotes**.
Regla de negocio del Diseño de Reconstrucción: *el presupuesto y los costos se envían al sistema financiero; si no
responde, la operación se confirma igual y el envío queda en cola con reintento*. Aísla el formato de SE-6 (AD-03): el
resto de la plataforma solo conoce el contrato de `comun` (AD-09).

Una imagen, dos modos (primer argumento):

| Modo | Qué hace |
|---|---|
| `salida` (o sin argumento) | Consume `q.salida.se6` (EV-18, EV-19 y EV-20), acumula en lotes por tamaño o por tiempo y entrega cada lote a SE-6 con `PUT /v1/lotes/{idLote}` |
| `migrar` | Crea el rol `adaptador_se6_app` y aplica las migraciones del esquema `se6` en la base `adaptadores` (DT-19) |

Reutiliza [`comun/Plataforma.Comun.Adaptadores`](../../comun/Plataforma.Comun.Adaptadores): el monitor del circuito,
la publicación de **EV-23** por bandeja de salida y la resiliencia HTTP con Polly v8.

## Cómo cumple QA-01 y DA-11

- **Envío por lote (Cuadro 14, DA-11):** acumula mensajes de `q.salida.se6` hasta alcanzar `Se6__TamanoLote` (10 por
  omisión) o hasta que venza `Se6__VentanaLoteSegundos` (2 s por omisión) desde el primer mensaje del lote.
- **Filtrado financiero de EV-20 (E-10):** de EV-20 solo envía a SE-6 los registros de costo (`costoRegistrado`) y las
  transiciones de estado a `FINALIZADO` o `CANCELADO`. Ignora los EV-20 que solo traen avance, contratista, responsable,
  cronograma, documentos o estados intermedios.
- **Resiliencia (DT-09, AD-05, AD-37):** Polly v8 con `Microsoft.Extensions.Http.Resilience`. Tiempo de espera por
  intento (3 s), reintento exponencial con jitter (2 reintentos) para `PUT /v1/lotes/{idLote}` y cortacircuitos "3 de 3
  en 30 s", abierto 15 s antes de cada prueba.
- **Idempotencia por elemento (Cuadro 14, DA-11):** cada movimiento lleva `(codigoProyecto, versionHistorial)`. Repetir
  un lote o un elemento en otro lote no duplica nada en SE-6.
- **Estado visible (QA-01 b, E-15):** la apertura y el cierre del circuito publican **EV-23** por la bandeja de salida.
- **Nada va a la cola de mensajes muertos por la caída (QA-01 c, d):** los mensajes del lote no se confirman (ACK) en
  RabbitMQ ni en `se6.mensajes_procesados` hasta que SE-6 acepta el lote. Al abrirse el circuito, `CompuertaLotesSe6`
  pausa el consumidor (devolviendo el lote en curso a `q.salida.se6`), sondea SE-6 (`GET /v1/salud`) y vuelve a
  consumir al cerrarse.

## Configuración (variables de entorno; no tiene `appsettings.json`)

| Variable | Uso |
|---|---|
| `ConnectionStrings__AdaptadorSe6` | Base `adaptadores` con el rol `adaptador_se6_app` (la ruta de búsqueda se fija en `se6`) |
| `Bus__Nodos__0..n`, `Bus__Clave` | Bus. El usuario y el productor son `adaptador-se6`; lee `q.salida.se6` |
| `Se6__Url` | API de SE-6 (por omisión `http://sim-se6:8080/`) |
| `Se6__TamanoLote` | Tamaño máximo de cada lote (por omisión `10`) |
| `Se6__VentanaLoteSegundos` | Ventana máxima de acumulación en segundos desde el primer mensaje del lote (por omisión `2`) |
| `Resiliencia__*` | `TiempoEsperaIntentoSegundos` (3), `TiempoEsperaTotalSegundos` (20), `Reintentos` (2), `EsperaBaseReintentoSegundos` (0.5), `MinimoLlamadas` (3), `ProporcionFallos` (1.0), `VentanaSegundos` (30), `DuracionAperturaSegundos` (15) |
| `Salida__IntervaloSondeoSegundos` | Sondeo de SE-6 con el consumo en pausa (5) |
| `ConnectionStrings__AdaptadorSe6Dueno`, `Migracion__ClaveAdaptadorSe6App` | Solo para `migrar` |

## Probar

```bash
bash herramientas/dotnet.sh test adaptadores/adaptador-se6/tests/AdaptadorSe6.Pruebas      # sin Docker
bash herramientas/dotnet.sh test adaptadores/adaptador-se6/tests/AdaptadorSe6.Integracion  # PostgreSQL, RabbitMQ y el simulador
```
