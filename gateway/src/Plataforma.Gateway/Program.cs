using Plataforma.Gateway;

// API Gateway (AD-04): única entrada de la consola y de la API. El catálogo de rutas está en rutas.json (DA-12).
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
// Gateway:Catalogo permite cargar otro catálogo (las pruebas usan uno con servicios simulados).
builder.Configuration.AddJsonFile(builder.Configuration["Gateway:Catalogo"] ?? "rutas.json", optional: false, reloadOnChange: false);
// Las variables de entorno van después para poder cambiar direcciones y parámetros sin tocar el catálogo.
builder.Configuration.AddEnvironmentVariables();

builder.Services.AgregarGateway(builder.Configuration);

WebApplication app = builder.Build();
app.UsarGateway();
await app.RunAsync();

/// <summary>Visible para las pruebas (WebApplicationFactory).</summary>
public partial class Program;
