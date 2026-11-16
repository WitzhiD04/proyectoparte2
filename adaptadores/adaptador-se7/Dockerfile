# Imagen del adaptador de SE-7 (sistema de proyectos externo). La misma imagen corre como:
#   - salida   (argumento "salida"):  consume q.salida.se7 y entrega EV-18 y EV-20 a SE-7
#   - entrada  (argumento "entrada"): consulta los cambios de SE-7 desde su marca de avance y publica EV-24
#   - migrador de su esquema (argumento "migrar")
# Se construye desde la raíz del monorepo, porque usa los proyectos de comun/:
#   docker build -f adaptadores/adaptador-se7/Dockerfile -t plataforma/adaptador-se7:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilacion
WORKDIR /fuente
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY comun/Plataforma.Comun.Eventos/Plataforma.Comun.Eventos.csproj comun/Plataforma.Comun.Eventos/
COPY comun/Plataforma.Comun.Mensajeria/Plataforma.Comun.Mensajeria.csproj comun/Plataforma.Comun.Mensajeria/
COPY comun/Plataforma.Comun.Salud/Plataforma.Comun.Salud.csproj comun/Plataforma.Comun.Salud/
COPY comun/Plataforma.Comun.Migraciones/Plataforma.Comun.Migraciones.csproj comun/Plataforma.Comun.Migraciones/
COPY comun/Plataforma.Comun.Adaptadores/Plataforma.Comun.Adaptadores.csproj comun/Plataforma.Comun.Adaptadores/
COPY adaptadores/adaptador-se7/src/AdaptadorSe7/AdaptadorSe7.csproj adaptadores/adaptador-se7/src/AdaptadorSe7/
RUN dotnet restore adaptadores/adaptador-se7/src/AdaptadorSe7/AdaptadorSe7.csproj
COPY comun/ comun/
COPY adaptadores/adaptador-se7/src/ adaptadores/adaptador-se7/src/
RUN dotnet publish adaptadores/adaptador-se7/src/AdaptadorSe7/AdaptadorSe7.csproj -c Release -o /app --no-restore

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
ENTRYPOINT ["dotnet", "AdaptadorSe7.dll"]
