#!/usr/bin/env bash
# Prueba de humo del adaptador de SE-8 en Jakarta EE (AD-66, QA-08, QA-09) contra la plataforma en marcha
# (docker compose up). Demuestra:
#   1. Una alerta llega a SE-8 y queda ENTREGADA solo cuando vuelve el acuse (sin acuse queda ENVIADA y se reenvía).
#   2. Un mensaje duplicado no genera un segundo envío.
#   3. Con SE-8 caído el circuito se abre, se publica EV-23 (Consulta lo muestra) y no se pierde ningún mensaje.
#   4. Al volver SE-8, la cola queda en 0 y todo se entrega una sola vez.
# Reporta los tiempos medidos. Uso: bash pruebas/humo/prueba-se8.sh [http://localhost:8080]
set -u
G="${1:-http://localhost:8080}/v1"
RAIZ="$(cd "$(dirname "$0")/../.." && pwd)"
DC=(docker compose -f "$RAIZ/despliegue/compose/docker-compose.yml")
N="${N_ALERTAS:-20}"
PY=""
for candidato in python3 python py; do
  if command -v "$candidato" >/dev/null 2>&1 && "$candidato" -c "import json" >/dev/null 2>&1; then PY="$candidato"; break; fi
done
[ -n "$PY" ] || { echo "Se necesita Python 3 para leer las respuestas JSON."; exit 2; }
KEYCLOAK_URL="${KEYCLOAK_URL:-http://localhost:8180}"
RABBIT="${RABBIT_URL:-http://localhost:15672}"
SALIDA="${TMPDIR:-/tmp}/humo-se8.json"
fallas=0

token() {
  curl -s -X POST "$KEYCLOAK_URL/realms/plataforma/protocol/openid-connect/token" -d grant_type=password -d client_id=movil \
    -d "username=$1" -d "password=${1}_local" | "$PY" -c "import json,sys;print(json.load(sys.stdin)['access_token'])" | tr -d '\r'
}
TOKEN=$(token ungrd.operador) || { echo "No se pudo obtener el token de ungrd.operador en $KEYCLOAK_URL."; exit 2; }
AUT=(-H "Authorization: Bearer $TOKEN")
J=(-H "Content-Type: application/json")

ahora() { "$PY" -c "import time;print(time.time())"; }
dif() { "$PY" -c "import sys;print(f'{float(sys.argv[2])-float(sys.argv[1]):.1f}')" "$1" "$2"; }
ok() { echo "  ok   $1"; }
falla() { echo "  FALLA $1"; fallas=$((fallas+1)); }
# Adaptador y simulador: solo en la red interna del compose; se consultan desde dentro de sus contenedores.
adaptador() { "${DC[@]}" exec -T adaptador-se8 curl -s "localhost:8080$1"; }
sim() { "${DC[@]}" exec -T sim-se8 curl -s "$@"; }
caida() { sim -X POST localhost:8080/admin/caida -H 'Content-Type: application/json' -d "{\"modo\":\"$1\"}" >/dev/null; }
py() { "$PY" -c "import json,sys;d=json.load(sys.stdin);$1"; }
# Mensajes de una cola, contados por el broker (listos y sin confirmar).
cola() { "${DC[@]}" exec -T bus1 rabbitmqctl -q list_queues name messages | awk -v c="$1" '$1==c {print $2}' | tr -d '\r'; }
hasta() { # descripción, segundos, comando que debe terminar en 0
  local descripcion="$1" maximo="$2"; shift 2
  local fin=$(( $(date +%s) + maximo ))
  while [ "$(date +%s)" -le "$fin" ]; do
    if "$@" >/dev/null 2>&1; then return 0; fi
    sleep 0.5
  done
  falla "$descripcion (más de $maximo s)"; return 1
}
estado_entrega() { adaptador "/v1/entregas?desastre=$1" | py "e=[x for x in d['entregas'] if x['tipoOrigen']=='$2'];print(e[0]['estado'] if e else '')" 2>/dev/null | tr -d '\r'; }
es_estado() { [ "$(estado_entrega "$1" "$2")" = "$3" ]; }
# Sobre EV-03 válido contra el contrato, para publicar directo en q.salida.se8 (cola de SE-8 por el exchange por
# omisión: no llega a Consulta, Reconstrucción ni Evaluación, así que no deja desastres de prueba en sus modelos).
sobre_ev03() {
  "$PY" - "$1" <<'PY'
import json, sys, uuid, datetime
clave = sys.argv[1]
ahora = datetime.datetime.now(datetime.timezone.utc).isoformat()
print(json.dumps({"idEvento": str(uuid.uuid4()), "tipo": "EV-03", "nombre": "desastre.creado", "version": 1,
  "instanteEmision": ahora, "productor": "gestion-desastres", "secuenciaProductor": 1, "idCorrelacion": None,
  "claveIdempotencia": clave, "procedencia": {"fuente": "plataforma", "sistemaOrigen": None, "confianza": "ALTA"},
  "datos": {"idDesastre": str(uuid.uuid4()), "tipo": "INUNDACION", "severidad": "NIVEL_2", "zonaVersion": 1,
    "estado": "REGISTRADO", "organismoRegistra": "ungrd", "responsable": "ungrd", "origen": "MANUAL",
    "nombre": "Humo SE-8", "descripcion": "Prueba de humo del adaptador de SE-8", "instanteInicio": ahora,
    "ubicacion": {"lat": 4.6, "lon": -74.08}, "versionDesastre": 1}}))
PY
}
publicar_cola() { # cuerpo JSON del sobre
  local cuerpo; cuerpo=$("$PY" -c "import json,sys;print(json.dumps({'properties':{'delivery_mode':2,'content_type':'application/json'},'routing_key':'q.salida.se8','payload':sys.stdin.read(),'payload_encoding':'string'}))" <<< "$1")
  curl -s -u admin:admin_local -H 'Content-Type: application/json' -d "$cuerpo" "$RABBIT/api/exchanges/%2F/amq.default/publish" | grep -q '"routed":true'
}
alertas_unicas() { sim localhost:8080/admin/estado | py "print(d['alertasUnicas'])" | tr -d '\r'; }
recepciones() { sim localhost:8080/admin/estado | py "print(d['recepciones'])" | tr -d '\r'; }
integracion() { adaptador /v1/integracion | py "print(d$1)" | tr -d '\r'; }
consulta_se8() { curl -s "${AUT[@]}" "$G/integraciones" | py "s=[x for x in d if x.get('sistema')=='SE-8'];print(s[0]['estado'] if s else '')" 2>/dev/null | tr -d '\r'; }

# Condiciones que hasta() evalúa en cada vuelta. Pasarlas como test con una sustitución las expandiría una sola vez.
cola_es() { [ "$(cola "$1")" = "$2" ]; }
ev23_mayor_que() { [ "$(integracion "['ev23Publicados']")" -gt "$1" ]; }
consulta_es() { [ "$(consulta_se8)" = "$1" ]; }

echo "0. Preparación"
sim -X POST localhost:8080/admin/reiniciar >/dev/null
caida off
adaptador /salud/listo | grep -q Healthy && ok "adaptador de SE-8 listo (WildFly)" || falla "adaptador de SE-8 no está listo"
[ "$(cola dlq.salida.se8)" = "0" ] && ok "dlq.salida.se8 vacía" || echo "  aviso dlq.salida.se8 trae $(cola dlq.salida.se8) mensajes de antes"
TIPO=$(curl -s "${AUT[@]}" "$G/catalogos/tipos-desastre" | py "print(d[0]['codigo'] if isinstance(d,list) else d['datos'][0]['codigo'])" | tr -d '\r')
ZONA='{"geometria":{"type":"Polygon","coordinates":[[[-74.10,4.60],[-74.08,4.60],[-74.08,4.62],[-74.10,4.62],[-74.10,4.60]]]},"fuente":"DECISION_AUTORIDAD"}'
registrar() {
  curl -s -o "$SALIDA" -w "%{http_code}" "${AUT[@]}" "${J[@]}" -d "{\"tipo\":\"$TIPO\",\"nombre\":\"$1\",\"descripcion\":\"Prueba de humo de SE-8\",\"instanteInicio\":\"2026-11-19T08:00:00Z\",\"lat\":4.61,\"lon\":-74.09,\"severidad\":\"NIVEL_1\",\"responsable\":\"ungrd\",\"participantes\":[],\"zona\":$ZONA}" "$G/desastres"
}

echo "1. Una alerta queda ENTREGADA solo cuando vuelve el acuse"
caida sin-acuse
[ "$(registrar "Humo SE-8 sin acuse")" = "201" ] && ok "desastre registrado por el gateway (EV-03 de Gestión de Desastres)" || falla "registro del desastre"
D1=$(py "print(d['id'])" < "$SALIDA" | tr -d '\r')
hasta "la alerta llega a SE-8 (ENVIADA)" 20 es_estado "$D1" EV-03 ENVIADA && ok "la alerta llegó a SE-8 y quedó ENVIADA"
sleep 7
[ "$(estado_entrega "$D1" EV-03)" = "ENVIADA" ] && ok "7 s sin acuse: sigue ENVIADA, nunca ENTREGADA (QA-08)" || falla "sin acuse cambió a $(estado_entrega "$D1" EV-03)"
ENVIOS=$(adaptador "/v1/entregas?desastre=$D1" | py "print(d['entregas'][0]['envios'])" | tr -d '\r')
[ "${ENVIOS:-0}" -ge 2 ] && ok "sin acuse en 5 s se reenvió con la misma referencia ($ENVIOS envíos)" || falla "no hubo reenvío por falta de acuse ($ENVIOS envíos)"
T0=$(ahora); caida off
hasta "con el canal de vuelta llega el acuse" 20 es_estado "$D1" EV-03 ENTREGADA && ok "con el acuse pasó a ENTREGADA en $(dif "$T0" "$(ahora)") s"
[ "$(sim "localhost:8080/admin/estado" | py "print(sum(1 for k in d['referencias'] if '$D1' in k))" | tr -d '\r')" = "1" ] \
  && ok "SE-8 registró una sola alerta para ese desastre" || falla "SE-8 registró más de una alerta para $D1"
T0=$(ahora)
[ "$(registrar "Humo SE-8 con acuse")" = "201" ] || falla "registro del segundo desastre"
D2=$(py "print(d['id'])" < "$SALIDA" | tr -d '\r')
hasta "alerta de punta a punta" 20 es_estado "$D2" EV-03 ENTREGADA && LAT=$(dif "$T0" "$(ahora)") && ok "registro -> alerta ENTREGADA con acuse en $LAT s (referencia QA-08: p95 < 5 s)"
curl -s -o /dev/null "${AUT[@]}" -X PATCH "${J[@]}" -d '{"severidad":"NIVEL_3","justificacion":"Prueba de humo SE-8"}' "$G/desastres/$D2/severidad"
hasta "el cambio de nivel (EV-04) llega a SE-8" 20 es_estado "$D2" EV-04 ENTREGADA && ok "cambio de NIVEL_1 a NIVEL_3 (EV-04) entregado; la alerta nombra a quienes se incorporan"

echo "2. Un mensaje duplicado no genera un segundo envío"
CLAVE="humo-se8:duplicado:$(date +%s%N)"
SOBRE=$(sobre_ev03 "$CLAVE")
U0=$(alertas_unicas); R0=$(recepciones)
publicar_cola "$SOBRE" && publicar_cola "$SOBRE" && ok "el mismo EV-03 publicado dos veces en q.salida.se8" || falla "publicación del duplicado"
hasta "los dos se consumen" 20 cola_es q.salida.se8 0
sleep 2
[ $(( $(alertas_unicas) - U0 )) -eq 1 ] && [ $(( $(recepciones) - R0 )) -eq 1 ] \
  && ok "SE-8 recibió una sola llamada y registró una sola alerta (el segundo se descartó por su clave)" \
  || falla "duplicado: $(( $(alertas_unicas) - U0 )) alertas y $(( $(recepciones) - R0 )) llamadas en SE-8"

echo "3. Con SE-8 caído el circuito se abre, se publica EV-23 y no se pierde nada"
EV23_0=$(integracion "['ev23Publicados']")
CONF_0=$(integracion "['mensajesConfirmados']")
U0=$(alertas_unicas)
TC=$(ahora); caida on
for i in $(seq 1 "$N"); do publicar_cola "$(sobre_ev03 "humo-se8:caida:$TC:$i")" || falla "publicación $i"; done
abierto() { [ "$(integracion "['circuito']")" != "CERRADO" ]; }
hasta "el circuito se abre" 90 abierto && T_ABRE=$(dif "$TC" "$(ahora)") && ok "circuito ABIERTO $T_ABRE s después de la caída"
hasta "EV-23 publicado" 20 ev23_mayor_que "$EV23_0" && ok "EV-23 (ABIERTO) publicado por la bandeja de salida"
hasta "Consulta ve SE-8 ABIERTO" 30 consulta_es ABIERTO && ok "Consulta muestra SE-8 ABIERTO (EV-23 llegó a la plataforma sin cambiar Consulta)"
sleep 3
EN_COLA=$(cola q.salida.se8)
[ "$EN_COLA" = "$N" ] && ok "los $N mensajes esperan en q.salida.se8" || falla "esperaba $N en q.salida.se8 y hay $EN_COLA"
[ "$(cola dlq.salida.se8)" = "0" ] && ok "dlq.salida.se8 en 0: la caída no manda nada a mensajes muertos" || falla "hay mensajes en dlq.salida.se8"

echo "4. Al volver SE-8 la cola queda en 0 y todo se entrega una sola vez"
TV=$(ahora); caida off
hasta "la cola se drena" 120 cola_es q.salida.se8 0 && T_DRENA=$(dif "$TV" "$(ahora)") && ok "q.salida.se8 en 0, $T_DRENA s después de volver SE-8"
todas_entregadas() { [ "$(adaptador "/v1/entregas?estado=ENTREGADA&limite=1000" | py "print(sum(1 for x in d['entregas'] if x['claveIdempotencia'].startswith('humo-se8:caida:$TC:')))" | tr -d '\r')" = "$N" ]; }
hasta "todas ENTREGADAS" 60 todas_entregadas && T_ENTREGA=$(dif "$TV" "$(ahora)") && ok "las $N alertas ENTREGADAS con acuse, $T_ENTREGA s después de volver SE-8"
UNICAS=$(( $(alertas_unicas) - U0 ))
DUPL=$(sim localhost:8080/admin/estado | py "print(sum(1 for k,v in d['referencias'].items() if 'humo-se8:caida:$TC:' in k and v>1))" | tr -d '\r')
[ "$UNICAS" = "$N" ] && [ "$DUPL" = "0" ] && ok "SE-8 registró $N alertas, cada una una sola vez" || falla "SE-8: $UNICAS alertas, $DUPL repetidas"
hasta "Consulta ve SE-8 CERRADO" 30 consulta_es CERRADO && ok "EV-23 (CERRADO) publicado; Consulta muestra SE-8 CERRADO"
[ "$(cola dlq.salida.se8)" = "0" ] && ok "dlq.salida.se8 sigue en 0" || falla "hay mensajes en dlq.salida.se8"
CONF=$(( $(integracion "['mensajesConfirmados']") - CONF_0 ))
REENC=$(integracion "['mensajesReencolados']")

echo
echo "Medidas"
echo "  registro -> ENTREGADA con acuse ........ ${LAT:-?} s"
echo "  caída -> circuito abierto .............. ${T_ABRE:-?} s"
echo "  vuelta -> q.salida.se8 en 0 ............ ${T_DRENA:-?} s ($N mensajes)"
echo "  vuelta -> las $N alertas ENTREGADAS .... ${T_ENTREGA:-?} s"
echo "  mensajes confirmados en la caída ....... $CONF (reencolados por la compuerta desde el arranque: $REENC)"
if [ "$fallas" -eq 0 ]; then echo "Prueba de SE-8: todo bien."; else echo "Prueba de SE-8: $fallas fallas."; exit 1; fi
