#!/usr/bin/env bash
# El gateway vuelve a resolver los nombres de los servicios (VidaConexion de SocketsHttpHandler): se reconstruye un
# servicio con compose (contenedores nuevos, IP nuevas) y se comprueba que el gateway lo sigue encontrando SIN
# recrearse. Con nginx esto fallaba (A1b: 404 RUTA_DE_OTRO_SERVICIO hasta recrear web).
# Uso, con la plataforma arriba: bash pruebas/humo/prueba-resolucion-gateway.sh [servicio] [ruta]
#   por omisión: consulta y /v1/consulta/desastres
set -u
SERVICIO="${1:-consulta}"
RUTA="${2:-/v1/consulta/desastres}"
B="http://localhost:8080"
KEYCLOAK_URL="${KEYCLOAK_URL:-http://localhost:8180}"
raiz="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
compose() { docker compose -f "$raiz/despliegue/compose/docker-compose.yml" -p plataforma "$@"; }
PY=""
for candidato in python3 python py; do
  if command -v "$candidato" >/dev/null 2>&1 && "$candidato" -c "import json" >/dev/null 2>&1; then PY="$candidato"; break; fi
done
[ -n "$PY" ] || { echo "Se necesita Python 3."; exit 2; }

TOKEN=$(curl -s -X POST "$KEYCLOAK_URL/realms/plataforma/protocol/openid-connect/token" -d grant_type=password \
  -d client_id=movil -d username=auditor.1 -d password=auditor.1_local | "$PY" -c "import json,sys;print(json.load(sys.stdin)['access_token'])" | tr -d '\r')
pedir() { curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $TOKEN" "$B$RUTA"; }
ips() { compose ps -q "$SERVICIO" | xargs docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' | sort | tr '\n' ' '; }

gateway_antes=$(compose ps -q gateway | sort | tr '\n' ' ')
ips_antes=$(ips)
echo "Gateway: $gateway_antes"
echo "$SERVICIO antes: IP $ips_antes, $RUTA -> $(pedir)"

echo "Reconstruyendo solo $SERVICIO (contenedores nuevos, sin tocar el gateway)..."
# Docker suele devolverle a un contenedor recreado la misma IP. Para obligar el cambio (lo que pasó en A1b), mientras
# el servicio está abajo se ocupan sus IP viejas con contenedores de relleno de la misma red (imagen ya local).
red=$(docker inspect -f '{{range $k, $v := .NetworkSettings.Networks}}{{$k}}{{end}}' "$(compose ps -q "$SERVICIO" | head -1)")
relleno=$(docker inspect -f '{{.Config.Image}}' "$(compose ps -q cache | head -1)")
compose rm -sf "$SERVICIO" >/dev/null 2>&1
ocupantes=()
for ip in $ips_antes; do
  ocupantes+=("$(docker run -d --rm --network "$red" --ip "$ip" --entrypoint sleep "$relleno" 600)")
done
compose up -d --build --no-deps "$SERVICIO" >/dev/null 2>&1 || { echo "FALLA no se pudo reconstruir $SERVICIO"; docker rm -f "${ocupantes[@]}" >/dev/null; exit 1; }
docker rm -f "${ocupantes[@]}" >/dev/null 2>&1
for _ in $(seq 1 60); do
  [ "$(compose ps "$SERVICIO" --format '{{.Status}}' | grep -vc '(healthy)')" = "0" ] && break
  sleep 2
done
ips_despues=$(ips)
echo "$SERVICIO después: IP $ips_despues"

inicio=$(date +%s); errores=0; codigo=000
for _ in $(seq 1 90); do
  codigo=$(pedir)
  [ "$codigo" = "200" ] && break
  errores=$((errores+1)); sleep 1
done
segundos=$(( $(date +%s) - inicio ))
gateway_despues=$(compose ps -q gateway | sort | tr '\n' ' ')

fallas=0
if [ "$codigo" = "200" ]; then echo "  ok   el gateway encuentra $SERVICIO en ${segundos} s ($errores respuestas distintas de 200 antes)"; else echo "  FALLA el gateway no encuentra $SERVICIO: $codigo"; fallas=1; fi
if [ "$gateway_antes" = "$gateway_despues" ]; then echo "  ok   el gateway no se recreó"; else echo "  FALLA el gateway se recreó"; fallas=1; fi
[ "$ips_antes" != "$ips_despues" ] && echo "  ok   las IP de $SERVICIO cambiaron" || echo "  aviso: las IP de $SERVICIO no cambiaron (la prueba no ejercitó el cambio de IP)"
exit $fallas
