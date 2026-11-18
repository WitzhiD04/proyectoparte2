# Imagen del simulador de SE-6 (sistema financiero externo, DA-11). No usa nada de la plataforma.
# Se construye desde la raíz del monorepo, como las demás imágenes:
#   docker build -f simuladores/sim-se6/Dockerfile -t plataforma/sim-se6:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilacion
WORKDIR /fuente
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY simuladores/sim-se6/SimSe6.csproj simuladores/sim-se6/
RUN dotnet restore simuladores/sim-se6/SimSe6.csproj
COPY simuladores/sim-se6/ simuladores/sim-se6/
RUN dotnet publish simuladores/sim-se6/SimSe6.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# curl: para el sondeo de salud de compose.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=compilacion /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "SimSe6.dll"]
