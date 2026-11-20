# Simulador de SE-8 (comunicación y notificación)

"El otro sistema" para el adaptador de SE-8 en Jakarta EE (AD-66). No referencia nada de la plataforma. Tiene su propio
formato (alertas con referencia externa, medio y prioridad) y guarda su estado en memoria. No está en `Plataforma.sln`:
incorporar SE-8 no modificó ningún archivo existente (QA-09).

| Ruta | Qué hace |
|---|---|
| `PUT /v1/alertas/{referencia}` | Registra una alerta. Es idempotente por referencia: 202 si es nueva, 200 si es repetida (no hay segundo envío), 422 si es inválida |
| `GET /v1/acuses?desde=&limite=` | Acuses desde una marca (consulta paginada e idempotente) |
| `GET /v1/salud` | Sondeo del cortacircuitos (sujeto a la caída) |
| `POST /admin/caida` | `{"modo":"on"}` (503), `"lento"` (10 s), `"sin-acuse"` (recibe pero no acusa), `"off"` |
| `GET /admin/estado`, `GET /admin/alertas/{referencia}`, `POST /admin/reiniciar` | Administración |

Cada alerta nueva se acusa después de `Simulador__RetrasoAcuseMs` (300 ms). El acuse se avisa al adaptador con
`POST Simulador__UrlAcuse` (`http://adaptador-se8:8080/v1/acuses`), por la red interna, con hasta 5 intentos. Si no
llega, queda en `GET /v1/acuses` para la conciliación por marca. Un reenvío de una alerta que el canal aún no acusó
vuelve a intentar el acuse. Un destinatario que contiene "rechazar" produce un acuse `RECHAZADA`.

```bash
docker compose -f despliegue/compose/docker-compose.yml exec sim-se8 curl -s -X POST localhost:8080/admin/caida -H 'Content-Type: application/json' -d '{"modo":"on"}'
```
