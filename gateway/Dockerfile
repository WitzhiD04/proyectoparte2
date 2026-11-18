# Imagen del API Gateway (YARP, DT-07). Se construye desde la raíz del monorepo, porque usa los proyectos de comun/:
#   docker build -f gateway/Dockerfile -t plataforma/gateway:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilacion
WORKDIR /fuente
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY comun/Plataforma.Comun.Salud/Plataforma.Comun.Salud.csproj comun/Plataforma.Comun.Salud/
COPY comun/Plataforma.Comun.Seguridad/Plataforma.Comun.Seguridad.csproj comun/Plataforma.Comun.Seguridad/
COPY gateway/src/Plataforma.Gateway/Plataforma.Gateway.csproj gateway/src/Plataforma.Gateway/
RUN dotnet restore gateway/src/Plataforma.Gateway/Plataforma.Gateway.csproj
COPY comun/Plataforma.Comun.Salud/ comun/Plataforma.Comun.Salud/
COPY comun/Plataforma.Comun.Seguridad/ comun/Plataforma.Comun.Seguridad/
COPY gateway/src/ gateway/src/
RUN dotnet publish gateway/src/Plataforma.Gateway/Plataforma.Gateway.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# curl: para el sondeo de salud de compose.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=compilacion /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Plataforma.Gateway.dll"]
