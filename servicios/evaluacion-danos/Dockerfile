# Imagen de Evaluación de Daños. La misma imagen corre como:
#   - servicio de Evaluación de Daños (sin argumentos)
#   - migrador del esquema            (argumento "migrar", opcionalmente "--sembrar" para el catálogo inicial)
# Se construye desde la raíz del monorepo, porque usa los proyectos de comun/:
#   docker build -f servicios/evaluacion-danos/Dockerfile -t plataforma/evaluacion-danos:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilacion
WORKDIR /fuente
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY comun/Plataforma.Comun.Eventos/Plataforma.Comun.Eventos.csproj comun/Plataforma.Comun.Eventos/
COPY comun/Plataforma.Comun.Mensajeria/Plataforma.Comun.Mensajeria.csproj comun/Plataforma.Comun.Mensajeria/
COPY comun/Plataforma.Comun.Salud/Plataforma.Comun.Salud.csproj comun/Plataforma.Comun.Salud/
COPY comun/Plataforma.Comun.Seguridad/Plataforma.Comun.Seguridad.csproj comun/Plataforma.Comun.Seguridad/
COPY comun/Plataforma.Comun.Migraciones/Plataforma.Comun.Migraciones.csproj comun/Plataforma.Comun.Migraciones/
COPY servicios/evaluacion-danos/src/EvaluacionDanos.Dominio/EvaluacionDanos.Dominio.csproj servicios/evaluacion-danos/src/EvaluacionDanos.Dominio/
COPY servicios/evaluacion-danos/src/EvaluacionDanos.Aplicacion/EvaluacionDanos.Aplicacion.csproj servicios/evaluacion-danos/src/EvaluacionDanos.Aplicacion/
COPY servicios/evaluacion-danos/src/EvaluacionDanos.Infraestructura/EvaluacionDanos.Infraestructura.csproj servicios/evaluacion-danos/src/EvaluacionDanos.Infraestructura/
COPY servicios/evaluacion-danos/src/EvaluacionDanos.Api/EvaluacionDanos.Api.csproj servicios/evaluacion-danos/src/EvaluacionDanos.Api/
RUN dotnet restore servicios/evaluacion-danos/src/EvaluacionDanos.Api/EvaluacionDanos.Api.csproj
COPY comun/ comun/
COPY servicios/evaluacion-danos/src/ servicios/evaluacion-danos/src/
RUN dotnet publish servicios/evaluacion-danos/src/EvaluacionDanos.Api/EvaluacionDanos.Api.csproj -c Release -o /app --no-restore

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
ENTRYPOINT ["dotnet", "EvaluacionDanos.Api.dll"]
