#!/usr/bin/env bash
# Despliega la plataforma en el Minikube LOCAL, que corre dentro de WSL (Debian).
#
# Se ejecuta DENTRO de WSL, desde la raíz del repositorio montado, por ejemplo:
#   wsl -d Debian
#   cd "/mnt/d/Proyecto arqui/proyectoparte2"
#   bash despliegue/k8s/aplicar.sh             construye las imágenes en Minikube, instala los operadores y aplica
#   bash despliegue/k8s/aplicar.sh --borrar    elimina el namespace "plataforma" (y sus datos)
#
# Por seguridad solo trabaja contra el contexto "minikube" y lo pasa explícito en cada comando: nunca usa el contexto
# activo de kubectl, que en este equipo puede apuntar a un clúster en la nube.
set -euo pipefail

CONTEXTO="minikube"
OPERADOR_RABBIT="https://github.com/rabbitmq/cluster-operator/releases/download/v2.23.2/cluster-operator.yml"
raiz="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
k() { kubectl --context "$CONTEXTO" "$@"; }

if ! kubectl config get-contexts -o name | grep -qx "$CONTEXTO"; then
  echo "No existe el contexto '$CONTEXTO'. Inicie Minikube dentro de WSL: minikube start" >&2
  exit 1
fi
if ! minikube status --format '{{.Host}}' >/dev/null 2>&1; then
  echo "Minikube no está corriendo. Inícielo con: minikube start --memory=8192 --cpus=4" >&2
  exit 1
fi

if [ "${1:-}" = "--borrar" ]; then
  k delete namespace plataforma --ignore-not-found
  exit 0
fi

echo "1. Imágenes, construidas dentro de Minikube (no hace falta un registro)"
minikube image build -t plataforma/reconstruccion:local -f servicios/reconstruccion/Dockerfile "$raiz"
minikube image build -t plataforma/evaluacion-danos:local -f servicios/evaluacion-danos/Dockerfile "$raiz"
minikube image build -t plataforma/adaptador-se2:local -f adaptadores/adaptador-se2/Dockerfile "$raiz"
minikube image build -t plataforma/consola:local "$raiz/consola"
# Las imágenes de los demás servicios se agregan aquí a medida que existen (ver despliegue/k8s/base/20-servicios.yaml).

echo "2. Operadores: RabbitMQ Cluster Operator (v2.23.2)"
k apply -f "$OPERADOR_RABBIT"
k -n rabbitmq-system rollout status deployment/rabbitmq-cluster-operator --timeout=180s
k wait --for=condition=Established crd/rabbitmqclusters.rabbitmq.com --timeout=120s

echo "3. Plataforma (namespace plataforma)"
k kustomize --load-restrictor=LoadRestrictionsNone "$raiz/despliegue/k8s/local" | k apply -f -
k -n plataforma wait --for=condition=complete job/reconstruccion-migrador --timeout=600s
k -n plataforma rollout status deployment/reconstruccion --timeout=300s
k -n plataforma rollout status deployment/consulta --timeout=300s

echo "Listo. Consola: http://$(minikube ip):30080"
echo "Prueba: bash pruebas/humo/prueba-humo.sh http://$(minikube ip):30080"
