#!/usr/bin/env bash
# Escenario E de punta a punta contra la plataforma en marcha (docker compose up): el desastre nace en Gestión de
# Desastres, la evaluación entra por la sincronización de Evaluación de Daños y se valida, y Reconstrucción la recibe
# por eventos (sin siembra); el proyecto recorre su ciclo y, al final, el desastre se puede cerrar (E-07).
# Uso: bash pruebas/humo/prueba-humo.sh [http://localhost:8080]
# Identidad (DA-15): MODO_IDENTIDAD=encabezados (por omisión) o jwt, que pide los tokens a Keycloak. Debe coincidir con
# el IDENTIDAD_MODO con que se levantó compose. Todos los servicios, Reconstrucción incluida, usan la misma identidad.
set -u
B="${1:-http://localhost:8080}/v1"
# Intérprete de Python para leer el JSON. En Windows, "python3" puede ser el acceso directo de la tienda y no un
# intérprete: se usa el primero que de verdad ejecute.
PY=""
for candidato in python3 python py; do
  if command -v "$candidato" >/dev/null 2>&1 && "$candidato" -c "import json" >/dev/null 2>&1; then PY="$candidato"; break; fi
done
[ -n "$PY" ] || { echo "Se necesita Python 3 para leer las respuestas JSON."; exit 2; }
MODO_IDENTIDAD="${MODO_IDENTIDAD:-encabezados}"
KEYCLOAK_URL="${KEYCLOAK_URL:-http://localhost:8180}"
DISP="humo-$(date +%s)"
# Usuarios del realm "plataforma" (clave = usuario + "_local"). Las autoridades llevan ámbito y territorio DIVIPOLA
# (DA-14): Armenia 63001 y Calarcá 63130, del Quindío (63).
pedir_token() {
  local respuesta
  respuesta=$(curl -s -X POST "$KEYCLOAK_URL/realms/plataforma/protocol/openid-connect/token" \
    -d "grant_type=password" -d "client_id=movil" -d "username=$1" -d "password=${1}_local")
  "$PY" -c "import json,sys;print(json.load(sys.stdin)['access_token'])" <<< "$respuesta" 2>/dev/null | tr -d '\r'
}
case "${MODO_IDENTIDAD,,}" in
  jwt)
    declare -A TOKEN
    for usuario in alcaldia.armenia alcaldia.calarca gobernacion.quindio ing.perez auditor.1 ungrd.operador bomberos.operador evaluador.armenia; do
      TOKEN[$usuario]=$(pedir_token "$usuario")
      [ -n "${TOKEN[$usuario]}" ] || { echo "No se pudo obtener el token de $usuario en $KEYCLOAK_URL."; exit 2; }
    done
    AUT=(-H "Authorization: Bearer ${TOKEN[alcaldia.armenia]}")
    CAL=(-H "Authorization: Bearer ${TOKEN[alcaldia.calarca]}")
    REG=(-H "Authorization: Bearer ${TOKEN[gobernacion.quindio]}")
    RES=(-H "Authorization: Bearer ${TOKEN[ing.perez]}")
    AUD=(-H "Authorization: Bearer ${TOKEN[auditor.1]}")
    GD=(-H "Authorization: Bearer ${TOKEN[ungrd.operador]}")
    ORG=(-H "Authorization: Bearer ${TOKEN[bomberos.operador]}")
    # X-Dispositivo no es identidad: la app lo sigue enviando como dato del dispositivo (contrato de identidad).
    EVA=(-H "Authorization: Bearer ${TOKEN[evaluador.armenia]}" -H "X-Dispositivo: $DISP")
    ;;
  encabezados)
    AUT=(-H "X-Usuario: alcaldia.armenia" -H "X-Roles: autoridad" -H "X-Organizacion: alcaldia-armenia" -H "X-Ambito: LOCAL" -H "X-Territorio: 63001")
    CAL=(-H "X-Usuario: alcaldia.calarca" -H "X-Roles: autoridad" -H "X-Organizacion: alcaldia-calarca" -H "X-Ambito: LOCAL" -H "X-Territorio: 63130")
    REG=(-H "X-Usuario: gobernacion.quindio" -H "X-Roles: autoridad" -H "X-Organizacion: gobernacion-quindio" -H "X-Ambito: REGIONAL" -H "X-Territorio: 63")
    RES=(-H "X-Usuario: ing.perez" -H "X-Roles: responsable-proyecto" -H "X-Organizacion: alcaldia-armenia")
    AUD=(-H "X-Usuario: auditor.1" -H "X-Roles: auditor" -H "X-Organizacion: contraloria")
    GD=(-H "X-Usuario: ungrd.operador" -H "X-Roles: autoridad" -H "X-Organizacion: ungrd" -H "X-Ambito: NACIONAL")
    ORG=(-H "X-Usuario: bomberos.operador" -H "X-Roles: organismo" -H "X-Organizacion: bomberos")
    EVA=(-H "X-Usuario: evaluador.armenia" -H "X-Roles: evaluador" -H "X-Organizacion: alcaldia-armenia" -H "X-Territorio: 63001" -H "X-Dispositivo: $DISP")
    ;;
  *)
    echo "MODO_IDENTIDAD desconocido: $MODO_IDENTIDAD (use 'encabezados' o 'jwt')."; exit 2 ;;
esac
J=(-H "Content-Type: application/json")
SALIDA="${TMPDIR:-/tmp}/humo.json"
fallas=0

esperar() { # descripción, código esperado, argumentos de curl...
  local descripcion="$1" esperado="$2"; shift 2
  # El cuerpo (-d) va por la entrada estándar: el curl de Git Bash en Windows pasa los argumentos a la
  # página de códigos ANSI y dañaría las tildes del JSON.
  local args=() cuerpo=""
  while [ $# -gt 0 ]; do
    if [ "$1" = "-d" ]; then cuerpo="$2"; args+=(--data-binary @-); shift 2; else args+=("$1"); shift; fi
  done
  local codigo; codigo=$(printf '%s' "$cuerpo" | curl -s -o "$SALIDA" -w "%{http_code}" "${args[@]}")
  if [ "$codigo" = "$esperado" ]; then echo "  ok   $descripcion ($codigo)"; else echo "  FALLA $descripcion: esperaba $esperado y llegó $codigo -> $(head -c 300 "$SALIDA")"; fallas=$((fallas+1)); fi
}
campo() { "$PY" -c "import json,sys;d=json.load(sys.stdin);print($1)" < "$SALIDA" | tr -d '\r'; }
hasta() { # descripción, condición de Python sobre d, argumentos de curl...; reintenta 30 s (los módulos se enteran por eventos)
  local descripcion="$1" condicion="$2"; shift 2
  for _ in $(seq 1 60); do
    curl -s -o "$SALIDA" "$@"
    if "$PY" -c "import json,sys;d=json.load(sys.stdin);sys.exit(0 if ($condicion) else 1)" < "$SALIDA" 2>/dev/null; then echo "  ok   $descripcion"; return 0; fi
    sleep 0.5
  done
  echo "  FALLA $descripcion -> $(head -c 300 "$SALIDA")"; fallas=$((fallas+1)); return 1
}
evaluar() { # id, nivel de daño, referencia, latitud: el dispositivo la sincroniza como lo hace la app
  local ev="$1" nivel="$2" referencia="$3" lat="$4" activo; activo=$("$PY" -c "import uuid;print(uuid.uuid4())" | tr -d '\r')
  local instante; instante=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  esperar "sincronizar la evaluación $referencia" 200 "${EVA[@]}" "${J[@]}" -d "{\"idDispositivo\":\"$DISP\",\"instanteEnvioDispositivo\":\"$instante\",\"operaciones\":[{\"idOperacion\":\"$("$PY" -c "import uuid;print(uuid.uuid4())" | tr -d '\r')\",\"tipo\":\"CREAR\",\"evaluacion\":{\"id\":\"$ev\",\"activoId\":\"$activo\",\"activoNuevo\":{\"id\":\"$activo\",\"tipoCodigo\":\"escuela\",\"lat\":$lat,\"lon\":$LON,\"referencia\":\"$referencia\"},\"desastreId\":\"$D\",\"instanteCampo\":\"$instante\",\"evaluador\":\"evaluador.armenia\",\"organizacion\":\"alcaldia-armenia\",\"lat\":$lat,\"lon\":$LON,\"precisionM\":5,\"tipoActivoCodigo\":\"escuela\",\"nivelDano\":\"$nivel\",\"descripcion\":\"Prueba de humo\",\"evidencias\":[]}}]}" "$B/sincronizacion/evaluaciones"
  hasta "la validación de $referencia la acepta un organismo participante" "d.get('estado')=='VALIDADA'" "${ORG[@]}" "${J[@]}" -X POST --data-binary '{"estado":"VALIDADA","motivo":"Verificada en campo"}' "$B/evaluaciones/$ev/validacion"
}

echo "Identidad: $MODO_IDENTIDAD"
echo "0. El desastre y las evaluaciones llegan por eventos (escenario E: DA-01, sin siembra)"
esperar "sin identidad se rechaza" 401 "$B/reconstruccion/desastres"
esperar "Gestión de Desastres registra el desastre" 201 "${GD[@]}" "${J[@]}" -d '{"tipo":"TERREMOTO","nombre":"Sismo de prueba de humo","descripcion":"Escenario E","instanteInicio":"2026-11-16T08:00:00Z","lat":4.5339,"lon":-75.6811,"severidad":"NIVEL_3","responsable":"ungrd","participantes":[{"organismo":"bomberos","responsabilidad":"Evaluación de daños"}],"zona":{"geometria":{"type":"Polygon","coordinates":[[[-75.72,4.50],[-75.64,4.50],[-75.64,4.58],[-75.72,4.58],[-75.72,4.50]]]},"fuente":"DECISION_AUTORIDAD"}}' "$B/desastres"
D=$(campo "d['id']")
echo "       desastre $D"
hasta "Evaluación de Daños lo ofrece en la descarga (EV-03)" "any(x['id']=='$D' for x in d['desastres'])" "${EVA[@]}" "$B/descarga?desastreId=$D"
EV=$("$PY" -c "import uuid;print(uuid.uuid4())" | tr -d '\r'); EVL=$("$PY" -c "import uuid;print(uuid.uuid4())" | tr -d '\r')
# Puntos distintos en cada corrida, dentro de Armenia y a ~1 km entre sí: dos escuelas a pocos metros serían un posible
# duplicado (AD-33), también con las de corridas anteriores.
LAT=$("$PY" -c "import random;print(round(4.515+random.random()*0.02,6))" | tr -d '
')
LON=$("$PY" -c "import random;print(round(-75.70+random.random()*0.03,6))" | tr -d '
')
LAT2=$("$PY" -c "print(round($LAT+0.009,6))" | tr -d '
')
evaluar "$EV" GRAVE "Escuela La Esperanza" "$LAT"
evaluar "$EVL" LEVE "Escuela El Caimo" "$LAT2"
for estado in EN_ATENCION EN_RECUPERACION EN_RECONSTRUCCION; do
  esperar "el desastre pasa a $estado" 200 "${GD[@]}" "${J[@]}" -d "{\"estado\":\"$estado\"}" "$B/desastres/$D/transiciones"
done
hasta "Reconstrucción lo ve en reconstrucción (EV-03 y EV-06)" "any(x['id']=='$D' and x['estado']=='EN_RECONSTRUCCION' for x in d)" "${AUT[@]}" "$B/reconstruccion/desastres"

CLAVE="humo-$(date +%s)"
echo "1. Creación por lote"
hasta "la evaluación grave validada es candidata y la leve no (EV-08, regla de creación)" "{x['idEvaluacion'] for x in d}=={'$EV'}" "${AUT[@]}" "$B/reconstruccion/candidatas?desastre=$D"
esperar "lote con una evaluación de daño leve se rechaza completo" 422 "${AUT[@]}" "${J[@]}" -d "{\"idDesastre\":\"$D\",\"idsEvaluaciones\":[\"$EV\",\"$EVL\"],\"claveIdempotencia\":\"$CLAVE-x\"}" "$B/proyectos:lote"
esperar "crear lote" 201 "${AUT[@]}" "${J[@]}" -d "{\"idDesastre\":\"$D\",\"idsEvaluaciones\":[\"$EV\"],\"claveIdempotencia\":\"$CLAVE\"}" "$B/proyectos:lote"
P=$("$PY" -c "import json,sys;print(json.load(sys.stdin)['idsProyectos'][0])" < $SALIDA | tr -d '\r')
esperar "reenviar el mismo lote no duplica" 201 "${AUT[@]}" "${J[@]}" -d "{\"idDesastre\":\"$D\",\"idsEvaluaciones\":[\"$EV\"],\"claveIdempotencia\":\"$CLAVE\"}" "$B/proyectos:lote"
"$PY" -c "import json,sys;d=json.load(sys.stdin);sys.exit(0 if d['reenvio'] and d['idsProyectos'][0]=='$P' else 1)" < $SALIDA && echo "  ok   el reenvío devolvió el mismo proyecto" || { echo "  FALLA el reenvío no devolvió el resultado original"; fallas=$((fallas+1)); }

echo "2. Asignar y aprobar"
esperar "aprobar con datos incompletos" 422 "${AUT[@]}" "${J[@]}" -d '{"accion":"APROBAR"}' "$B/proyectos/$P/transiciones"
esperar "asignar presupuesto" 200 "${AUT[@]}" "${J[@]}" -X PATCH -d '{"presupuesto":500000000,"fuenteFinanciacion":"Fondo de reconstrucción","inicio":"2027-01-15","fin":"2027-09-30","justificacion":"Asignación inicial"}' "$B/proyectos/$P/presupuesto"
esperar "asignar responsable" 200 "${AUT[@]}" "${J[@]}" -X PATCH -d '{"responsable":"ing.perez","justificacion":"Asignación inicial"}' "$B/proyectos/$P/responsable"
esperar "la autoridad regional no aprueba un proyecto municipal" 403 "${REG[@]}" "${J[@]}" -d '{"accion":"APROBAR"}' "$B/proyectos/$P/transiciones"
# Calarcá ni siquiera ve el proyecto de Armenia: responde como si no existiera (404, E-39). La regional sí lo ve (403).
esperar "la autoridad local de otro municipio (Calarcá) no lo ve para aprobarlo (E-39)" 404 "${CAL[@]}" "${J[@]}" -d '{"accion":"APROBAR"}' "$B/proyectos/$P/transiciones"
esperar "aprobar" 200 "${AUT[@]}" "${J[@]}" -d '{"accion":"APROBAR"}' "$B/proyectos/$P/transiciones"
echo "   Jurisdicción de Calarcá sobre el proyecto de Armenia (Cuadro 3 del SRS, E-39)"
hasta "Calarcá no lo ve en la lista de proyectos" "not any(x['id']=='$P' for x in d)" "${CAL[@]}" "$B/proyectos?desastre=$D"
esperar "Calarcá no ve el detalle (404)" 404 "${CAL[@]}" "$B/proyectos/$P"
esperar "Calarcá no ve el historial (404)" 404 "${CAL[@]}" "$B/proyectos/$P/historial"
esperar "Calarcá no crea un lote con una evaluación de Armenia (403)" 403 "${CAL[@]}" "${J[@]}" -d "{\"idDesastre\":\"$D\",\"idsEvaluaciones\":[\"$EV\"],\"claveIdempotencia\":\"$CLAVE-calarca\"}" "$B/proyectos:lote"
esperar "Calarcá no lo cancela (404)" 404 "${CAL[@]}" "${J[@]}" -d '{"accion":"CANCELAR","justificacion":"prueba"}' "$B/proyectos/$P/transiciones"

echo "3. Ejecutar"
esperar "finalizar sin haber iniciado" 409 "${RES[@]}" "${J[@]}" -d '{"accion":"FINALIZAR"}' "$B/proyectos/$P/transiciones"
esperar "iniciar ejecución" 200 "${RES[@]}" "${J[@]}" -d '{"accion":"INICIAR","contratista":"Constructora Andina"}' "$B/proyectos/$P/transiciones"
esperar "avance y costo (queda en sobrecosto)" 200 "${RES[@]}" "${J[@]}" -d '{"avance":100,"costo":520000000,"concepto":"Obra completa"}' "$B/proyectos/$P/avance"
echo "Acta de entrega" > /tmp/humo-acta.txt
esperar "adjuntar documento (MinIO)" 201 "${RES[@]}" -F "archivo=@/tmp/humo-acta.txt" "$B/proyectos/$P/documentos"
esperar "finalizar" 200 "${RES[@]}" "${J[@]}" -d '{"accion":"FINALIZAR"}' "$B/proyectos/$P/transiciones"
esperar "un proyecto finalizado no admite cambios" 409 "${AUT[@]}" "${J[@]}" -d '{"accion":"CANCELAR","justificacion":"prueba"}' "$B/proyectos/$P/transiciones"

echo "4. Auditoría y consolidado"
esperar "el responsable no ve el historial" 403 "${RES[@]}" "$B/proyectos/$P/historial"
esperar "historial" 200 "${AUD[@]}" "$B/proyectos/$P/historial"
"$PY" -c "import json,sys;d=json.load(sys.stdin);print('       entradas de historial:',len(d['entradas']))" < $SALIDA
sleep 5   # el relevo publica los eventos y la consulta actualiza su modelo de lectura
esperar "consolidado (servicio de consulta)" 200 "${AUD[@]}" "$B/programa/consolidado?desastre=$D"
"$PY" -c "import json,sys;d=json.load(sys.stdin);print('       finalizados:',d['proyectosPorEstado']['Finalizado'],' en sobrecosto:',len(d['proyectosEnSobrecosto']),' dato al:',d['datoAl']); sys.exit(0 if d['datoAl'] else 1)" < $SALIDA
# Jurisdicción en el consolidado (DA-14, Cuadro 3 del SRS): la alcaldía de Armenia cuenta su proyecto; la de Calarcá, no.
hasta "la autoridad de Armenia ve su proyecto en el consolidado" "d['totalProyectos']==1" "${AUT[@]}" "$B/programa/consolidado?desastre=$D"
hasta "la autoridad de Calarcá no lo cuenta (otro municipio)" "d['totalProyectos']==0" "${CAL[@]}" "$B/programa/consolidado?desastre=$D"

echo "5. Cierre del desastre (E-07): Gestión de Desastres sabe por EV-18 y EV-20 que el proyecto terminó"
cerrar() { printf '%s' '{"estado":"CERRADO","justificacion":"Reconstrucción terminada"}' | curl -s -o "$SALIDA" -w "%{http_code}" "${GD[@]}" "${J[@]}" --data-binary @- "$B/desastres/$D/transiciones"; }
for _ in $(seq 1 60); do codigo=$(cerrar); [ "$codigo" = 200 ] && break; sleep 0.5; done
[ "$codigo" = 200 ] && echo "  ok   el desastre se cierra cuando su único proyecto está finalizado (200)" || { echo "  FALLA cierre del desastre: $codigo -> $(head -c 300 "$SALIDA")"; fallas=$((fallas+1)); }

echo; [ "$fallas" = 0 ] && echo "Todo pasó." || echo "Fallaron $fallas verificaciones."
exit $fallas
