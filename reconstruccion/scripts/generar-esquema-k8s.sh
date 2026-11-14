#!/usr/bin/env bash
# Regenera k8s/05-esquema.yaml a partir de los archivos de db/. Requiere kubectl.
# Uso (desde la raíz del repositorio): bash scripts/generar-esquema-k8s.sh
set -euo pipefail
{
  echo "# Esquema inicial, permisos y datos de prueba para PostgreSQL en Kubernetes."
  echo "# Es una copia de los archivos de db/. No se edita a mano: si cambia algo en db/, se regenera con"
  echo "#   bash scripts/generar-esquema-k8s.sh"
  kubectl create configmap esquema-bd --from-file=db/ --dry-run=client -o yaml
} > k8s/05-esquema.yaml
echo "k8s/05-esquema.yaml actualizado."
