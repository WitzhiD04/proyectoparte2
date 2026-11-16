# Imagen de Consulta (modelo de lectura de desastres). Con el argumento "migrar" es el migrador del esquema (DT-19).
# Se construye desde la raíz del monorepo, porque usa los proyectos de comun/:
#   docker build -f servicios/consulta/Dockerfile -t plataforma/consulta:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilacion
WORKDIR /fuente
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY comun/Plataforma.Comun.Eventos/Plataforma.Comun.Eventos.csproj comun/Plataforma.Comun.Eventos/
COPY comun/Plataforma.Comun.Mensajeria/Plataforma.Comun.Mensajeria.csproj comun/Plataforma.Comun.Mensajeria/
COPY comun/Plataforma.Comun.Salud/Plataforma.Comun.Salud.csproj comun/Plataforma.Comun.Salud/
COPY comun/Plataforma.Comun.Migraciones/Plataforma.Comun.Migraciones.csproj comun/Plataforma.Comun.Migraciones/
COPY comun/Plataforma.Comun.Seguridad/Plataforma.Comun.Seguridad.csproj comun/Plataforma.Comun.Seguridad/
COPY servicios/consulta/src/Consulta.Dominio/Consulta.Dominio.csproj servicios/consulta/src/Consulta.Dominio/
COPY servicios/consulta/src/Consulta.Aplicacion/Consulta.Aplicacion.csproj servicios/consulta/src/Consulta.Aplicacion/
COPY servicios/consulta/src/Consulta.Infraestructura/Consulta.Infraestructura.csproj servicios/consulta/src/Consulta.Infraestructura/
COPY servicios/consulta/src/Consulta.Api/Consulta.Api.csproj servicios/consulta/src/Consulta.Api/
RUN dotnet restore servicios/consulta/src/Consulta.Api/Consulta.Api.csproj
COPY comun/ comun/
COPY servicios/consulta/src/ servicios/consulta/src/
RUN dotnet publish servicios/consulta/src/Consulta.Api/Consulta.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Npgsql intenta cargar la biblioteca de Kerberos; curl es para el sondeo de salud de compose.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=compilacion /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Consulta.Api.dll"]
