# Imagen del simulador de SE-8 (comunicación y notificación, AD-66). No usa nada de la plataforma.
# Se construye desde la raíz del monorepo, como las demás imágenes:
#   docker build -f simuladores/sim-se8/Dockerfile -t plataforma/sim-se8:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilacion
WORKDIR /fuente
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY simuladores/sim-se8/SimSe8.csproj simuladores/sim-se8/
RUN dotnet restore simuladores/sim-se8/SimSe8.csproj
COPY simuladores/sim-se8/ simuladores/sim-se8/
RUN dotnet publish simuladores/sim-se8/SimSe8.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# curl: para el sondeo de salud de compose y para la prueba de humo (exec ... curl).
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=compilacion /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "SimSe8.dll"]
