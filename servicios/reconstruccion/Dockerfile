# Imagen de Reconstrucción. La misma imagen corre como:
#   - servicio de Reconstrucción      (Reconstruccion__Rol=escritura)
#   - servicio de Consulta del Programa (Reconstruccion__Rol=consulta)
#   - migrador del esquema             (argumento "migrar", opcionalmente "--sembrar")
# Se construye desde la raíz del monorepo, porque usa los proyectos de comun/:
#   docker build -f servicios/reconstruccion/Dockerfile -t reconstruccion-api:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilacion
WORKDIR /fuente
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY comun/Plataforma.Comun.Eventos/Plataforma.Comun.Eventos.csproj comun/Plataforma.Comun.Eventos/
COPY comun/Plataforma.Comun.Mensajeria/Plataforma.Comun.Mensajeria.csproj comun/Plataforma.Comun.Mensajeria/
COPY comun/Plataforma.Comun.Salud/Plataforma.Comun.Salud.csproj comun/Plataforma.Comun.Salud/
COPY comun/Plataforma.Comun.Migraciones/Plataforma.Comun.Migraciones.csproj comun/Plataforma.Comun.Migraciones/
COPY servicios/reconstruccion/src/Reconstruccion.Dominio/Reconstruccion.Dominio.csproj servicios/reconstruccion/src/Reconstruccion.Dominio/
COPY servicios/reconstruccion/src/Reconstruccion.Aplicacion/Reconstruccion.Aplicacion.csproj servicios/reconstruccion/src/Reconstruccion.Aplicacion/
COPY servicios/reconstruccion/src/Reconstruccion.Infraestructura/Reconstruccion.Infraestructura.csproj servicios/reconstruccion/src/Reconstruccion.Infraestructura/
COPY servicios/reconstruccion/src/Reconstruccion.Api/Reconstruccion.Api.csproj servicios/reconstruccion/src/Reconstruccion.Api/
RUN dotnet restore servicios/reconstruccion/src/Reconstruccion.Api/Reconstruccion.Api.csproj
COPY comun/ comun/
COPY servicios/reconstruccion/src/ servicios/reconstruccion/src/
RUN dotnet publish servicios/reconstruccion/src/Reconstruccion.Api/Reconstruccion.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Npgsql intenta cargar la biblioteca de Kerberos al conectarse; sin ella escribe un error en el log.
# curl: para el sondeo de salud de compose.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=compilacion /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# Sin privilegios dentro del contenedor.
USER $APP_UID
ENTRYPOINT ["dotnet", "Reconstruccion.Api.dll"]
