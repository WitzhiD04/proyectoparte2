# Encargo · Notificación mínima con SE-8

> **Fuera de esta entrega (2026-11-17).** Notificación es de Gestión de Desastres. No se implementa; queda declarada como límite del prototipo.

Lea primero `D:\Proyecto arqui\encargos\convenciones.md` completo: manda sobre este encargo.

**Base:** trabaje sobre la rama principal actual: ya trae el contrato de este encargo (EV-07, EV-28, EV-29 y la
topología) y `comun/Plataforma.Comun.Adaptadores`, con el adaptador de SE-7 como ejemplo.

## Qué es

RF-GD-06 exige que toda reclasificación de un desastre produzca una notificación a los organismos suscritos al **nuevo
ámbito de coordinación** (SRS §4.3, RN-3, Cuadro 18), y RF-GD-15 que se notifique al crear un evento o cambiar su nivel,
registrando el estado de entrega de cada notificación (pendiente, entregada o fallida). Decisiones del SAD: **AD-13**
(microservicio con modelo de suscripciones), AD-09, AD-25 (el adaptador de SE-8 mantiene **una instancia encendida**
para QA-08). Escenario **QA-08** (léalo completo en `docs/SRS.pdf`, Cuadro 31: p95 < 5 s y p99 < 10 s desde la
confirmación hasta el acuse; si un canal no acusa en 5 s se reintenta y queda pendiente; solo pasa a entregada con acuse;
100 % consultable por destinatario). CU-1 del SAD (pasos 4 y 4a) y la vista de componentes ("cola de salida de SE-8",
"acuse de entrega"). Enmienda **E-37** en `ENMIENDAS_DOCUMENTOS.md`: el contrato ya está en `comun/` (no lo cambie).

## Alcance

1. **`servicios/notificacion`** (capas de Reconstrucción), base `notificacion`, rol `notificacion_app`, modo `migrar`.
   Usuario de bus `notificacion` (clave `notificacion_local`), cola `q.notificacion` (EV-03, EV-04, EV-29).
   - **Suscripciones** (Cuadro 15: `GET`, `POST /v1/suscripciones`, `DELETE /v1/suscripciones/{id}`): organismo, canal
     (CORREO o SMS), destino, los niveles de severidad cuyo ámbito de coordinación lo incluye (Cuadro 18: por ejemplo, la
     autoridad local en NIVEL_1 a NIVEL_4; los organismos en NIVEL_2 a NIVEL_4; la autoridad regional o nacional en
     NIVEL_3 y NIVEL_4; asistencia internacional en NIVEL_4) y, opcional, los tipos de desastre. Escribe el rol
     `administrador`; lee cualquier usuario autenticado. Siembra de desarrollo con una suscripción por ámbito.
     **Incorporar un organismo es agregar una suscripción** (AD-13, QA-10): ningún otro servicio cambia.
   - **Resolución:** con EV-03 (creación) y EV-04 (reclasificación), los suscritos al ámbito del nivel nuevo. Un ascenso
     de NIVEL_1 a NIVEL_3 incorpora destinatarios nuevos (SRS §4.3). Una notificación por destinatario y canal, en estado
     PENDIENTE, con su evento de origen.
   - **Entrega:** por cada intento publica EV-28 por la bandeja (misma transacción que la notificación). Con EV-29
     ENTREGADA pasa a ENTREGADA y publica **EV-07**. Si no hay acuse en 5 s (o llega FALLIDA), reintenta con un EV-28
     nuevo (intento n+1) y sigue PENDIENTE; al agotar los intentos configurables queda FALLIDA y publica EV-07. Con dos
     réplicas, el proceso de reintentos no debe duplicar intentos (bloqueo, como el relevo de la bandeja o la revisión
     de huecos de Auditoría).
   - **Consulta:** `GET /v1/notificaciones/{id}` (estado e intentos, Cuadro 15) y `GET /v1/notificaciones?desastre=`
     (estado por destinatario, QA-08). Toda respuesta con `datoAl`.
2. **`adaptadores/adaptador-se8`**: consume `q.salida.se8` (usuario `adaptador-se8`, clave `adaptador_se8_local`),
   entrega a SE-8 con la resiliencia de `comun/Plataforma.Comun.Adaptadores` (timeout de 5 s por intento,
   cortacircuitos con EV-23 para `SE-8`), con clave de idempotencia `idNotificacion + intento` hacia SE-8, y publica
   **EV-29** con el acuse (ENTREGADA) o el rechazo explícito de SE-8 (FALLIDA). Ante un tiempo agotado o un error
   técnico no publica nada: el reintento lo decide Notificación (es la dueña del estado, AD-13). Base propia solo si la
   necesita su bandeja (esquema `se8` en la base `adaptadores`).
3. **`simuladores/sim-se8`**: recibe mensajes (idempotente por clave), acusa, permite consultar lo recibido y
   `POST /admin/caida` (`on`, `off`, `lento`) como `sim-se7`.
4. **Despliegue:** compose (`notificacion-migrador`, `notificacion` ×2, `adaptador-se8`, `sim-se8`), k8s
   (`despliegue/k8s/base/notificacion.yaml` y `adaptador-se8.yaml`; KEDA para el adaptador con **mínimo 1 réplica**,
   AD-25), entradas en `kustomization.yaml`, `00-base.yaml`, `.env.ejemplo` y `aplicar.sh`. No toque `consola/`.

## Criterio de salida
- Unitarias: resolución por ámbito (N1→N3 incorpora destinatarios), máquina de estados de la entrega, reintento por
  tiempo agotado, arquitectura.
- Integración (Testcontainers + simulador en memoria): una reclasificación produce las notificaciones del ámbito nuevo,
  EV-28 y EV-07 cumplen el contrato; con SE-8 caído quedan PENDIENTE y se reintentan; al volver pasan a ENTREGADA sin
  duplicados en SE-8; al agotar intentos, FALLIDA; dos réplicas no duplican intentos. Mida la latencia de punta a punta
  en la prueba y repórtela (referencia para QA-08).
- `bash herramientas/dotnet.sh test Plataforma.sln` en verde (todo).
- Reporte final con el formato de `convenciones.md`.
