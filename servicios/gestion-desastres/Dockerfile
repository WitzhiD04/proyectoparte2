# Imagen de Gestión de Desastres. Con el argumento "migrar" es el migrador del esquema (DT-19).
# Se construye desde la raíz del monorepo, porque usa los proyectos de comun/:
#   docker build -f servicios/gestion-desastres/Dockerfile -t plataforma/gestion-desastres:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilacion
WORKDIR /fuente
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY comun/Plataforma.Comun.Eventos/Plataforma.Comun.Eventos.csproj comun/Plataforma.Comun.Eventos/
COPY comun/Plataforma.Comun.Mensajeria/Plataforma.Comun.Mensajeria.csproj comun/Plataforma.Comun.Mensajeria/
COPY comun/Plataforma.Comun.Salud/Plataforma.Comun.Salud.csproj comun/Plataforma.Comun.Salud/
COPY comun/Plataforma.Comun.Migraciones/Plataforma.Comun.Migraciones.csproj comun/Plataforma.Comun.Migraciones/
COPY comun/Plataforma.Comun.Seguridad/Plataforma.Comun.Seguridad.csproj comun/Plataforma.Comun.Seguridad/
COPY servicios/gestion-desastres/src/GestionDesastres.Dominio/GestionDesastres.Dominio.csproj servicios/gestion-desastres/src/GestionDesastres.Dominio/
COPY servicios/gestion-desastres/src/GestionDesastres.Aplicacion/GestionDesastres.Aplicacion.csproj servicios/gestion-desastres/src/GestionDesastres.Aplicacion/
COPY servicios/gestion-desastres/src/GestionDesastres.Infraestructura/GestionDesastres.Infraestructura.csproj servicios/gestion-desastres/src/GestionDesastres.Infraestructura/
COPY servicios/gestion-desastres/src/GestionDesastres.Api/GestionDesastres.Api.csproj servicios/gestion-desastres/src/GestionDesastres.Api/
RUN dotnet restore servicios/gestion-desastres/src/GestionDesastres.Api/GestionDesastres.Api.csproj
COPY comun/ comun/
COPY servicios/gestion-desastres/src/ servicios/gestion-desastres/src/
RUN dotnet publish servicios/gestion-desastres/src/GestionDesastres.Api/GestionDesastres.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Npgsql intenta cargar la biblioteca de Kerberos; curl es para el sondeo de salud de compose.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=compilacion /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "GestionDesastres.Api.dll"]
