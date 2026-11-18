# Imagen del adaptador de SE-6 (sistema financiero externo, DA-11). La misma imagen corre como:
#   - salida   (argumento "salida" o sin argumento): consume q.salida.se6 en lotes y entrega EV-18, EV-19 y EV-20 a SE-6
#   - migrador de su esquema se6 (argumento "migrar")
# Se construye desde la raíz del monorepo, porque usa los proyectos de comun/:
#   docker build -f adaptadores/adaptador-se6/Dockerfile -t plataforma/adaptador-se6:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilacion
WORKDIR /fuente
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY comun/Plataforma.Comun.Eventos/Plataforma.Comun.Eventos.csproj comun/Plataforma.Comun.Eventos/
COPY comun/Plataforma.Comun.Mensajeria/Plataforma.Comun.Mensajeria.csproj comun/Plataforma.Comun.Mensajeria/
COPY comun/Plataforma.Comun.Salud/Plataforma.Comun.Salud.csproj comun/Plataforma.Comun.Salud/
COPY comun/Plataforma.Comun.Migraciones/Plataforma.Comun.Migraciones.csproj comun/Plataforma.Comun.Migraciones/
COPY comun/Plataforma.Comun.Adaptadores/Plataforma.Comun.Adaptadores.csproj comun/Plataforma.Comun.Adaptadores/
COPY adaptadores/adaptador-se6/src/AdaptadorSe6/AdaptadorSe6.csproj adaptadores/adaptador-se6/src/AdaptadorSe6/
RUN dotnet restore adaptadores/adaptador-se6/src/AdaptadorSe6/AdaptadorSe6.csproj
COPY comun/ comun/
COPY adaptadores/adaptador-se6/src/ adaptadores/adaptador-se6/src/
RUN dotnet publish adaptadores/adaptador-se6/src/AdaptadorSe6/AdaptadorSe6.csproj -c Release -o /app --no-restore

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
ENTRYPOINT ["dotnet", "AdaptadorSe6.dll"]
