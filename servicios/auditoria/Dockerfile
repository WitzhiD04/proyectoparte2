# Imagen de Auditoría. La misma imagen corre como:
#   - servicio de Auditoría (consumidor de q.auditoria, detección de huecos y consulta del rastro)
#   - migrador del esquema   (argumento "migrar")
# Se construye desde la raíz del monorepo, porque usa los proyectos de comun/:
#   docker build -f servicios/auditoria/Dockerfile -t plataforma/auditoria:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilacion
WORKDIR /fuente
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY comun/Plataforma.Comun.Eventos/Plataforma.Comun.Eventos.csproj comun/Plataforma.Comun.Eventos/
COPY comun/Plataforma.Comun.Mensajeria/Plataforma.Comun.Mensajeria.csproj comun/Plataforma.Comun.Mensajeria/
COPY comun/Plataforma.Comun.Salud/Plataforma.Comun.Salud.csproj comun/Plataforma.Comun.Salud/
COPY comun/Plataforma.Comun.Migraciones/Plataforma.Comun.Migraciones.csproj comun/Plataforma.Comun.Migraciones/
COPY comun/Plataforma.Comun.Seguridad/Plataforma.Comun.Seguridad.csproj comun/Plataforma.Comun.Seguridad/
COPY servicios/auditoria/src/Auditoria.Dominio/Auditoria.Dominio.csproj servicios/auditoria/src/Auditoria.Dominio/
COPY servicios/auditoria/src/Auditoria.Aplicacion/Auditoria.Aplicacion.csproj servicios/auditoria/src/Auditoria.Aplicacion/
COPY servicios/auditoria/src/Auditoria.Infraestructura/Auditoria.Infraestructura.csproj servicios/auditoria/src/Auditoria.Infraestructura/
COPY servicios/auditoria/src/Auditoria.Api/Auditoria.Api.csproj servicios/auditoria/src/Auditoria.Api/
RUN dotnet restore servicios/auditoria/src/Auditoria.Api/Auditoria.Api.csproj
COPY comun/ comun/
COPY servicios/auditoria/src/ servicios/auditoria/src/
RUN dotnet publish servicios/auditoria/src/Auditoria.Api/Auditoria.Api.csproj -c Release -o /app --no-restore

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
ENTRYPOINT ["dotnet", "Auditoria.Api.dll"]
