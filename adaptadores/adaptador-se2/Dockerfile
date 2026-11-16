# Imagen del adaptador de SE-2 (capas geográficas). La misma imagen corre como:
#   - adaptador (sin argumentos): publica la capa de municipios en EV-21 si su versión no ha salido
#   - migrador de su base (argumento "migrar")
# Se construye desde la raíz del monorepo, porque usa los proyectos de comun/ y la capa de datos/geo:
#   docker build -f adaptadores/adaptador-se2/Dockerfile -t plataforma/adaptador-se2:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilacion
WORKDIR /fuente
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY comun/Plataforma.Comun.Eventos/Plataforma.Comun.Eventos.csproj comun/Plataforma.Comun.Eventos/
COPY comun/Plataforma.Comun.Mensajeria/Plataforma.Comun.Mensajeria.csproj comun/Plataforma.Comun.Mensajeria/
COPY comun/Plataforma.Comun.Salud/Plataforma.Comun.Salud.csproj comun/Plataforma.Comun.Salud/
COPY comun/Plataforma.Comun.Migraciones/Plataforma.Comun.Migraciones.csproj comun/Plataforma.Comun.Migraciones/
COPY adaptadores/adaptador-se2/src/AdaptadorSe2/AdaptadorSe2.csproj adaptadores/adaptador-se2/src/AdaptadorSe2/
RUN dotnet restore adaptadores/adaptador-se2/src/AdaptadorSe2/AdaptadorSe2.csproj
COPY comun/ comun/
COPY adaptadores/adaptador-se2/src/ adaptadores/adaptador-se2/src/
RUN dotnet publish adaptadores/adaptador-se2/src/AdaptadorSe2/AdaptadorSe2.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Npgsql intenta cargar la biblioteca de Kerberos al conectarse; sin ella escribe un error en el log.
# curl: para el sondeo de salud de compose.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=compilacion /app .
# La capa que entrega SE-2 (ver datos/geo/LEEME.md). Para publicar una versión nueva se reemplaza este archivo.
COPY datos/geo/municipios-colombia.geojson datos/geo/municipios-colombia.geojson
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# Sin privilegios dentro del contenedor.
USER $APP_UID
ENTRYPOINT ["dotnet", "AdaptadorSe2.dll"]
