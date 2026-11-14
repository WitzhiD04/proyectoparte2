#!/usr/bin/env bash
# Recorre el ciclo completo de un proyecto contra el módulo en marcha (docker compose up).
# Uso: bash scripts/prueba-humo.sh [http://localhost:8080]
set -u
B="${1:-http://localhost:8080}/v1"
D=11111111-1111-1111-1111-111111111111
AUT=(-H "X-Usuario: alcaldia.armenia" -H "X-Rol: Autoridad" -H "X-Ambito: Local")
REG=(-H "X-Usuario: gobernacion.quindio" -H "X-Rol: Autoridad" -H "X-Ambito: Regional")
RES=(-H "X-Usuario: ing.perez" -H "X-Rol: Responsable")
AUD=(-H "X-Usuario: contraloria" -H "X-Rol: Auditor")
J=(-H "Content-Type: application/json")
fallas=0

esperar() { # descripción, código esperado, argumentos de curl...
  local descripcion="$1" esperado="$2"; shift 2
  # El cuerpo (-d) va por la entrada estándar: el curl de Git Bash en Windows pasa los argumentos a la
  # página de códigos ANSI y dañaría las tildes del JSON.
  local args=() cuerpo=""
  while [ $# -gt 0 ]; do
    if [ "$1" = "-d" ]; then cuerpo="$2"; args+=(--data-binary @-); shift 2; else args+=("$1"); shift; fi
  done
  local codigo; codigo=$(printf '%s' "$cuerpo" | curl -s -o /tmp/humo.json -w "%{http_code}" "${args[@]}")
  if [ "$codigo" = "$esperado" ]; then echo "  ok   $descripcion ($codigo)"; else echo "  FALLA $descripcion: esperaba $esperado y llegó $codigo -> $(head -c 300 /tmp/humo.json)"; fallas=$((fallas+1)); fi
}

CLAVE="humo-$(date +%s)"
echo "1. Creación por lote"
esperar "sin identidad se rechaza" 401 "$B/desastres"
esperar "candidatas" 200 "${AUT[@]}" "$B/reconstruccion/candidatas?desastre=$D"
EV=$(python3 -c "import json,sys;print(json.load(sys.stdin)[0]['idEvaluacion'])" < /tmp/humo.json | tr -d '\r')
esperar "lote con una evaluación de daño leve se rechaza completo" 422 "${AUT[@]}" "${J[@]}" -d "{\"idDesastre\":\"$D\",\"idsEvaluaciones\":[\"$EV\",\"aaaaaaaa-0000-0000-0000-000000000005\"],\"claveIdempotencia\":\"$CLAVE-x\"}" "$B/proyectos/lote"
esperar "crear lote" 201 "${AUT[@]}" "${J[@]}" -d "{\"idDesastre\":\"$D\",\"idsEvaluaciones\":[\"$EV\"],\"claveIdempotencia\":\"$CLAVE\"}" "$B/proyectos/lote"
P=$(python3 -c "import json,sys;print(json.load(sys.stdin)['idsProyectos'][0])" < /tmp/humo.json | tr -d '\r')
esperar "reenviar el mismo lote no duplica" 201 "${AUT[@]}" "${J[@]}" -d "{\"idDesastre\":\"$D\",\"idsEvaluaciones\":[\"$EV\"],\"claveIdempotencia\":\"$CLAVE\"}" "$B/proyectos/lote"
python3 -c "import json,sys;d=json.load(sys.stdin);sys.exit(0 if d['reenvio'] and d['idsProyectos'][0]=='$P' else 1)" < /tmp/humo.json && echo "  ok   el reenvío devolvió el mismo proyecto" || { echo "  FALLA el reenvío no devolvió el resultado original"; fallas=$((fallas+1)); }

echo "2. Asignar y aprobar"
esperar "aprobar con datos incompletos" 422 "${AUT[@]}" "${J[@]}" -d '{"accion":"APROBAR"}' "$B/proyectos/$P/transiciones"
esperar "asignar presupuesto" 200 "${AUT[@]}" "${J[@]}" -X PATCH -d '{"presupuesto":500000000,"fuenteFinanciacion":"Fondo de reconstrucción","inicio":"2027-01-15","fin":"2027-09-30","justificacion":"Asignación inicial"}' "$B/proyectos/$P/presupuesto"
esperar "asignar responsable" 200 "${AUT[@]}" "${J[@]}" -X PATCH -d '{"responsable":"ing.perez","justificacion":"Asignación inicial"}' "$B/proyectos/$P/responsable"
esperar "la autoridad regional no aprueba un proyecto municipal" 403 "${REG[@]}" "${J[@]}" -d '{"accion":"APROBAR"}' "$B/proyectos/$P/transiciones"
esperar "aprobar" 200 "${AUT[@]}" "${J[@]}" -d '{"accion":"APROBAR"}' "$B/proyectos/$P/transiciones"

echo "3. Ejecutar"
esperar "finalizar sin haber iniciado" 409 "${RES[@]}" "${J[@]}" -d '{"accion":"FINALIZAR"}' "$B/proyectos/$P/transiciones"
esperar "iniciar ejecución" 200 "${RES[@]}" "${J[@]}" -d '{"accion":"INICIAR","contratista":"Constructora Andina"}' "$B/proyectos/$P/transiciones"
esperar "avance y costo (queda en sobrecosto)" 200 "${RES[@]}" "${J[@]}" -d '{"avance":100,"costo":520000000,"concepto":"Obra completa"}' "$B/proyectos/$P/avances"
echo "Acta de entrega" > /tmp/humo-acta.txt
esperar "adjuntar documento (MinIO)" 201 "${RES[@]}" -F "archivo=@/tmp/humo-acta.txt" "$B/proyectos/$P/documentos"
esperar "finalizar" 200 "${RES[@]}" "${J[@]}" -d '{"accion":"FINALIZAR"}' "$B/proyectos/$P/transiciones"
esperar "un proyecto finalizado no admite cambios" 409 "${AUT[@]}" "${J[@]}" -d '{"accion":"CANCELAR","justificacion":"prueba"}' "$B/proyectos/$P/transiciones"

echo "4. Auditoría y consolidado"
esperar "el responsable no ve el historial" 403 "${RES[@]}" "$B/proyectos/$P/historial"
esperar "historial" 200 "${AUD[@]}" "$B/proyectos/$P/historial"
python3 -c "import json,sys;d=json.load(sys.stdin);print('       entradas de historial:',len(d['entradas']))" < /tmp/humo.json
sleep 5   # el relevo publica los eventos y la consulta actualiza su modelo de lectura
esperar "consolidado (servicio de consulta)" 200 "${AUD[@]}" "$B/programa/consolidado?desastre=$D"
python3 -c "import json,sys;d=json.load(sys.stdin);print('       finalizados:',d['proyectosPorEstado']['Finalizado'],' en sobrecosto:',len(d['proyectosEnSobrecosto']))" < /tmp/humo.json

echo; [ "$fallas" = 0 ] && echo "Todo pasó." || echo "Fallaron $fallas verificaciones."
exit $fallas
