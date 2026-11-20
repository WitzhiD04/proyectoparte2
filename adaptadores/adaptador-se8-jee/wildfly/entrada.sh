#!/bin/bash
# Arranque del adaptador de SE-8 en WildFly. Traduce la cadena de conexión con formato .NET
# (ConnectionStrings__AdaptadorSe8="Host=db;Port=5432;Database=adaptador_se8;Username=...;Password=...")
# a las variables que usa el datasource, para que compose y Kubernetes sigan una sola convención.
set -euo pipefail
cadena="${ConnectionStrings__AdaptadorSe8:-}"
if [ -z "$cadena" ]; then
  echo "Falta ConnectionStrings__AdaptadorSe8." >&2
  exit 2
fi
host=""; puerto="5432"; base=""; usuario=""; clave=""
IFS=';' read -ra partes <<< "$cadena"
for parte in "${partes[@]}"; do
  nombre="${parte%%=*}"; valor="${parte#*=}"
  case "${nombre,,}" in
    host) host="$valor" ;;
    port) puerto="$valor" ;;
    database) base="$valor" ;;
    username|"user id") usuario="$valor" ;;
    password) clave="$valor" ;;
  esac
done
export SE8_BD_URL="jdbc:postgresql://${host}:${puerto}/${base}"
export SE8_BD_USUARIO="$usuario"
export SE8_BD_CLAVE="$clave"
exec /opt/jboss/wildfly/bin/standalone.sh -b 0.0.0.0 -c standalone.xml
