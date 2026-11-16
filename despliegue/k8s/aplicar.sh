#!/usr/bin/env bash
# Despliega la plataforma en el Kubernetes LOCAL de Docker Desktop.
#
# Por seguridad solo trabaja contra el contexto "docker-desktop" y lo pasa explícito en cada comando: nunca usa el
# contexto activo de kubectl, que puede apuntar a un clúster en la nube.
#
# Uso (desde la raíz del repositorio):
#   bash despliegue/k8s/aplicar.sh             construye las imágenes, instala el operador de RabbitMQ y aplica
#   bash despliegue/k8s/aplicar.sh --borrar    elimina el namespace "plataforma" (y sus datos)
set -euo pipefail

CONTEXTO="docker-desktop"
OPERADOR_RABBIT="https://github.com/rabbitmq/cluster-operator/releases/download/v2.23.2/cluster-operator.yml"
raiz="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
k() { kubectl --context "$CONTEXTO" "$@"; }

if ! kubectl config get-contexts -o name | grep -qx "$CONTEXTO"; then
  echo "No existe el contexto '$CONTEXTO'. Active Kubernetes en Docker Desktop (Settings > Kubernetes)." >&2
  exit 1
fi

if [ "${1:-}" = "--borrar" ]; then
  k delete namespace plataforma --ignore-not-found
  exit 0
fi

echo "1. Imágenes locales"
docker build -f "$raiz/servicios/reconstruccion/Dockerfile" -t plataforma/reconstruccion:local "$raiz"
docker build -t plataforma/consola:local "$raiz/consola"

echo "2. Operador de RabbitMQ (v2.23.2)"
k apply -f "$OPERADOR_RABBIT"
k -n rabbitmq-system rollout status deployment/rabbitmq-cluster-operator --timeout=180s

echo "3. Plataforma (namespace plataforma)"
k kustomize --load-restrictor=LoadRestrictionsNone "$raiz/despliegue/k8s/local" | k apply -f -
k -n plataforma wait --for=condition=complete job/reconstruccion-migrador --timeout=600s
k -n plataforma rollout status deployment/reconstruccion --timeout=300s
k -n plataforma rollout status deployment/consulta --timeout=300s
echo "Listo. Consola en http://localhost:30080 (prueba: bash pruebas/humo/prueba-humo.sh http://localhost:30080)"
