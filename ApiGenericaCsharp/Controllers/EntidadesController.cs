// EntidadesController.cs — Controlador genérico para operaciones HTTP sobre cualquier tabla
// Ubicación: Controllers/EntidadesController.cs
//
// Principios SOLID aplicados:
// - SRP: El controlador solo coordina peticiones HTTP, no contiene lógica de negocio ni acceso a datos
// - DIP: Depende de IServicioCrud (abstracción), no de ServicioCrud (implementación concreta)
// - ISP: Consume solo los métodos necesarios de IServicioCrud
// - OCP: Preparado para agregar más endpoints sin modificar código existente

using System;                                             // Para DateTime y excepciones básicas
using Microsoft.AspNetCore.Authorization;                // Para [AllowAnonymous] y políticas de autorización
using Microsoft.AspNetCore.Mvc;                          // Para ControllerBase, IActionResult, atributos HTTP
using System.Threading.Tasks;                            // Para async/await
using Microsoft.Extensions.Logging;                      // Para ILogger y logging estructurado
using Microsoft.Extensions.Configuration;                // Para IConfiguration y acceso a appsettings.json
using ApiGenericaCsharp.Servicios.Abstracciones;              // Para IServicioCrud
using Microsoft.Data.SqlClient;                     // Para SqlException en manejo de errores específicos
using System.Text.Json;

namespace ApiGenericaCsharp.Controllers
{
    /// <summary>
    /// Controlador genérico para operaciones CRUD sobre cualquier tabla de base de datos.
    /// 
    /// Este controlador implementa el patrón API genérica donde:
    /// - Un solo controlador maneja múltiples tablas
    /// - La tabla se especifica como parámetro en la URL: /api/{tabla}
    /// - No requiere crear controladores específicos por cada entidad
    /// - Delega toda la lógica de negocio al servicio correspondiente
    /// 
    /// Ventajas del enfoque genérico:
    /// - Menos código duplicado (un controlador vs N controladores específicos)
    /// - Más fácil de mantener (cambios en un solo lugar afectan todas las tablas)
    /// - Escalable automáticamente (funciona con tablas nuevas sin escribir código adicional)
    /// - Consistente (mismo comportamiento y formato de respuesta para todas las tablas)
    /// - Extensible (agregar nuevos endpoints beneficia automáticamente a todas las tablas)
    /// 
    /// Responsabilidades específicas de este controlador:
    /// - Recibir y validar peticiones HTTP de entrada
    /// - Extraer parámetros de URL y query string de forma segura
    /// - Llamar al servicio apropiado para ejecutar la lógica de negocio
    /// - Convertir respuestas del servicio a formatos HTTP apropiados con códigos de estado correctos
    /// - Manejar errores de forma consistente y devolver respuestas informativas
    /// - Generar logging estructurado para auditoría, debugging y monitoreo
    /// - Proporcionar metadatos útiles en las respuestas para facilitar el uso de la API
    /// </summary>
    [Route("api/{tabla}")]                                // Ruta dinámica: /api/usuarios, /api/productos, etc.
    [ApiController]                                       // Activa validación automática, binding, y comportamientos de API REST
    public class EntidadesController : ControllerBase
    {
        // Dependencias inyectadas - Aplicando DIP (Dependency Inversion Principle)
        // Cada dependencia tiene una responsabilidad específica y bien definida (SRP)
        private readonly IServicioCrud _servicioCrud;           // Para lógica de negocio CRUD y reglas del dominio
        private readonly ILogger<EntidadesController> _logger;  // Para logging estructurado, auditoría y debugging
        private readonly IConfiguration _configuration;         // Para acceso a configuraciones desde appsettings.json

        /// <summary>
        /// Constructor que recibe dependencias mediante inyección automática del contenedor DI.
        /// 
        /// ARQUITECTURA SOLID EN ACCIÓN:
        /// - SRP: Cada dependencia tiene una responsabilidad única y bien definida
        /// - DIP: Dependemos de abstracciones (interfaces), no de implementaciones concretas
        /// - OCP: Podemos cambiar implementaciones sin modificar este controlador
        /// - LSP: Cualquier implementación de las interfaces debe funcionar correctamente aquí
        /// 
        /// CADENA DE DEPENDENCIAS COMPLETA EN LA APLICACIÓN:
        /// EntidadesController → IServicioCrud → IRepositorioLecturaTabla → IProveedorConexion → IConfiguration
        /// 
        /// FLUJO DE INYECCIÓN DE DEPENDENCIAS:
        /// 1. ASP.NET Core necesita crear EntidadesController para manejar petición HTTP entrante
        /// 2. Ve que el constructor requiere 3 parámetros: IServicioCrud, ILogger, IConfiguration  
        /// 3. Para IServicioCrud: busca registro en Program.cs y crea ServicioCrud
        /// 4. ServicioCrud requiere IRepositorioLecturaTabla, entonces crea repositorio según DatabaseProvider
        /// 5. Repositorio requiere IProveedorConexion, entonces crea ProveedorConexion
        /// 6. ProveedorConexion requiere IConfiguration (ya disponible como singleton)
        /// 7. Inyecta toda la cadena automáticamente en este constructor
        /// 8. El controlador queda listo para manejar la petición HTTP
        /// </summary>
        /// <param name="servicioCrud">
        /// Servicio que contiene toda la lógica de negocio para operaciones CRUD.
        /// Se inyecta desde el registro: builder.Services.AddScoped<IServicioCrud, ServicioCrud>()
        /// Aplica DIP: dependemos de la abstracción IServicioCrud, no de la implementación ServicioCrud
        /// </param>
        /// <param name="logger">
        /// Logger específico para este controlador, registrado automáticamente por ASP.NET Core.
        /// Permite logging estructurado con contexto específico de "EntidadesController".
        /// Útil para debugging, auditoría, monitoreo de performance y análisis de uso.
        /// </param>
        /// <param name="configuration">
        /// Configuración de la aplicación que incluye datos de appsettings.json, variables de entorno, secrets.
        /// Registrada automáticamente por ASP.NET Core como singleton.
        /// Se puede usar para leer configuraciones específicas del controlador si es necesario.
        /// </param>
        public EntidadesController(
            IServicioCrud servicioCrud,           // Lógica de negocio y coordinación de operaciones CRUD
            ILogger<EntidadesController> logger,  // Logging estructurado, auditoría y monitoreo de operaciones
            IConfiguration configuration         // Acceso a configuraciones desde appsettings.json y otras fuentes
        )
        {
            // Validaciones defensivas - Guard clauses para prevenir errores de configuración DI
            // En funcionamiento normal de una aplicación correctamente configurada, 
            // estas excepciones nunca deberían lanzarse

            _servicioCrud = servicioCrud ?? throw new ArgumentNullException(
                nameof(servicioCrud),
                "IServicioCrud no fue inyectado correctamente. Verificar registro de servicios en Program.cs"
            );

            _logger = logger ?? throw new ArgumentNullException(
                nameof(logger),
                "ILogger no fue inyectado correctamente. Problema en configuración de logging de ASP.NET Core"
            );

            _configuration = configuration ?? throw new ArgumentNullException(
                nameof(configuration),
                "IConfiguration no fue inyectado correctamente. Problema en configuración base de ASP.NET Core"
            );
        }

        /// <summary>
        /// Endpoint principal para listar registros de cualquier tabla de base de datos.
        /// 
        /// Este es el endpoint núcleo de la API genérica. Permite consultar cualquier tabla
        /// sin necesidad de crear controladores específicos para cada entidad del dominio.
        /// 
        /// Ruta completa: GET /api/{tabla}?esquema={esquema}&limite={limite}
        /// 
        /// Ejemplos de uso en diferentes escenarios:
        /// - GET /api/usuarios → Lista usuarios con configuración por defecto (esquema y límite)
        /// - GET /api/productos?limite=50 → Lista máximo 50 productos del esquema por defecto
        /// - GET /api/pedidos?esquema=ventas&limite=100 → Lista 100 pedidos del esquema "ventas"
        /// - GET /api/empleados?esquema=rrhh → Lista empleados del esquema "rrhh" con límite por defecto
        /// 
        /// Proceso detallado de la petición:
        /// 1. ASP.NET Core recibe la petición HTTP y usa el routing para identificar este método
        /// 2. Extrae automáticamente 'tabla' del path de la URL usando el patrón {tabla}
        /// 3. Extrae 'esquema' y 'limite' del query string si están presentes
        /// 4. Llama a este método con los parámetros extraídos y convertidos a los tipos apropiados
        /// 5. El método ejecuta la lógica de coordinación HTTP → Servicio
        /// 6. Se construye y devuelve la respuesta HTTP apropiada con códigos de estado REST
        /// 
        /// Códigos de respuesta HTTP que puede devolver:
        /// - 200 OK: Datos encontrados y devueltos correctamente con metadatos
        /// - 204 No Content: Consulta exitosa pero la tabla no contiene registros
        /// - 400 Bad Request: Parámetros de entrada inválidos (tabla vacía, límite negativo, etc.)
        /// - 404 Not Found: Tabla no existe en la base de datos o esquema especificado
        /// - 500 Internal Server Error: Error inesperado del sistema (problemas de BD, configuración, etc.)
        /// </summary>
        /// <param name="tabla">
        /// Nombre de la tabla a consultar. Se extrae automáticamente de la URL mediante routing.
        /// Ejemplo: en GET /api/usuarios, el parámetro tabla contendrá "usuarios"
        /// 
        /// Este parámetro es obligatorio (viene del path) y no puede ser null.
        /// El servicio aplicará validaciones adicionales: caracteres permitidos, tablas autorizadas, etc.
        /// </param>
        /// <param name="esquema">
        /// Esquema de la base de datos. Parámetro opcional extraído del query string.
        /// Ejemplo: ?esquema=dbo para SQL Server, ?esquema=public para PostgreSQL
        /// 
        /// Si no se proporciona (null), el servicio aplicará el esquema por defecto
        /// según el proveedor de base de datos configurado y las reglas de negocio.
        /// </param>
        /// <param name="limite">
        /// Límite máximo de registros a devolver. Parámetro opcional extraído del query string.
        /// Ejemplo: ?limite=100 para obtener máximo 100 registros
        /// 
        /// Si no se proporciona (null), el servicio aplicará un límite por defecto
        /// según las reglas de negocio configuradas (típicamente 1000 para evitar consultas masivas).
        /// </param>
        /// <returns>
        /// IActionResult que representa la respuesta HTTP completa. Puede ser:
        /// - Ok() con datos y metadatos estructurados para respuestas exitosas con contenido
        /// - NoContent() para respuestas exitosas sin contenido (tabla vacía)
        /// - BadRequest() para errores de validación de parámetros de entrada
        /// - NotFound() para recursos no encontrados (tabla inexistente)
        /// - StatusCode(500) para errores internos del servidor
        /// 
        /// Todas las respuestas incluyen información estructurada para facilitar el uso de la API.
        /// </returns>
        [AllowAnonymous]                                  // Permite acceso sin autenticación (apropiado para desarrollo)
        //[Authorize]
        [HttpGet]                                        // Responde exclusivamente a peticiones HTTP GET
        public async Task<IActionResult> ListarAsync(
            string tabla,                                 // Del path de la URL: /api/{tabla}
            [FromQuery] string? esquema,                  // Del query string: ?esquema=valor
            [FromQuery] int? limite                       // Del query string: ?limite=valor
        )
        {
            try
            {
                // ==============================================================================
                // FASE 1: LOGGING DE AUDITORÍA Y TRAZABILIDAD
                // ==============================================================================

                // Registrar inicio de operación con todos los parámetros para:
                // - Auditoría de seguridad: rastro completo de quién consulta qué datos
                // - Debugging y troubleshooting: facilitar investigación de problemas
                // - Métricas de uso: estadísticas sobre qué tablas se consultan más frecuentemente
                // - Monitoreo de rendimiento: detectar consultas lentas o patrones problemáticos
                // - Análisis de negocio: entender cómo se usa la API
                _logger.LogInformation(
                    "INICIO consulta - Tabla: {Tabla}, Esquema: {Esquema}, Límite: {Limite}",
                    tabla,                                // Tabla que se está consultando
                    esquema ?? "por defecto",            // Esquema especificado o indicador de valor por defecto
                    limite?.ToString() ?? "por defecto"  // Límite especificado o indicador de valor por defecto
                );

                // ==============================================================================
                // FASE 2: DELEGACIÓN AL SERVICIO (APLICANDO SRP y DIP)
                // ==============================================================================

                // PRINCIPIO SRP APLICADO:
                // El controlador NO contiene lógica de negocio, solo coordina la comunicación HTTP ↔ Servicio
                // Toda la lógica compleja (validaciones, reglas de negocio, transformaciones) se delega al servicio

                // PRINCIPIO DIP APLICADO:
                // Dependemos de IServicioCrud (abstracción), no de ServicioCrud (implementación concreta)
                // Esto permite intercambiar implementaciones sin modificar este controlador

                // El servicio se encarga de:
                // - Aplicar validaciones de reglas de negocio específicas del dominio
                // - Normalizar y validar parámetros según políticas empresariales
                // - Coordinar con el repositorio apropiado para obtener datos técnicos
                // - Aplicar transformaciones post-consulta según reglas del dominio
                // - Manejar errores específicos del contexto de negocio
                var filas = await _servicioCrud.ListarAsync(tabla, esquema, limite);

                // ==============================================================================
                // FASE 3: LOGGING DE RESULTADOS PARA MONITOREO Y ANÁLISIS
                // ==============================================================================

                // Registrar el resultado exitoso de la operación para:
                // - Métricas de rendimiento: qué consultas devuelven más/menos datos
                // - Análisis de patrones de uso: qué tablas son más populares
                // - Debugging: confirmar que la operación se completó correctamente
                // - Auditoría: rastro completo de la operación desde inicio hasta fin
                // - Monitoreo de salud: detectar degradación del servicio
                _logger.LogInformation(
                    "RESULTADO exitoso - Registros obtenidos: {Cantidad} de tabla {Tabla}",
                    filas.Count,    // Cantidad exacta de registros devueltos
                    tabla          // Tabla que fue consultada exitosamente
                );

                // ==============================================================================
                // FASE 4: MANEJO DE CASO SIN DATOS (204 NO CONTENT)
                // ==============================================================================

                // Verificar si la consulta no devolvió registros
                // En REST, se debe distinguir entre:
                // - 404 Not Found: El recurso (tabla) no existe
                // - 204 No Content: El recurso existe pero no contiene datos
                // Esta distinción es importante para que los clientes de la API entiendan la diferencia
                if (filas.Count == 0)
                {
                    // Logging específico para caso sin datos (útil para análisis de patrones de datos)
                    _logger.LogInformation(
                        "SIN DATOS - Tabla {Tabla} consultada exitosamente pero no contiene registros",
                        tabla
                    );

                    // Devolver 204 No Content según estándares REST
                    // Indica que la petición fue exitosa pero no hay contenido que devolver
                    // El cliente puede interpretar esto como "tabla vacía" vs "tabla inexistente" (404)
                    return NoContent();
                }

                // ==============================================================================
                // FASE 5: RESPUESTA EXITOSA CON DATOS Y METADATOS (200 OK)
                // ==============================================================================

                // Construir respuesta enriquecida que incluye:
                // - Los datos solicitados en formato estándar
                // - Metadatos útiles sobre la consulta realizada  
                // - Información de contexto que facilite el uso de la API por parte de los clientes
                // - Estructura consistente que se mantiene para todas las tablas
                return Ok(new
                {
                    // METADATOS DE LA CONSULTA (información contextual útil para el cliente)
                    tabla = tabla,                              // Tabla que fue consultada (confirmación)
                    esquema = esquema ?? "por defecto",         // Esquema usado, con indicador legible para null
                    limite = limite,                            // Límite aplicado (puede ser null si no se especificó)
                    total = filas.Count,                        // Cantidad exacta de registros devueltos

                    // DATOS REALES DE LA CONSULTA
                    datos = filas                               // Lista de registros obtenidos de la base de datos

                    // NOTA: La estructura de 'datos' es List<Dictionary<string, object?>>
                    // Cada elemento de la lista es un registro (fila)
                    // Cada registro es un Dictionary donde la clave es el nombre de columna y el valor es el dato
                });
            }
            // ==============================================================================
            // MANEJO DE ERRORES POR CAPAS (CADA TIPO DE EXCEPCIÓN TIENE TRATAMIENTO ESPECÍFICO)
            // ==============================================================================

            catch (ArgumentException excepcionArgumento)
            {
                // ERRORES DE VALIDACIÓN DE ENTRADA (400 BAD REQUEST)
                // Se lanzan cuando los parámetros de entrada no cumplen reglas básicas de validación:
                // - Tabla con nombre vacío o solo espacios en blanco
                // - Límite con valor negativo o cero
                // - Caracteres inválidos en nombres de tabla o esquema
                // - Violación de reglas de formato específicas del dominio

                // Logging como Warning porque no es error del sistema, sino entrada incorrecta del usuario
                // No requiere investigación técnica urgente, pero es útil para análisis de patrones de uso
                _logger.LogWarning(
                    "ERROR DE VALIDACIÓN - Petición rechazada - Tabla: {Tabla}, Error: {Mensaje}",
                    tabla,                          // Tabla que se intentó consultar
                    excepcionArgumento.Message      // Mensaje específico de la validación que falló
                );

                // Respuesta 400 Bad Request con información estructurada para corregir el problema
                // Incluye detalles específicos para que el cliente pueda ajustar su petición
                return BadRequest(new
                {
                    estado = 400,                                    // Código de estado HTTP explícito
                    mensaje = "Parámetros de entrada inválidos.",    // Mensaje general para el usuario final
                    detalle = excepcionArgumento.Message,            // Detalle específico del problema de validación
                    tabla = tabla                                    // Contexto: tabla que se intentó consultar
                });
            }
            catch (InvalidOperationException excepcionOperacion)
            {
                // ERRORES DE OPERACIÓN/RECURSOS NO ENCONTRADOS (404 NOT FOUND)
                // Se lanzan cuando hay problemas operacionales que no son culpa del usuario:
                // - La tabla especificada no existe en la base de datos
                // - El esquema especificado no existe
                // - Problemas de permisos de acceso a la tabla
                // - Problemas de conectividad con la base de datos
                // - Configuración incorrecta de cadenas de conexión

                // Logging como Error porque puede requerir investigación técnica
                // Estos problemas podrían indicar issues de configuración, red, o base de datos
                _logger.LogError(excepcionOperacion,
                    "ERROR DE OPERACIÓN - Fallo en consulta - Tabla: {Tabla}, Error: {Mensaje}",
                    tabla,                              // Tabla que se intentó consultar
                    excepcionOperacion.Message          // Mensaje específico del error operacional
                );

                // Respuesta 404 Not Found para recursos que no existen o no son accesibles
                // Proporciona información útil para que el cliente entienda qué salió mal
                return NotFound(new
                {
                    estado = 404,                                      // Código de estado HTTP explícito
                    mensaje = "El recurso solicitado no fue encontrado.", // Mensaje general user-friendly
                    detalle = excepcionOperacion.Message,              // Detalle específico (ej: "tabla no existe")
                    tabla = tabla,                                     // Contexto: tabla que se buscó
                    sugerencia = "Verifique que la tabla y el esquema existan en la base de datos" // Guía para resolver
                });
            }
            catch (UnauthorizedAccessException excepcionAcceso)
            {
                _logger.LogWarning(
                    "ACCESO DENEGADO - Tabla restringida: {Tabla}, Error: {Mensaje}",
                    tabla,
                    excepcionAcceso.Message
                );

                return StatusCode(403, new
                {
                    estado = 403,
                    mensaje = "Acceso denegado.",
                    detalle = excepcionAcceso.Message,
                    tabla = tabla
                });
            }
            catch (Exception excepcionGeneral)
            {
                // ERRORES INESPERADOS/CRÍTICOS (500 INTERNAL SERVER ERROR)
                // Cualquier error que no se manejó específicamente en las categorías anteriores:
                // - Errores de programación no anticipados (NullReference, etc.)
                // - Problemas de infraestructura (falta de memoria, disco lleno, etc.)
                // - Errores de configuración críticos del sistema
                // - Fallas de red o servicios externos inesperadas
                // - Bugs en el código que requieren investigación inmediata

                // Logging como Error crítico para investigación técnica inmediata
                // Se incluye la excepción completa con stack trace para debugging
                _logger.LogError(excepcionGeneral,
                    "ERROR CRÍTICO - Falla inesperada en consulta - Tabla: {Tabla}",
                    tabla              // Tabla donde ocurrió el error crítico
                );

                // Construir mensaje de error más informativo para debugging
                var detalleError = new System.Text.StringBuilder();
                detalleError.AppendLine($"Tipo de error: {excepcionGeneral.GetType().Name}");
                detalleError.AppendLine($"Mensaje: {excepcionGeneral.Message}");

                if (excepcionGeneral.InnerException != null)
                {
                    detalleError.AppendLine($"Error interno: {excepcionGeneral.InnerException.Message}");
                }

                // Agregar información del stack trace (solo las primeras 3 líneas para no saturar)
                if (!string.IsNullOrEmpty(excepcionGeneral.StackTrace))
                {
                    var stackLines = excepcionGeneral.StackTrace.Split('\n').Take(3);
                    detalleError.AppendLine("Stack trace:");
                    foreach (var line in stackLines)
                    {
                        detalleError.AppendLine($"  {line.Trim()}");
                    }
                }

                // Respuesta 500 Internal Server Error con detalles útiles
                return StatusCode(500, new
                {
                    estado = 500,                                        // Código de estado HTTP explícito
                    mensaje = "Error interno del servidor al consultar tabla.",
                    tabla = tabla,                                       // Contexto de la operación
                    tipoError = excepcionGeneral.GetType().Name,        // Tipo de excepción
                    detalle = excepcionGeneral.Message,                 // Mensaje principal
                    detalleCompleto = detalleError.ToString(),          // Desglose completo
                    errorInterno = excepcionGeneral.InnerException?.Message,
                    timestamp = DateTime.UtcNow,                        // Timestamp para correlación
                    sugerencia = "Revise los logs del servidor para más detalles o contacte al administrador."
                });
            }
        }

        /// <summary>
        /// Obtiene registros filtrados por un valor de clave específico.
        /// 
        /// Ruta: GET /api/{tabla}/{nombreClave}/{valor}
        /// Ejemplo: GET /api/factura/numero/1
        /// Con esquema: GET /api/factura/numero/1?esquema=ventas
        /// </summary>
        //[AllowAnonymous]
        //[Authorize]
        [HttpGet("{nombreClave}/{valor}")]
        public async Task<IActionResult> ObtenerPorClaveAsync(
        string tabla,           // Del path: /api/{tabla}
        string nombreClave,     // Del path: /{nombreClave}
        string valor,           // Del path: /{valor}
        [FromQuery] string? esquema = null  // Del query string: ?esquema=valor
        )
        {
            try
            {
                // LOGGING DE AUDITORÍA
                _logger.LogInformation(
                    "INICIO filtrado - Tabla: {Tabla}, Esquema: {Esquema}, Clave: {Clave}, Valor: {Valor}",
                    tabla, esquema ?? "por defecto", nombreClave, valor
                );

                // DELEGACIÓN AL SERVICIO (aplicando SRP y DIP)
                var filas = await _servicioCrud.ObtenerPorClaveAsync(tabla, esquema, nombreClave, valor);

                // LOGGING DE RESULTADOS
                _logger.LogInformation(
                    "RESULTADO filtrado - {Cantidad} registros encontrados para {Clave}={Valor} en {Tabla}",
                    filas.Count, nombreClave, valor, tabla
                );

                // MANEJO DE CASO SIN DATOS (404 si no se encuentran registros)
                if (filas.Count == 0)
                {
                    return NotFound(new
                    {
                        estado = 404,
                        mensaje = "No se encontraron registros",
                        detalle = $"No se encontró ningún registro con {nombreClave} = {valor} en la tabla {tabla}",
                        tabla = tabla,
                        esquema = esquema ?? "por defecto",
                        filtro = $"{nombreClave} = {valor}"
                    });
                }

                // RESPUESTA EXITOSA CON METADATOS
                return Ok(new
                {
                    tabla = tabla,
                    esquema = esquema ?? "por defecto",
                    filtro = $"{nombreClave} = {valor}",
                    total = filas.Count,
                    datos = filas
                });
            }
            catch (UnauthorizedAccessException excepcionAcceso)
            {
                return StatusCode(403, new
                {
                    estado = 403,
                    mensaje = "Acceso denegado.",
                    detalle = excepcionAcceso.Message,
                    tabla = tabla
                });
            }
            catch (ArgumentException excepcionArgumento)
            {
                return BadRequest(new
                {
                    estado = 400,
                    mensaje = "Parámetros inválidos.",
                    detalle = excepcionArgumento.Message,
                    tabla = tabla
                });
            }
            catch (InvalidOperationException excepcionOperacion)
            {
                return NotFound(new
                {
                    estado = 404,
                    mensaje = "Recurso no encontrado.",
                    detalle = excepcionOperacion.Message,
                    tabla = tabla
                });
            }
            catch (Exception excepcionGeneral)
            {
                _logger.LogError(excepcionGeneral,
                    "ERROR CRÍTICO - Falla en filtrado - Tabla: {Tabla}, Clave: {Clave}, Valor: {Valor}",
                    tabla, nombreClave, valor
                );

                var detalleError = new System.Text.StringBuilder();
                detalleError.AppendLine($"Tipo: {excepcionGeneral.GetType().Name}");
                detalleError.AppendLine($"Mensaje: {excepcionGeneral.Message}");
                if (excepcionGeneral.InnerException != null)
                    detalleError.AppendLine($"Error interno: {excepcionGeneral.InnerException.Message}");

                return StatusCode(500, new
                {
                    estado = 500,
                    mensaje = "Error interno del servidor al filtrar registros.",
                    tabla = tabla,
                    filtro = $"{nombreClave} = {valor}",
                    tipoError = excepcionGeneral.GetType().Name,
                    detalle = excepcionGeneral.Message,
                    detalleCompleto = detalleError.ToString(),
                    errorInterno = excepcionGeneral.InnerException?.Message,
                    timestamp = DateTime.UtcNow,
                    sugerencia = "Revise los logs para más detalles."
                });
            }
        }


        /// <summary>
        /// Crea un nuevo registro en la tabla especificada.
        /// 
        /// Ruta: POST /api/{tabla}
        /// Ejemplo: POST /api/usuario con body JSON
        /// </summary>
        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> CrearAsync(
            string tabla,                                           // Del path: /api/{tabla}
            [FromBody] Dictionary<string, object?> datosEntidad,   // Del body: JSON con datos
            [FromQuery] string? esquema = null,                    // Query param: ?esquema=valor
            [FromQuery] string? camposEncriptar = null             // Query param: ?camposEncriptar=password,pin
        )
        {
            try
            {
                // LOGGING DE AUDITORÍA
                _logger.LogInformation(
                    "INICIO creación - Tabla: {Tabla}, Esquema: {Esquema}, Campos a encriptar: {CamposEncriptar}",
                    tabla, esquema ?? "por defecto", camposEncriptar ?? "ninguno"
                );

                // VALIDACIÓN TEMPRANA EN CONTROLADOR
                if (datosEntidad == null || !datosEntidad.Any())
                {
                    return BadRequest(new
                    {
                        estado = 400,
                        mensaje = "Los datos de la entidad no pueden estar vacíos.",
                        tabla = tabla
                    });
                }

                // CONVERSIÓN DE JsonElement A TIPOS NATIVOS
                var datosConvertidos = new Dictionary<string, object?>();
                foreach (var kvp in datosEntidad)
                {
                    if (kvp.Value is JsonElement elemento)
                    {
                        datosConvertidos[kvp.Key] = ConvertirJsonElement(elemento);
                    }
                    else
                    {
                        datosConvertidos[kvp.Key] = kvp.Value;
                    }
                }

                // DELEGACIÓN AL SERVICIO (aplicando SRP y DIP)
                bool creado = await _servicioCrud.CrearAsync(tabla, esquema, datosConvertidos, camposEncriptar);

                if (creado)
                {
                    _logger.LogInformation(
                        "ÉXITO creación - Registro creado en tabla {Tabla}",
                        tabla
                    );

                    return Ok(new
                    {
                        estado = 200,
                        mensaje = "Registro creado exitosamente.",
                        tabla = tabla,
                        esquema = esquema ?? "por defecto"
                    });
                }
                else
                {
                    return StatusCode(500, new
                    {
                        estado = 500,
                        mensaje = "No se pudo crear el registro.",
                        tabla = tabla
                    });
                }
            }
            catch (UnauthorizedAccessException excepcionAcceso)
            {
                return StatusCode(403, new
                {
                    estado = 403,
                    mensaje = "Acceso denegado.",
                    detalle = excepcionAcceso.Message,
                    tabla = tabla
                });
            }
            catch (ArgumentException excepcionArgumento)
            {
                return BadRequest(new
                {
                    estado = 400,
                    mensaje = "Datos inválidos.",
                    detalle = excepcionArgumento.Message,
                    tabla = tabla
                });
            }
            catch (InvalidOperationException excepcionOperacion)
            {
                return StatusCode(500, new
                {
                    estado = 500,
                    mensaje = "Error en la operación.",
                    detalle = excepcionOperacion.Message,
                    tabla = tabla
                });
            }
            catch (Exception excepcionGeneral)
            {
                _logger.LogError(excepcionGeneral,
                    "ERROR CRÍTICO - Falla en creación - Tabla: {Tabla}",
                    tabla
                );

                var detalleError = new System.Text.StringBuilder();
                detalleError.AppendLine($"Tipo: {excepcionGeneral.GetType().Name}");
                detalleError.AppendLine($"Mensaje: {excepcionGeneral.Message}");
                if (excepcionGeneral.InnerException != null)
                    detalleError.AppendLine($"Error interno: {excepcionGeneral.InnerException.Message}");

                return StatusCode(500, new
                {
                    estado = 500,
                    mensaje = "Error interno del servidor al crear registro.",
                    tabla = tabla,
                    tipoError = excepcionGeneral.GetType().Name,
                    detalle = excepcionGeneral.Message,
                    detalleCompleto = detalleError.ToString(),
                    errorInterno = excepcionGeneral.InnerException?.Message,
                    timestamp = DateTime.UtcNow,
                    sugerencia = "Revise los logs para más detalles."
                });
            }
        }

        /// <summary>
        /// Actualiza un registro existente en la tabla especificada.
        /// 
        /// Ruta: PUT /api/{tabla}/{nombreClave}/{valorClave}
        /// Ejemplo: PUT /api/usuario/email/juan@test.com con body JSON
        /// Con encriptación: PUT /api/usuario/email/juan@test.com?camposEncriptar=contrasena
        /// </summary>
        [AllowAnonymous]
        [HttpPut("{nombreClave}/{valorClave}")]
        public async Task<IActionResult> ActualizarAsync(
            string tabla,                                           // Del path: /api/{tabla}
            string nombreClave,                                     // Del path: /{nombreClave}
            string valorClave,                                      // Del path: /{valorClave}
            [FromBody] Dictionary<string, object?> datosEntidad,   // Del body: JSON con nuevos datos
            [FromQuery] string? esquema = null,                    // Query param: ?esquema=valor
            [FromQuery] string? camposEncriptar = null             // Query param: ?camposEncriptar=password,pin
        )
        {
            try
            {
                // LOGGING DE AUDITORÍA
                _logger.LogInformation(
                    "INICIO actualización - Tabla: {Tabla}, Clave: {Clave}={Valor}, Esquema: {Esquema}, Campos a encriptar: {CamposEncriptar}",
                    tabla, nombreClave, valorClave, esquema ?? "por defecto", camposEncriptar ?? "ninguno"
                );

                // VALIDACIÓN TEMPRANA EN CONTROLADOR
                if (datosEntidad == null || !datosEntidad.Any())
                {
                    return BadRequest(new
                    {
                        estado = 400,
                        mensaje = "Los datos de actualización no pueden estar vacíos.",
                        tabla = tabla,
                        filtro = $"{nombreClave} = {valorClave}"
                    });
                }

                // CONVERSIÓN DE JsonElement A TIPOS NATIVOS
                // Reutilizar la misma lógica que en CrearAsync
                var datosConvertidos = new Dictionary<string, object?>();
                foreach (var kvp in datosEntidad)
                {
                    if (kvp.Value is JsonElement elemento)
                    {
                        datosConvertidos[kvp.Key] = ConvertirJsonElement(elemento);
                    }
                    else
                    {
                        datosConvertidos[kvp.Key] = kvp.Value;
                    }
                }

                // DELEGACIÓN AL SERVICIO (aplicando SRP y DIP)
                int filasAfectadas = await _servicioCrud.ActualizarAsync(
                    tabla, esquema, nombreClave, valorClave, datosConvertidos, camposEncriptar
                );

                // EVALUAR RESULTADO SEGÚN FILAS AFECTADAS
                if (filasAfectadas > 0)
                {
                    _logger.LogInformation(
                        "ÉXITO actualización - {FilasAfectadas} filas actualizadas en tabla {Tabla} WHERE {Clave}={Valor}",
                        filasAfectadas, tabla, nombreClave, valorClave
                    );

                    return Ok(new
                    {
                        estado = 200,
                        mensaje = "Registro actualizado exitosamente.",
                        tabla = tabla,
                        esquema = esquema ?? "por defecto",
                        filtro = $"{nombreClave} = {valorClave}",
                        filasAfectadas = filasAfectadas,
                        camposEncriptados = camposEncriptar ?? "ninguno"
                    });
                }
                else
                {
                    // Si no se afectaron filas, significa que no se encontró el registro
                    return NotFound(new
                    {
                        estado = 404,
                        mensaje = "No se encontró el registro a actualizar.",
                        detalle = $"No existe un registro con {nombreClave} = {valorClave} en la tabla {tabla}",
                        tabla = tabla,
                        filtro = $"{nombreClave} = {valorClave}"
                    });
                }
            }
            catch (UnauthorizedAccessException excepcionAcceso)
            {
                return StatusCode(403, new
                {
                    estado = 403,
                    mensaje = "Acceso denegado.",
                    detalle = excepcionAcceso.Message,
                    tabla = tabla
                });
            }
            catch (ArgumentException excepcionArgumento)
            {
                return BadRequest(new
                {
                    estado = 400,
                    mensaje = "Parámetros inválidos.",
                    detalle = excepcionArgumento.Message,
                    tabla = tabla,
                    filtro = $"{nombreClave} = {valorClave}"
                });
            }
            catch (InvalidOperationException excepcionOperacion)
            {
                return StatusCode(500, new
                {
                    estado = 500,
                    mensaje = "Error en la operación de actualización.",
                    detalle = excepcionOperacion.Message,
                    tabla = tabla,
                    filtro = $"{nombreClave} = {valorClave}"
                });
            }
            catch (Exception excepcionGeneral)
            {
                _logger.LogError(excepcionGeneral,
                    "ERROR CRÍTICO - Falla en actualización - Tabla: {Tabla}, Clave: {Clave}={Valor}",
                    tabla, nombreClave, valorClave
                );

                var detalleError = new System.Text.StringBuilder();
                detalleError.AppendLine($"Tipo: {excepcionGeneral.GetType().Name}");
                detalleError.AppendLine($"Mensaje: {excepcionGeneral.Message}");
                if (excepcionGeneral.InnerException != null)
                    detalleError.AppendLine($"Error interno: {excepcionGeneral.InnerException.Message}");

                return StatusCode(500, new
                {
                    estado = 500,
                    mensaje = "Error interno del servidor al actualizar registro.",
                    tabla = tabla,
                    filtro = $"{nombreClave} = {valorClave}",
                    tipoError = excepcionGeneral.GetType().Name,
                    detalle = excepcionGeneral.Message,
                    detalleCompleto = detalleError.ToString(),
                    errorInterno = excepcionGeneral.InnerException?.Message,
                    timestamp = DateTime.UtcNow,
                    sugerencia = "Revise los logs para más detalles."
                });
            }
        }


        /// <summary>
        /// Elimina un registro existente de la tabla especificada.
        /// 
        /// Ruta: DELETE /api/{tabla}/{nombreClave}/{valorClave}
        /// Ejemplo: DELETE /api/producto/codigo/PRD001
        /// </summary>
        [AllowAnonymous]
        [HttpDelete("{nombreClave}/{valorClave}")]
        public async Task<IActionResult> EliminarAsync(
            string tabla,                                          // Del path: /api/{tabla}
            string nombreClave,                                    // Del path: /{nombreClave}
            string valorClave,                                     // Del path: /{valorClave}
            [FromQuery] string? esquema = null                     // Query param: ?esquema=valor
        )
        {
            try
            {
                // LOGGING DE AUDITORÍA
                _logger.LogInformation(
                    "INICIO eliminación - Tabla: {Tabla}, Clave: {Clave}={Valor}, Esquema: {Esquema}",
                    tabla, nombreClave, valorClave, esquema ?? "por defecto"
                );

                // DELEGACIÓN AL SERVICIO (aplicando SRP y DIP)
                int filasEliminadas = await _servicioCrud.EliminarAsync(
                    tabla, esquema, nombreClave, valorClave
                );

                // EVALUAR RESULTADO SEGÚN FILAS ELIMINADAS
                if (filasEliminadas > 0)
                {
                    _logger.LogInformation(
                        "ÉXITO eliminación - {FilasEliminadas} filas eliminadas de tabla {Tabla} WHERE {Clave}={Valor}",
                        filasEliminadas, tabla, nombreClave, valorClave
                    );

                    return Ok(new
                    {
                        estado = 200,
                        mensaje = "Registro eliminado exitosamente.",
                        tabla = tabla,
                        esquema = esquema ?? "por defecto",
                        filtro = $"{nombreClave} = {valorClave}",
                        filasEliminadas = filasEliminadas
                    });
                }
                else
                {
                    // Si no se eliminaron filas, significa que no se encontró el registro
                    return NotFound(new
                    {
                        estado = 404,
                        mensaje = "No se encontró el registro a eliminar.",
                        detalle = $"No existe un registro con {nombreClave} = {valorClave} en la tabla {tabla}",
                        tabla = tabla,
                        filtro = $"{nombreClave} = {valorClave}"
                    });
                }
            }
            catch (UnauthorizedAccessException excepcionAcceso)
            {
                return StatusCode(403, new
                {
                    estado = 403,
                    mensaje = "Acceso denegado.",
                    detalle = excepcionAcceso.Message,
                    tabla = tabla
                });
            }
            catch (ArgumentException excepcionArgumento)
            {
                return BadRequest(new
                {
                    estado = 400,
                    mensaje = "Parámetros inválidos.",
                    detalle = excepcionArgumento.Message,
                    tabla = tabla,
                    filtro = $"{nombreClave} = {valorClave}"
                });
            }
            catch (InvalidOperationException excepcionOperacion)
            {
                // Manejo específico para restricciones de clave foránea
                if (excepcionOperacion.InnerException is SqlException sqlEx &&
                    sqlEx.Number == 547)
                {
                    return Conflict(new
                    {
                        estado = 409,
                        mensaje = "No se puede eliminar el registro.",
                        detalle = "El registro está siendo referenciado por otros datos (restricción de clave foránea).",
                        tabla = tabla,
                        filtro = $"{nombreClave} = {valorClave}"
                    });
                }

                return StatusCode(500, new
                {
                    estado = 500,
                    mensaje = "Error en la operación de eliminación.",
                    detalle = excepcionOperacion.Message,
                    tabla = tabla,
                    filtro = $"{nombreClave} = {valorClave}"
                });
            }
            catch (Exception excepcionGeneral)
            {
                _logger.LogError(excepcionGeneral,
                    "ERROR CRÍTICO - Falla en eliminación - Tabla: {Tabla}, Clave: {Clave}={Valor}",
                    tabla, nombreClave, valorClave
                );

                var detalleError = new System.Text.StringBuilder();
                detalleError.AppendLine($"Tipo: {excepcionGeneral.GetType().Name}");
                detalleError.AppendLine($"Mensaje: {excepcionGeneral.Message}");
                if (excepcionGeneral.InnerException != null)
                    detalleError.AppendLine($"Error interno: {excepcionGeneral.InnerException.Message}");

                return StatusCode(500, new
                {
                    estado = 500,
                    mensaje = "Error interno del servidor al eliminar registro.",
                    tabla = tabla,
                    filtro = $"{nombreClave} = {valorClave}",
                    tipoError = excepcionGeneral.GetType().Name,
                    detalle = excepcionGeneral.Message,
                    detalleCompleto = detalleError.ToString(),
                    errorInterno = excepcionGeneral.InnerException?.Message,
                    timestamp = DateTime.UtcNow,
                    sugerencia = "Revise los logs para más detalles."
                });
            }
        }


        /// <summary>
        /// Endpoint de información sobre el controlador y sus capacidades actuales.
        /// 
        /// Ruta: GET /api/info
        /// 
        /// Este endpoint es útil para:
        /// - Documentación dinámica de la API (self-describing API)
        /// - Verificar que el controlador está funcionando correctamente (health check básico)
        /// - Obtener ejemplos de uso para desarrolladores que consumen la API
        /// - Debugging y troubleshooting durante desarrollo
        /// - Descubrimiento de funcionalidades disponibles
        /// </summary>
        /// <returns>
        /// Información estructurada sobre el controlador incluyendo:
        /// - Metadatos del controlador (nombre, versión, descripción)
        /// - Lista de endpoints disponibles con descripciones
        /// - Ejemplos prácticos de uso
        /// - Enlaces útiles para navegación
        /// </returns>
        [AllowAnonymous]                                  // Acceso público para facilitar descubrimiento
        [HttpGet]                                         // Responde a peticiones GET
        [Route("api/info")]                               // Ruta específica que no interfiere con el patrón {tabla}
        public IActionResult ObtenerInformacion()
        {
            return Ok(new
            {
                // METADATOS DEL CONTROLADOR
                controlador = "EntidadesController",
                version = "1.0",
                descripcion = "Controlador genérico para consultar tablas de base de datos",

                // ENDPOINTS DISPONIBLES con descripción clara de funcionalidad
                endpoints = new[]
                {
                   "GET /api/{tabla} - Lista registros de una tabla",
                   "GET /api/{tabla}?esquema={esquema} - Lista con esquema específico",
                   "GET /api/{tabla}?limite={numero} - Lista con límite de registros",
                   "GET /api/info - Muestra esta información"
               },

                // EJEMPLOS PRÁCTICOS DE USO para facilitar adopción
                ejemplos = new[]
                {
                   "GET /api/usuarios",
                   "GET /api/productos?esquema=ventas",
                   "GET /api/clientes?limite=50",
                   "GET /api/pedidos?esquema=ventas&limite=100"
               }
            });
        }

        /// <summary>
        /// Endpoint de bienvenida en la raíz de la aplicación.
        /// 
        /// Ruta: GET /
        /// 
        /// Proporciona información de bienvenida y navegación para usuarios
        /// que accedan directamente a la URL base de la API sin conocimiento previo.
        /// Funciona como "landing page" de la API con enlaces útiles.
        /// </summary>
        /// <returns>
        /// Información de bienvenida estructurada incluyendo:
        /// - Mensaje de bienvenida y descripción general
        /// - Enlaces de navegación a recursos útiles
        /// - Guía rápida de uso básico
        /// - Metadatos del servidor (versión, timestamp)
        /// </returns>
        [AllowAnonymous]                                  // Acceso público para bienvenida
        [HttpGet("/")]                                    // Mapea específicamente a la ruta raíz de la aplicación
        public IActionResult Inicio()
        {
            return Ok(new
            {
                // INFORMACIÓN DE BIENVENIDA
                Mensaje = "Bienvenido a la API Genérica en C#",
                Version = "1.0",
                Descripcion = "API genérica para operaciones CRUD sobre cualquier tabla de base de datos",
                Documentacion = "Para más detalles, visita /swagger",
                FechaServidor = DateTime.UtcNow,          // UTC para consistencia global y evitar problemas de zona horaria

                // ENLACES ÚTILES PARA NAVEGACIÓN
                Enlaces = new
                {
                    Swagger = "/swagger",                 // Documentación interactiva completa
                    Info = "/api/info",                   // Información específica del controlador
                    EjemploTabla = "/api/MiTabla"        // Ejemplo de cómo usar el endpoint principal
                },

                // GUÍA RÁPIDA DE USO para primeros pasos
                Uso = new[]
                {
                   "GET /api/{tabla} - Lista registros de una tabla",
                   "GET /api/{tabla}?limite=50 - Lista con límite específico",
                   "GET /api/{tabla}?esquema=dbo - Lista con esquema específico"
               }
            });
        }

        /// <summary>
        /// Convierte un JsonElement al tipo nativo de .NET correspondiente.
        /// 
        /// PROBLEMA TÉCNICO:
        /// ASP.NET Core usa System.Text.Json para deserializar peticiones HTTP JSON.
        /// Los valores JSON se convierten automáticamente en objetos JsonElement.
        /// Sin embargo, SQL Server requiere tipos nativos específicos de .NET para parámetros.
        /// 
        /// CONVERSIONES REALIZADAS:
        /// JSON string "texto" → C# string
        /// JSON number 123 → C# int (si cabe) o double (si es decimal/grande)
        /// JSON true/false → C# bool
        /// JSON null → C# null
        /// JSON object/array → C# string (serializado como texto)
        /// 
        /// Esta conversión es esencial para que los parámetros SQL funcionen correctamente
        /// sin generar errores de tipo "No hay ninguna asignación de tipo de objeto".
        /// </summary>
        /// <param name="elemento">JsonElement deserializado desde el body de la petición HTTP</param>
        /// <returns>
        /// Valor convertido al tipo nativo apropiado para SQL Server:
        /// - string para texto
        /// - int para números enteros
        /// - double para números decimales
        /// - bool para valores booleanos
        /// - null para valores nulos
        /// </returns>
        private object? ConvertirJsonElement(JsonElement elemento)
        {
            // Usar pattern matching para determinar tipo JSON y convertir apropiadamente
            return elemento.ValueKind switch
            {
                // JSON "texto" → C# string
                // Ejemplo: {"nombre": "Juan"} → nombre = "Juan" (string)
                JsonValueKind.String => elemento.GetString(),

                // JSON number → C# int o double
                // Estrategia: intentar int primero, si no cabe usar double
                // Ejemplo: {"edad": 25} → edad = 25 (int)
                // Ejemplo: {"precio": 99.99} → precio = 99.99 (double)
                JsonValueKind.Number => elemento.TryGetInt32(out int intValue)
                    ? intValue           // Número entero válido para int
                    : elemento.GetDouble(),  // Número decimal o muy grande

                // JSON booleanos → C# bool
                // Ejemplo: {"activo": true} → activo = true (bool)
                JsonValueKind.True => true,
                JsonValueKind.False => false,

                // JSON null → C# null (compatible con campos nullable de SQL)
                // Ejemplo: {"descripcion": null} → descripcion = null
                JsonValueKind.Null => null,

                // CASOS COMPLEJOS: objetos y arrays JSON → string serializado
                // Esto permite almacenar JSON complejo como texto en campos VARCHAR
                // Ejemplo: {"config": {"tema": "dark"}} → config = "{\"tema\":\"dark\"}"
                JsonValueKind.Object => elemento.GetRawText(),
                JsonValueKind.Array => elemento.GetRawText(),

                // FALLBACK SEGURO: cualquier otro caso → string
                // Garantiza que siempre devolvemos algo utilizable
                _ => elemento.ToString()
            };
        }

        /// <summary>
        /// Verifica las credenciales de un usuario comparando contraseña con hash almacenado.
        /// 
        /// Ruta: POST /api/{tabla}/verificar-contrasena
        /// Ejemplo: POST /api/usuario/verificar-contrasena
        /// </summary>
        [AllowAnonymous]
        [HttpPost("verificar-contrasena")]
        public async Task<IActionResult> VerificarContrasenaAsync(
            string tabla,                                                    // Del path: /api/{tabla}
            [FromBody] Dictionary<string, object?> datos,                    // Del body: JSON con parámetros
            [FromQuery] string? esquema = null                               // Query param: ?esquema=valor
        )
        {
            try
            {
                // LOGGING DE AUDITORÍA DE AUTENTICACIÓN
                _logger.LogInformation(
                    "INICIO verificación credenciales - Tabla: {Tabla}, Esquema: {Esquema}",
                    tabla, esquema ?? "por defecto"
                );

                // VALIDACIÓN TEMPRANA DE PARÁMETROS REQUERIDOS
                if (datos == null || !datos.Any())
                {
                    return BadRequest(new
                    {
                        estado = 400,
                        mensaje = "Los parámetros de verificación no pueden estar vacíos.",
                        tabla = tabla
                    });
                }

                // CONVERSIÓN DE JsonElement A TIPOS NATIVOS
                var datosConvertidos = new Dictionary<string, object?>();
                foreach (var kvp in datos)
                {
                    if (kvp.Value is JsonElement elemento)
                    {
                        datosConvertidos[kvp.Key] = ConvertirJsonElement(elemento);
                    }
                    else
                    {
                        datosConvertidos[kvp.Key] = kvp.Value;
                    }
                }

                // VALIDACIÓN DE PARÁMETROS ESPECÍFICOS PARA VERIFICACIÓN
                var parametrosRequeridos = new[] { "campoUsuario", "campoContrasena", "valorUsuario", "valorContrasena" };
                foreach (var parametro in parametrosRequeridos)
                {
                    if (!datosConvertidos.ContainsKey(parametro) ||
                        string.IsNullOrWhiteSpace(datosConvertidos[parametro]?.ToString()))
                    {
                        return BadRequest(new
                        {
                            estado = 400,
                            mensaje = $"El parámetro '{parametro}' es requerido.",
                            tabla = tabla,
                            parametrosRequeridos = parametrosRequeridos
                        });
                    }
                }

                // EXTRAER PARÁMETROS ESPECÍFICOS PARA VERIFICACIÓN
                string campoUsuario = datosConvertidos["campoUsuario"]?.ToString() ?? "";
                string campoContrasena = datosConvertidos["campoContrasena"]?.ToString() ?? "";
                string valorUsuario = datosConvertidos["valorUsuario"]?.ToString() ?? "";
                string valorContrasena = datosConvertidos["valorContrasena"]?.ToString() ?? "";

                // LOGGING DE INTENTO DE AUTENTICACIÓN (sin incluir contraseña por seguridad)
                _logger.LogInformation(
                    "Verificando credenciales - Usuario: {Usuario}, Tabla: {Tabla}",
                    valorUsuario, tabla
                );

                // DELEGACIÓN AL SERVICIO (aplicando SRP y DIP)
                var (codigo, mensaje) = await _servicioCrud.VerificarContrasenaAsync(
                    tabla, esquema, campoUsuario, campoContrasena, valorUsuario, valorContrasena
                );

                // EVALUAR RESULTADO Y DEVOLVER RESPUESTA HTTP APROPIADA
                switch (codigo)
                {
                    case 200:
                        _logger.LogInformation(
                            "ÉXITO autenticación - Usuario {Usuario} autenticado correctamente en tabla {Tabla}",
                            valorUsuario, tabla
                        );

                        return Ok(new
                        {
                            estado = 200,
                            mensaje = "Credenciales verificadas exitosamente.",
                            tabla = tabla,
                            usuario = valorUsuario,
                            autenticado = true
                        });

                    case 404:
                        _logger.LogWarning(
                            "FALLO autenticación - Usuario {Usuario} no encontrado en tabla {Tabla}",
                            valorUsuario, tabla
                        );

                        return NotFound(new
                        {
                            estado = 404,
                            mensaje = "Usuario no encontrado.",
                            tabla = tabla,
                            usuario = valorUsuario,
                            autenticado = false
                        });

                    case 401:
                        _logger.LogWarning(
                            "FALLO autenticación - Contraseña incorrecta para usuario {Usuario} en tabla {Tabla}",
                            valorUsuario, tabla
                        );

                        return Unauthorized(new
                        {
                            estado = 401,
                            mensaje = "Contraseña incorrecta.",
                            tabla = tabla,
                            usuario = valorUsuario,
                            autenticado = false
                        });

                    default:
                        return StatusCode(500, new
                        {
                            estado = 500,
                            mensaje = "Error durante la verificación de credenciales.",
                            detalle = mensaje,
                            tabla = tabla
                        });
                }
            }
            catch (UnauthorizedAccessException excepcionAcceso)
            {
                return StatusCode(403, new
                {
                    estado = 403,
                    mensaje = "Acceso denegado.",
                    detalle = excepcionAcceso.Message,
                    tabla = tabla
                });
            }
            catch (ArgumentException excepcionArgumento)
            {
                return BadRequest(new
                {
                    estado = 400,
                    mensaje = "Parámetros inválidos.",
                    detalle = excepcionArgumento.Message,
                    tabla = tabla
                });
            }
            catch (Exception excepcionGeneral)
            {
                _logger.LogError(excepcionGeneral,
                    "ERROR CRÍTICO - Falla en verificación de credenciales - Tabla: {Tabla}",
                    tabla
                );

                var detalleError = new System.Text.StringBuilder();
                detalleError.AppendLine($"Tipo: {excepcionGeneral.GetType().Name}");
                detalleError.AppendLine($"Mensaje: {excepcionGeneral.Message}");
                if (excepcionGeneral.InnerException != null)
                    detalleError.AppendLine($"Error interno: {excepcionGeneral.InnerException.Message}");

                return StatusCode(500, new
                {
                    estado = 500,
                    mensaje = "Error interno del servidor al verificar credenciales.",
                    tabla = tabla,
                    tipoError = excepcionGeneral.GetType().Name,
                    detalle = excepcionGeneral.Message,
                    detalleCompleto = detalleError.ToString(),
                    errorInterno = excepcionGeneral.InnerException?.Message,
                    timestamp = DateTime.UtcNow,
                    sugerencia = "Revise los logs para más detalles."
                });
            }
        }

        // aquí se puede agregar más endpoints en el futuro (DELETE, PATCH, etc.)

    }
}

// NOTAS PEDAGÓGICAS para el tutorial:
//
// 1. ESTA ES LA CAPA DE PRESENTACIÓN HTTP:
//    - Su única responsabilidad es manejar la comunicación HTTP ↔ Lógica de Negocio
//    - NO contiene lógica de negocio (eso está en servicios)
//    - NO accede directamente a datos (eso está en repositorios)
//    - Su trabajo es: recibir HTTP, llamar servicio, devolver HTTP
//
// 2. PATRÓN API GENÉRICA:
//    - Un solo controlador maneja múltiples tablas (vs crear UserController, ProductController, etc.)
//    - Reduce duplicación de código significativamente
//    - Escalable automáticamente: nuevas tablas funcionan sin código adicional
//    - Comportamiento consistente para todas las entidades
//
// 3. APLICACIÓN PRÁCTICA DE SOLID:
//    - SRP: Solo coordinación HTTP, cada método tiene una responsabilidad específica
//    - DIP: Depende de IServicioCrud (abstracción), no de ServicioCrud (implementación)
//    - OCP: Preparado para agregar nuevos endpoints sin modificar código existente
//    - ISP: Usa solo los métodos necesarios de IServicioCrud (por ahora solo ListarAsync)
//
// 4. MANEJO DE ERRORES ESTRATIFICADO:
//    - ArgumentException → 400 Bad Request (errores de entrada del usuario)
//    - InvalidOperationException → 404 Not Found (recurso no existe o no accesible)
//    - Exception general → 500 Internal Server Error (errores del sistema)
//    - Cada tipo se maneja con logging y respuesta apropiados
//
// 5. LOGGING ESTRUCTURADO:
//    - LogInformation para operaciones normales exitosas
//    - LogWarning para errores de entrada del usuario (no críticos)
//    - LogError para errores que requieren investigación técnica
//    - Parámetros estructurados facilitan búsquedas y análisis posteriores
//
// 6. RESPUESTAS HTTP ENRIQUECIDAS:
//    - No solo devuelve datos crudos, sino también metadatos útiles
//    - Información contextual que facilita el uso de la API
//    - Estructura consistente que se mantiene para todas las operaciones
//    - Códigos de estado HTTP semánticamente correctos
//
// 7. ENDPOINTS AUXILIARES DE DESCUBRIMIENTO:
//    - /api/info: información técnica sobre capacidades del controlador
//    - /: página de bienvenida con guía rápida de uso
//    - Facilitan adopción y uso de la API sin documentación externa
//
// 8. ARQUITECTURA COMPLETA EN FUNCIONAMIENTO:
//    Petición HTTP → EntidadesController → ServicioCrud → RepositorioLectura → Base de Datos
//    ↑ Presentación  ↑ Coordinación HTTP  ↑ Lógica Negocio ↑ Acceso Datos    ↑ Almacenamiento
//
// 9. PREPARADO PARA EXTENSIONES FUTURAS:
//    - Estructura lista para agregar más endpoints (POST, PUT, DELETE)
//    - Manejo de errores extensible para nuevos tipos de operaciones
//    - Logging preparado para auditoría completa de operaciones CRUD
//
// 10. TESTING DE ESTE CONTROLADOR:
//     - IServicioCrud se puede mockear fácilmente para testing unitario
//     - Cada método devuelve IActionResult que se puede verificar
//     - Escenarios de error se pueden simular mediante mocks
//     - Logging se puede verificar usando test loggers
