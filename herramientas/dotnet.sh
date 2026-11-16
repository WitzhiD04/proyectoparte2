#!/usr/bin/env bash
# Ejecuta el SDK de .NET 10 dentro de un contenedor, sin instalarlo en la máquina.
# Monta el repositorio en /repo, guarda la caché de NuGet en un volumen y comparte el socket de Docker
# para que Testcontainers pueda levantar PostgreSQL y RabbitMQ en las pruebas de integración.
#
# Uso (desde cualquier carpeta del repositorio):
#   bash herramientas/dotnet.sh test Plataforma.sln
#   bash herramientas/dotnet.sh build servicios/reconstruccion/Reconstruccion.sln
set -euo pipefail

raiz="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# En Git Bash para Windows, Docker necesita la ruta con formato de Windows.
if command -v cygpath >/dev/null 2>&1; then
  raiz_docker="$(cygpath -w "$raiz")"
else
  raiz_docker="$raiz"
fi

MSYS_NO_PATHCONV=1 docker run --rm \
  -v "$raiz_docker:/repo" \
  -v plataforma-nuget:/root/.nuget/packages \
  -v plataforma-dotnet:/root/.dotnet \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  -e DOTNET_NOLOGO=1 \
  -w /repo \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet "$@"
