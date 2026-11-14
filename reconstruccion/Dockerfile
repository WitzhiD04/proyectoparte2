# Servicio de Reconstrucción y servicio de Consulta del Programa: es la misma imagen;
# la variable Reconstruccion__Rol decide con qué rol arranca (escritura o consulta).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilacion
WORKDIR /fuente
COPY src/Reconstruccion.Dominio/Reconstruccion.Dominio.csproj src/Reconstruccion.Dominio/
COPY src/Reconstruccion.Aplicacion/Reconstruccion.Aplicacion.csproj src/Reconstruccion.Aplicacion/
COPY src/Reconstruccion.Infraestructura/Reconstruccion.Infraestructura.csproj src/Reconstruccion.Infraestructura/
COPY src/Reconstruccion.Api/Reconstruccion.Api.csproj src/Reconstruccion.Api/
RUN dotnet restore src/Reconstruccion.Api/Reconstruccion.Api.csproj
COPY src/ src/
RUN dotnet publish src/Reconstruccion.Api/Reconstruccion.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Npgsql intenta cargar la biblioteca de Kerberos al conectarse; sin ella escribe un error en el log.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=compilacion /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Reconstruccion.Api.dll"]
