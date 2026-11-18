#!/usr/bin/env bash
# Recorre Gestión de Desastres y Consulta contra la plataforma en marcha (docker compose up): registro, reclasificación,
# delimitación, QA-11, ciclo de vida hasta el cierre y, del otro lado del bus, el tablero, el mapa y la evolución.
# Uso: bash pruebas/humo/prueba-desastres.sh [http://localhost:8080]
set -u
B="${1:-http://localhost:8080}/v1"
PY=""
for candidato in python3 python py; do
  if command -v "$candidato" >/dev/null 2>&1 && "$candidato" -c "import json" >/dev/null 2>&1; then PY="$candidato"; break; fi
done
[ -n "$PY" ] || { echo "Se necesita Python 3 para leer las respuestas JSON."; exit 2; }
MODO_IDENTIDAD="${MODO_IDENTIDAD:-jwt}"
KEYCLOAK_URL="${KEYCLOAK_URL:-http://localhost:8180}"

pedir_token() {
  local usuario="$1"
  local respuesta
  respuesta=$(curl -s -X POST "$KEYCLOAK_URL/realms/plataforma/protocol/openid-connect/token" \
    -d "grant_type=password" \
    -d "client_id=movil" \
    -d "username=$usuario" \
    -d "password=${usuario}_local")
  "$PY" -c "import json,sys;d=json.load(sys.stdin);sys.exit(0 if 'access_token' in d else 1);print(d['access_token'])" <<< "$respuesta" 2>/dev/null && \
    "$PY" -c "import json,sys;print(json.load(sys.stdin)['access_token'])" <<< "$respuesta" | tr -d '\r'
}

case "${MODO_IDENTIDAD,,}" in
  jwt)
    TOKEN_AUT=$(pedir_token "ungrd.operador") || { echo "No se pudo obtener token JWT para ungrd.operador en $KEYCLOAK_URL."; exit 2; }
    TOKEN_ORG=$(pedir_token "bomberos.operador") || { echo "No se pudo obtener token JWT para bomberos.operador en $KEYCLOAK_URL."; exit 2; }
    TOKEN_ADM=$(pedir_token "admin.catalogo") || { echo "No se pudo obtener token JWT para admin.catalogo en $KEYCLOAK_URL."; exit 2; }
    AUT=(-H "Authorization: Bearer $TOKEN_AUT")
    ORG=(-H "Authorization: Bearer $TOKEN_ORG")
    ADM=(-H "Authorization: Bearer $TOKEN_ADM")
    ;;
  encabezados)
    AUT=(-H "X-Usuario: ungrd.operador" -H "X-Roles: autoridad" -H "X-Organizacion: ungrd" -H "X-Ambito: NACIONAL")
    ORG=(-H "X-Usuario: bomberos.operador" -H "X-Roles: organismo" -H "X-Organizacion: bomberos")
    ADM=(-H "X-Usuario: admin.catalogo" -H "X-Roles: administrador" -H "X-Organizacion: ungrd")
    ;;
  *)
    echo "MODO_IDENTIDAD desconocido: $MODO_IDENTIDAD (use 'encabezados' o 'jwt')."
    exit 2
    ;;
esac
J=(-H "Content-Type: application/json")
SALIDA="${TMPDIR:-/tmp}/humo-desastres.json"
fallas=0

esperar() { # descripción, código esperado, argumentos de curl...
  local descripcion="$1" esperado="$2"; shift 2
  local args=() cuerpo=""
  while [ $# -gt 0 ]; do
    if [ "$1" = "-d" ]; then cuerpo="$2"; args+=(--data-binary @-); shift 2; else args+=("$1"); shift; fi
  done
  local codigo; codigo=$(printf '%s' "$cuerpo" | curl -s -o "$SALIDA" -w "%{http_code}" "${args[@]}")
  if [ "$codigo" = "$esperado" ]; then echo "  ok   $descripcion ($codigo)"; else echo "  FALLA $descripcion: esperaba $esperado y llegó $codigo -> $(head -c 300 "$SALIDA")"; fallas=$((fallas+1)); fi
}
campo() { "$PY" -c "import json,sys;d=json.load(sys.stdin);print(eval('d'+sys.argv[1]))" "$1" < "$SALIDA" | tr -d '\r'; }
hasta() { # descripción, expresión de Python sobre d, url; reintenta hasta 15 s (el modelo de lectura es eventual)
  local descripcion="$1" condicion="$2" url="$3"
  for _ in $(seq 1 30); do
    curl -s -o "$SALIDA" "${AUT[@]}" "$url"
    if "$PY" -c "import json,sys;d=json.load(sys.stdin);sys.exit(0 if ($condicion) else 1)" < "$SALIDA" 2>/dev/null; then echo "  ok   $descripcion"; return; fi
    sleep 0.5
  done
  echo "  FALLA $descripcion -> $(head -c 300 "$SALIDA")"; fallas=$((fallas+1))
}

echo "1. Catálogo (QA-10)"
esperar "tipos del catálogo" 200 "${AUT[@]}" "$B/catalogos/tipos-desastre"
TIPO="HUMO_$(date +%s)"
esperar "un organismo no crea tipos" 403 "${ORG[@]}" "${J[@]}" -d "{\"codigo\":\"$TIPO\",\"nombre\":\"Prueba\",\"perfil\":\"ESTANDAR\"}" "$B/catalogos/tipos-desastre"
esperar "el administrador crea un tipo en ejecución" 201 "${ADM[@]}" "${J[@]}" -d "{\"codigo\":\"$TIPO\",\"nombre\":\"Prueba\",\"perfil\":\"ESTANDAR\"}" "$B/catalogos/tipos-desastre"

echo "2. Registro y operaciones"
ZONA='{"geometria":{"type":"Polygon","coordinates":[[[-75.69,4.53],[-75.67,4.53],[-75.67,4.55],[-75.69,4.55],[-75.69,4.53]]]},"fuente":"DECISION_AUTORIDAD"}'
esperar "sin identidad se rechaza" 401 "$B/desastres/00000000-0000-0000-0000-000000000000/historial"
esperar "registrar desastre con el tipo nuevo" 201 "${AUT[@]}" "${J[@]}" -d "{\"tipo\":\"$TIPO\",\"nombre\":\"Humo Armenia\",\"descripcion\":\"Prueba de humo\",\"instanteInicio\":\"2026-11-16T08:00:00Z\",\"lat\":4.5339,\"lon\":-75.6811,\"severidad\":\"NIVEL_2\",\"responsable\":\"ungrd\",\"participantes\":[],\"zona\":$ZONA}" "$B/desastres"
D=$(campo "['id']")
echo "       desastre $D"
esperar "un organismo no responsable no reclasifica (QA-11)" 403 "${ORG[@]}" -X PATCH "${J[@]}" -d '{"severidad":"NIVEL_3","justificacion":"x"}' "$B/desastres/$D/severidad"
esperar "reclasificar a NIVEL_3" 200 "${AUT[@]}" -X PATCH "${J[@]}" -d '{"severidad":"NIVEL_3","justificacion":"Réplicas fuertes"}' "$B/desastres/$D/severidad"
[ "$(campo "['perfil']")" = "EXTENDIDO" ] && echo "  ok   el perfil sube a EXTENDIDO con el nivel" || { echo "  FALLA el perfil no subió: $(campo "['perfil']")"; fallas=$((fallas+1)); }
# La zona v2 crece. Va por polígono: la delimitación por municipios necesita la capa de SE-2 (EV-21) cargada.
esperar "redelimitar: la zona crece (v2)" 200 "${AUT[@]}" -X PUT "${J[@]}" -d '{"geometria":{"type":"Polygon","coordinates":[[[-75.72,4.50],[-75.64,4.50],[-75.64,4.58],[-75.72,4.58],[-75.72,4.50]]]},"fuente":"EVALUACION_CAMPO","justificacion":"Evaluación de campo"}' "$B/desastres/$D/zona"
esperar "una transición fuera del perfil se rechaza" 409 "${AUT[@]}" "${J[@]}" -d '{"estado":"EN_RECONSTRUCCION"}' "$B/desastres/$D/transiciones"
for estado in EN_EVALUACION EN_ATENCION EN_RECUPERACION EN_RECONSTRUCCION; do
  esperar "pasar a $estado" 200 "${AUT[@]}" "${J[@]}" -d "{\"estado\":\"$estado\"}" "$B/desastres/$D/transiciones"
done
esperar "cerrar sin proyectos abiertos" 200 "${AUT[@]}" "${J[@]}" -d '{"estado":"CERRADO","justificacion":"Fin de la prueba"}' "$B/desastres/$D/transiciones"
esperar "historial" 200 "${AUT[@]}" "$B/desastres/$D/historial"

echo "3. Consulta (modelo de lectura por eventos)"
hasta "el tablero lo muestra cerrado" "any(f['id']=='$D' and f['estado']=='CERRADO' for f in d['datos'])" "$B/consulta/desastres"
hasta "la vista trae la marca de cada dato" "d['severidad']['valor']=='NIVEL_3' and d['severidad']['datoAl'] is not None and d['zona']['valor']['version']==2" "$B/consulta/desastres/$D"
hasta "el mapa de todos trae la zona v2" "any(f['properties']['idDesastre']=='$D' and f['properties']['zonaVersion']==2 for f in d['features'])" "$B/consulta/mapa?soloActivos=false"
hasta "el mapa de activos ya no lo trae" "not any(f['properties']['idDesastre']=='$D' for f in d['features'])" "$B/consulta/mapa"
hasta "la evolución trae los cambios" "len(d['cambios'])>=8" "$B/consulta/desastres/$D/evolucion"
T1=$("$PY" -c "import json,sys;d=json.load(sys.stdin);print([c['instante'] for c in d['cambios'] if c['tipo']=='REGISTRO'][0])" < "$SALIDA" | tr -d '\r')
hasta "el desastre en el instante del registro: nivel 2 y zona v1 (RF-GD-09)" "d['enInstante']['severidad']=='NIVEL_2' and d['enInstante']['zona']['version']==1" "$B/consulta/desastres/$D/evolucion?en=$("$PY" -c "import urllib.parse,sys;print(urllib.parse.quote(sys.argv[1]))" "$T1" | tr -d '\r')"
esperar "integraciones" 200 "${AUT[@]}" "$B/integraciones"

if [ "$fallas" -eq 0 ]; then echo "Todo bien."; else echo "$fallas fallas."; exit 1; fi
