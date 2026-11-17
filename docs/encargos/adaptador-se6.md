# Encargo · Adaptador de SE-6 (sistema financiero) con su simulador

Lea primero `docs/encargos/convenciones.md`.

**Plantilla:** `adaptadores/adaptador-se7` y `simuladores/sim-se7`. Reutilice `comun/Plataforma.Comun.Adaptadores`: la
salida, la resiliencia con EV-23 y la compuerta del consumidor.

## Qué es

El Diseño de Reconstrucción (`docs/DiseñoReconstrucción.pdf`) dice que el presupuesto y los costos se envían al sistema
financiero externo, y que si no responde, la operación se confirma igual y queda en cola con reintento.

**Decisiones:**
- **DA-11:** solo salida, con envío **por lote** (Cuadro 14: "envío por lote") e idempotencia por
  `idProyecto + versionHistorial`.
- **E-10:** EV-19 trae el presupuesto y la fuente; EV-20 trae el costo con `costoRegistrado`, que es el monto del
  movimiento.
- Tácticas AD-05, AD-36 y AD-37. Escenario QA-01, con SE-6 en lugar de SE-3.

## Alcance

1. **Topología.** Es la única excepción a "no tocar `despliegue/bus/`". En `despliegue/bus/generar_definiciones.py`
   agregue la cola `q.salida.se6` con EV-18, EV-19 y EV-20, y el usuario `adaptador-se6`, que lee esa cola y publica
   EV-23. Regenere `definiciones.json` con `python -I generar_definiciones.py`.
2. **`adaptadores/adaptador-se6`:**
   - Consume `q.salida.se6` y **acumula en lotes**, por tamaño o por tiempo, configurables.
   - Entrega cada lote a SE-6 con la resiliencia común.
   - Con SE-6 caído, nada va a la cola de mensajes muertos: use la compuerta, igual que SE-7.
   - Publica EV-23 al abrirse y al cerrarse el circuito.
   - **Del EV-20 solo envía lo que interesa al sistema financiero:** el costo, y el estado cuando un proyecto se cancela
     o se finaliza. Ignora el avance y el contratista.
3. **`simuladores/sim-se6`:** recibe lotes de forma idempotente por elemento, permite consultar lo recibido y tiene
   `POST /admin/caida`.
4. **Despliegue:**
   - Compose.
   - K8s con KEDA: un `ScaledObject` por largo de cola, de 0 a N.
   - Las entradas en `kustomization.yaml`, `00-base.yaml`, `.env.ejemplo` y `aplicar.sh`.

## Criterio de salida

- Unitarias y de integración con Testcontainers, como las de SE-7:
  - con SE-6 caído, los movimientos quedan en la cola y se entregan todos al volver, sin duplicados en SE-6;
  - EV-23 sale al abrirse y al cerrarse el circuito;
  - el lote respeta el tamaño y el tiempo.
- `bash herramientas/dotnet.sh test Plataforma.sln` en verde.
- Reporte final con el formato de `convenciones.md`.
