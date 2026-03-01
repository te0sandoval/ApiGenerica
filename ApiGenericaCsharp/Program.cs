// Program.cs - Punto de entrada de la API Genérica
// Configuración de servicios, inyección de dependencias y middleware

// Importa tipos básicos como TimeSpan para configurar tiempos.
using System;

// Importa la API para construir la aplicación web y el pipeline HTTP.
using Microsoft.AspNetCore.Builder;

// Importa la API para registrar servicios en el contenedor de inyección de dependencias (DIP).
using Microsoft.Extensions.DependencyInjection;

// Importa utilidades para detectar el entorno (Desarrollo, Producción, etc.).
using Microsoft.Extensions.Hosting;

// Importa utilidades para autenticación JWT
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using ApiGenericaCsharp.Modelos; // donde está ConfiguracionJwt

// Crea el "builder": punto de inicio para configurar servicios y la aplicación.
var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------
// CONFIGURACIÓN (OCP: se puede extender sin tocar la lógica)
// ---------------------------------------------------------

// Agrega un archivo JSON opcional para configuraciones adicionales.
// Si "tablasprohibidas.json" existe, se carga; si no existe, no falla.
builder.Configuration.AddJsonFile(
    "tablasprohibidas.json",
    optional: true,
    reloadOnChange: true
);

// ---------------------------------------------------------
// SERVICIOS (SRP: solo registro, sin lógica de negocio aquí)
// ---------------------------------------------------------

// Agrega soporte para controladores. Los controladores viven en la carpeta "Controllers".
builder.Services.AddControllers();

// CORS (Cross-Origin Resource Sharing) Intercambio de recursos de origen cruzado
// Permite que la API sea consumida desde otros dominios.
// Agrega CORS con una política genérica llamada "PermitirTodo".
builder.Services.AddCors(opts =>
{
    opts.AddPolicy("PermitirTodo", politica => politica
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader()
    );
});

// Agrega caché en memoria y sesión HTTP ligera (opcional).
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(opciones =>
{
    // Tiempo de inactividad permitido antes de expirar la sesión.
    opciones.IdleTimeout = TimeSpan.FromMinutes(30);

    // Marca la cookie de sesión como accesible solo por HTTP (más seguro).
    opciones.Cookie.HttpOnly = true;

    // Indica que la cookie es esencial para el funcionamiento.
    opciones.Cookie.IsEssential = true;
});

// Agrega servicios para exponer documentación Swagger/OpenAPI.
builder.Services.AddEndpointsApiExplorer();

// Configuración avanzada de Swagger para mostrar el botón "Authorize"
builder.Services.AddSwaggerGen(opciones =>
{
    opciones.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "API Genérica CRUD Multi-Base de Datos",
        Version = "v1",
        Description = "API REST genérica para operaciones CRUD sobre cualquier tabla. Soporta SQL Server, PostgreSQL, MySQL/MariaDB. Incluye autenticación JWT."
    });

    // Define el esquema de seguridad JWT
    var esquemaSeguridad = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Ingrese el token con el prefijo 'Bearer'. Ejemplo: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
    };

    // Añade la definición del esquema
    opciones.AddSecurityDefinition("Bearer", esquemaSeguridad);

    // Indica que todos los endpoints usan este esquema por defecto
    opciones.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

// -----------------------------------------------------------------
// REGISTRO DE SERVICIOS (Dependency Injection - DIP)
// -----------------------------------------------------------------
// TIPOS DE REGISTRO:
// - AddSingleton: Una instancia para toda la aplicación (compartida entre requests)
// - AddScoped: Una instancia por request HTTP (se crea y destruye con cada petición)
// - AddTransient: Una instancia nueva cada vez que se solicita
// -----------------------------------------------------------------

// -----------------------------------------------------------------
// REGISTRO DE POLÍTICA DE TABLAS PROHIBIDAS
// -----------------------------------------------------------------
builder.Services.AddSingleton<
    ApiGenericaCsharp.Servicios.Abstracciones.IPoliticaTablasProhibidas,
    ApiGenericaCsharp.Servicios.Politicas.PoliticaTablasProhibidasDesdeJson>();

// -----------------------------------------------------------------
// REGISTRO DE SERVICIO CRUD (DIP)
// -----------------------------------------------------------------
builder.Services.AddScoped<
    ApiGenericaCsharp.Servicios.Abstracciones.IServicioCrud,
    ApiGenericaCsharp.Servicios.ServicioCrud>();

// -----------------------------------------------------------------
// REGISTRO DEL PROVEEDOR DE CONEXIÓN (DIP)
// -----------------------------------------------------------------
builder.Services.AddSingleton<
    ApiGenericaCsharp.Servicios.Abstracciones.IProveedorConexion,
    ApiGenericaCsharp.Servicios.Conexion.ProveedorConexion>();

// Lee el proveedor de BD desde la configuración
var proveedorBD = builder.Configuration.GetValue<string>("DatabaseProvider") ?? "SqlServer";

// -----------------------------------------------------------------
// REGISTRO DE SERVICIO CONSULTAS (DIP)
// -----------------------------------------------------------------
builder.Services.AddScoped<ApiGenericaCsharp.Servicios.Abstracciones.IServicioConsultas,
    ApiGenericaCsharp.Servicios.ServicioConsultas>();

// -----------------------------------------------------------------
// REGISTRO AUTOMÁTICO DEL REPOSITORIO SEGÚN DatabaseProvider
// -----------------------------------------------------------------
switch (proveedorBD.ToLower())
{
    case "postgres":
        // Repositorio de lectura para PostgreSQL
        builder.Services.AddScoped<ApiGenericaCsharp.Repositorios.Abstracciones.IRepositorioLecturaTabla,
                                   ApiGenericaCsharp.Repositorios.RepositorioLecturaPostgreSQL>();
        // Repositorio de consultas para PostgreSQL
        builder.Services.AddScoped<
            ApiGenericaCsharp.Repositorios.Abstracciones.IRepositorioConsultas,
            ApiGenericaCsharp.Repositorios.RepositorioConsultasPostgreSQL
        >();
        break;

    case "mariadb":
    case "mysql":
        // Repositorio de lectura para MySQL/MariaDB
        builder.Services.AddScoped<
            ApiGenericaCsharp.Repositorios.Abstracciones.IRepositorioLecturaTabla,
            ApiGenericaCsharp.Repositorios.RepositorioLecturaMysqlMariaDB>();

        // Repositorio de consultas para MySQL/MariaDB
        builder.Services.AddScoped<
            ApiGenericaCsharp.Repositorios.Abstracciones.IRepositorioConsultas,
            ApiGenericaCsharp.Repositorios.RepositorioConsultasMysqlMariaDB>();
        break;

    case "sqlserver":
    case "sqlserverexpress":
    case "localdb":
    default:
        // Repositorio de lectura para SQL Server (incluyendo LocalDb)
        builder.Services.AddScoped<ApiGenericaCsharp.Repositorios.Abstracciones.IRepositorioLecturaTabla,
                                   ApiGenericaCsharp.Repositorios.RepositorioLecturaSqlServer>();

        // Repositorio de consultas para SQL Server
        builder.Services.AddScoped<ApiGenericaCsharp.Repositorios.Abstracciones.IRepositorioConsultas,
                               ApiGenericaCsharp.Repositorios.RepositorioConsultasSqlServer>();
        break;
}

// ---------------------------------------------------------
// CONFIGURACIÓN JWT (para autenticación segura con tokens)
// ---------------------------------------------------------

// Vincula la sección "Jwt" del archivo appsettings.json a la clase ConfiguracionJwt.
builder.Services.Configure<ConfiguracionJwt>(
    builder.Configuration.GetSection("Jwt")
);

// Crea una instancia temporal con la configuración de JWT.
var configuracionJwt = new ConfiguracionJwt();
builder.Configuration.GetSection("Jwt").Bind(configuracionJwt);

// Registra el servicio de autenticación basado en JWT Bearer.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opciones =>
    {
        // Define las reglas de validación del token.
        opciones.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,           // Valida el emisor del token.
            ValidateAudience = true,         // Valida el público objetivo.
            ValidateLifetime = true,         // Valida que no esté expirado.
            ValidateIssuerSigningKey = true, // Valida la firma.
            ValidIssuer = configuracionJwt.Issuer,     // Emisor válido.
            ValidAudience = configuracionJwt.Audience, // Audiencia válida.
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(configuracionJwt.Key) // Clave secreta.
            )
        };
    });

// Construye la aplicación con todo lo configurado arriba.
var app = builder.Build();

// ---------------------------------------------------------
// MIDDLEWARE (orden importa: se ejecuta de arriba hacia abajo)
// ---------------------------------------------------------

// En Desarrollo se muestran páginas de error detalladas para depurar.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Habilita Swagger y expone la UI en la ruta /swagger.
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ApiGenericaCsharp v1");
    c.RoutePrefix = "swagger";
});

// Habilita ReDoc en la ruta /redoc (documentación alternativa de solo lectura).
app.UseReDoc(c =>
{
    c.RoutePrefix = "redoc";
    c.SpecUrl("/swagger/v1/swagger.json");
});

// Redirige HTTP a HTTPS para mejorar la seguridad.
app.UseHttpsRedirection();

// Aplica la política CORS definida como "PermitirTodo".
app.UseCors("PermitirTodo");

// Habilita la sesión HTTP.
app.UseSession();

// Activa la autenticación JWT antes de aplicar la autorización.
app.UseAuthentication();

// Agrega el middleware de autorización.
app.UseAuthorization();

// Mapea las rutas de los controladores.
app.MapControllers();

// Arranca la aplicación y queda escuchando solicitudes HTTP.
app.Run();

