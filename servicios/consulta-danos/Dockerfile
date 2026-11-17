# Imagen de Consulta de Daños (modelo de lectura de la evaluación de daños, DA-08). Con el argumento "migrar" es el
# migrador del esquema (DT-19). Se construye desde la raíz del monorepo, porque usa los proyectos de comun/:
#   docker build -f servicios/consulta-danos/Dockerfile -t plataforma/consulta-danos:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilacion
WORKDIR /fuente
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY comun/Plataforma.Comun.Eventos/Plataforma.Comun.Eventos.csproj comun/Plataforma.Comun.Eventos/
COPY comun/Plataforma.Comun.Mensajeria/Plataforma.Comun.Mensajeria.csproj comun/Plataforma.Comun.Mensajeria/
COPY comun/Plataforma.Comun.Salud/Plataforma.Comun.Salud.csproj comun/Plataforma.Comun.Salud/
COPY comun/Plataforma.Comun.Migraciones/Plataforma.Comun.Migraciones.csproj comun/Plataforma.Comun.Migraciones/
COPY comun/Plataforma.Comun.Seguridad/Plataforma.Comun.Seguridad.csproj comun/Plataforma.Comun.Seguridad/
COPY servicios/consulta-danos/src/ConsultaDanos.Dominio/ConsultaDanos.Dominio.csproj servicios/consulta-danos/src/ConsultaDanos.Dominio/
COPY servicios/consulta-danos/src/ConsultaDanos.Aplicacion/ConsultaDanos.Aplicacion.csproj servicios/consulta-danos/src/ConsultaDanos.Aplicacion/
COPY servicios/consulta-danos/src/ConsultaDanos.Infraestructura/ConsultaDanos.Infraestructura.csproj servicios/consulta-danos/src/ConsultaDanos.Infraestructura/
COPY servicios/consulta-danos/src/ConsultaDanos.Api/ConsultaDanos.Api.csproj servicios/consulta-danos/src/ConsultaDanos.Api/
RUN dotnet restore servicios/consulta-danos/src/ConsultaDanos.Api/ConsultaDanos.Api.csproj
COPY comun/ comun/
COPY servicios/consulta-danos/src/ servicios/consulta-danos/src/
RUN dotnet publish servicios/consulta-danos/src/ConsultaDanos.Api/ConsultaDanos.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Npgsql intenta cargar la biblioteca de Kerberos; curl es para el sondeo de salud de compose.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=compilacion /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ConsultaDanos.Api.dll"]
