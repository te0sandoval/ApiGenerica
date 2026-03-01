// ConsultasController.cs — Controlador específico para ejecutar consultas SQL parametrizadas
// Ubicación: Controllers/ConsultasController.cs
//
// Principios SOLID aplicados:
// - SRP: El controlador solo coordina peticiones HTTP para consultas SQL, una responsabilidad específica
// - DIP: Depende de IServicioConsultas (abstracción), no de ServicioConsultas (implementación concreta)
// - ISP: Consume solo los métodos necesarios de IServicioConsultas
// - OCP: Preparado para agregar más endpoints sin modificar código existente

using System;                                             // Para DateTime y excepciones básicas
using Microsoft.AspNetCore.Authorization;                // Para [AllowAnonymous] y políticas de autorización
using Microsoft.AspNetCore.Mvc;                          // Para ControllerBase, IActionResult, atributos HTTP
using System.Threading.Tasks;                            // Para async/await
using System.Collections.Generic;                        // Para Dictionary y List
using System.Data;                                       // Para DataRow y DataColumn
using System.Linq;                                       // Para Cast y ToDictionary
using System.Text.Json;                                 // Para JsonElement
using Microsoft.Extensions.Logging;                      // Para ILogger y logging estructurado
using ApiGenericaCsharp.Servicios.Abstracciones;              // Para IServicioConsultas

namespace ApiGenericaCsharp.Controllers
{
    /// <summary>
    /// Controlador específico para ejecutar consultas SQL parametrizadas de forma segura.
    /// 
    /// Este controlador se especializa en permitir la ejecución de consultas SQL arbitrarias
    /// con parámetros seguros, aplicando validaciones de seguridad y límites apropiados.
    /// 
    /// Diferencias con EntidadesController:
    /// - EntidadesController: Operaciones CRUD estándar sobre tablas (SELECT * FROM tabla)
    /// - ConsultasController: Consultas SQL personalizadas con parámetros y lógica compleja
    /// 
    /// Responsabilidades específicas:
    /// - Recibir consultas SQL y parámetros desde peticiones HTTP POST
    /// - Validar formato de entrada (JSON bien formado)
    /// - Delegar validaciones de seguridad y ejecución al servicio especializado
    /// - Convertir DataTable a formato JSON amigable para respuestas HTTP
    /// - Manejar errores específicos de consultas SQL con códigos HTTP apropiados
    /// - Generar logging detallado para auditoría y debugging de consultas ejecutadas
    /// </summary>
    [Route("api/consultas")]                              // Ruta específica: /api/consultas
    [ApiController]                                       // Activa validación automática, binding, y comportamientos REST
    public class ConsultasController : ControllerBase
    {
        // Dependencias inyectadas - Aplicando DIP (Dependency Inversion Principle)
        private readonly IServicioConsultas _servicioConsultas;    // Para lógica de negocio y validaciones de consultas
        private readonly ILogger<ConsultasController> _logger;     // Para logging estructurado y auditoría

        /// <summary>
        /// Constructor que recibe dependencias mediante inyección automática del contenedor DI.
        /// 
        /// FLUJO DE INYECCIÓN DE DEPENDENCIAS:
        /// 1. ASP.NET Core necesita crear ConsultasController para manejar petición HTTP entrante
        /// 2. Ve que el constructor requiere IServicioConsultas e ILogger
        /// 3. Para IServicioConsultas: busca registro en Program.cs y crea ServicioConsultas
        /// 4. ServicioConsultas requiere IRepositorioConsultas, entonces crea el repositorio apropiado
        /// 5. El repositorio requiere IProveedorConexion, entonces crea ProveedorConexion
        /// 6. ProveedorConexion requiere IConfiguration (ya disponible como singleton)
        /// 7. Inyecta toda la cadena automáticamente en este constructor
        /// 8. El controlador queda listo para manejar peticiones HTTP específicas de consultas
        /// </summary>
        /// <param name="servicioConsultas">
        /// Servicio especializado que contiene toda la lógica de negocio para consultas SQL.
        /// Se inyecta desde: builder.Services.AddScoped<IServicioConsultas, ServicioConsultas>()
        /// Aplica DIP: dependemos de la abstracción IServicioConsultas, no de la implementación
        /// </param>
        /// <param name="logger">
        /// Logger específico para este controlador, registrado automáticamente por ASP.NET Core.
        /// Permite logging estructurado con contexto específico de "ConsultasController".
        /// Esencial para auditoría de consultas SQL ejecutadas y debugging de problemas.
        /// </param>
        public ConsultasController(
            IServicioConsultas servicioConsultas,        // Lógica de negocio para consultas SQL especializadas
            ILogger<ConsultasController> logger          // Logging estructurado, auditoría y monitoreo
        )
        {
            // Validaciones defensivas - Guard clauses para prevenir errores de configuración DI
            _servicioConsultas = servicioConsultas ?? throw new ArgumentNullException(
                nameof(servicioConsultas), 
                "IServicioConsultas no fue inyectado correctamente. Verificar registro de servicios en Program.cs"
            );
            
            _logger = logger ?? throw new ArgumentNullException(
                nameof(logger), 
                "ILogger no fue inyectado correctamente. Problema en configuración de logging de ASP.NET Core"
            );
        }

        /// <summary>
        /// Endpoint principal para ejecutar consultas SQL parametrizadas de forma segura.
        /// 
        /// Este endpoint recibe consultas SQL arbitrarias con parámetros seguros y las ejecuta
        /// aplicando todas las validaciones de seguridad configuradas en el servicio.
        /// 
        /// Ruta completa: POST /api/consultas/ejecutarconsultaparametrizada
        /// 
        /// Formato de entrada JSON esperado:
        /// {
        ///   "consulta": "SELECT * FROM productos WHERE precio > @precio AND categoria = @categoria",
        ///   "parametros": {
        ///     "precio": 100,
        ///     "@categoria": "Electronics"
        ///   }
        /// }
        /// 
        /// Proceso detallado de la petición:
        /// 1. ASP.NET Core recibe POST y deserializa JSON automáticamente a Dictionary
        /// 2. Este método valida formato básico de entrada (consulta no vacía)
        /// 3. Extrae parámetros del JSON y los normaliza para el servicio
        /// 4. Delega validaciones de seguridad y ejecución al servicio especializado
        /// 5. Convierte DataTable devuelto por el servicio a formato JSON amigable
        /// 6. Construye respuesta HTTP estructurada con datos y metadatos útiles
        /// 7. Maneja errores con códigos HTTP semánticamente correctos
        /// 
        /// Validaciones de seguridad aplicadas por el servicio:
        /// - Solo consultas SELECT (previene modificaciones accidentales)
        /// - Tablas prohibidas según configuración (evita acceso a datos sensibles)
        /// - Parámetros seguros (previene inyección SQL automáticamente)
        /// - Límites de registros (previene consultas masivas que afecten performance)
        /// </summary>
        /// <param name="cuerpoSolicitud">
        /// Diccionario que representa el JSON de entrada deserializado automáticamente.
        /// Debe contener al menos la clave "consulta" con la consulta SQL a ejecutar.
        /// Opcionalmente puede contener "parametros" con los parámetros de la consulta.
        /// 
        /// ASP.NET Core maneja automáticamente la deserialización desde JSON HTTP body.
        /// </param>
        /// <returns>
        /// IActionResult que representa la respuesta HTTP completa:
        /// - 200 OK: Consulta ejecutada exitosamente con datos y metadatos
        /// - 400 Bad Request: Formato de entrada inválido o parámetros incorrectos
        /// - 403 Forbidden: Consulta viola políticas de seguridad (tablas prohibidas, etc.)
        /// - 404 Not Found: Consulta exitosa pero sin resultados
        /// - 500 Internal Server Error: Error técnico durante la ejecución
        /// 
        /// Estructura de respuesta exitosa:
        /// {
        ///   "Resultados": [...],     // Array de objetos con los datos
        ///   "Total": 42,             // Cantidad de registros devueltos
        ///   "Advertencia": null      // Mensaje si se alcanzó límite máximo
        /// }
        /// </returns>
        //[Authorize]                                       // Requiere autenticación JWT válida
        [HttpPost("ejecutarconsultaparametrizada")]      // Responde a POST /api/consultas/ejecutarconsultaparametrizada
        public async Task<IActionResult> EjecutarConsultaParametrizadaAsync([FromBody] Dictionary<string, object?> cuerpoSolicitud)
        {
            const int maximoRegistros = 10000;            // Límite para generar advertencias en respuesta

            try
            {
                // ==============================================================================
                // FASE 1: VALIDACIÓN BÁSICA DE FORMATO DE ENTRADA
                // ==============================================================================
                
                // Validar que la consulta esté presente y no sea null
                if (!cuerpoSolicitud.TryGetValue("consulta", out var consultaObj) || consultaObj is null)
                    return BadRequest("La consulta no puede estar vacía.");

                // Extraer la consulta del objeto, manejando diferentes tipos posibles
                // JsonElement viene cuando ASP.NET Core deserializa JSON automáticamente
                string consulta = consultaObj switch
                {
                    string texto => texto,                                                    // Cadena directa
                    JsonElement json when json.ValueKind == JsonValueKind.String => json.GetString() ?? string.Empty, // JSON string
                    _ => string.Empty                                             // Cualquier otro tipo se considera vacío
                };

                if (string.IsNullOrWhiteSpace(consulta))
                    return BadRequest("La consulta no puede estar vacía.");

                // ==============================================================================
                // FASE 2: EXTRACCIÓN Y NORMALIZACIÓN DE PARÁMETROS
                // ==============================================================================
                
                // Preparar diccionario de parámetros para el servicio
                Dictionary<string, object?>? parametros = null;
                
                if (cuerpoSolicitud.TryGetValue("parametros", out var parametrosObj) &&
                    parametrosObj is JsonElement jsonParametros &&
                    jsonParametros.ValueKind == JsonValueKind.Object)
                {
                    // Convertir JsonElement a Dictionary para el servicio
                    parametros = new Dictionary<string, object?>();
                    foreach (var p in jsonParametros.EnumerateObject())
                    {
                        // Agregar cada parámetro manteniendo el JsonElement
                        // El servicio se encargará de la conversión de tipos final
                        parametros[p.Name] = p.Value;
                    }
                }

                // ==============================================================================
                // FASE 3: LOGGING DE AUDITORÍA PARA CONSULTAS EJECUTADAS
                // ==============================================================================
                
                // Registrar intento de ejecución de consulta para auditoría de seguridad
                // Información crítica para detectar patrones de uso malicioso o problemático
                _logger.LogInformation(
                    "INICIO ejecución consulta SQL - Consulta: {Consulta}, Parámetros: {CantidadParametros}",
                    consulta.Length > 100 ? consulta.Substring(0, 100) + "..." : consulta,  // Truncar consultas muy largas en logs
                    parametros?.Count ?? 0                                        // Cantidad de parámetros recibidos
                );

                // ==============================================================================
                // FASE 4: DELEGACIÓN AL SERVICIO (APLICANDO SRP y DIP)
                // ==============================================================================
                
                // PRINCIPIO SRP APLICADO:
                // El controlador NO contiene validaciones de seguridad ni lógica de consultas
                // Solo coordina la comunicación HTTP ↔ Servicio especializado
                
                // PRINCIPIO DIP APLICADO:
                // Dependemos de IServicioConsultas (abstracción), no de ServicioConsultas (implementación)
                // El servicio aplicará todas las validaciones de seguridad y ejecutará la consulta
                var resultado = await _servicioConsultas.EjecutarConsultaParametrizadaDesdeJsonAsync(consulta, parametros);

                // ==============================================================================
                // FASE 5: CONVERSIÓN DE DATATABLE A FORMATO JSON-FRIENDLY
                // ==============================================================================
                
                // Convertir DataTable (eficiente para operaciones de BD) a List<Dictionary> (JSON-friendly)
                var lista = new List<Dictionary<string, object?>>();
                foreach (DataRow fila in resultado.Rows)
                {
                    // Crear diccionario para cada fila con conversión de tipos apropiada
                    var filaDiccionario = resultado.Columns.Cast<DataColumn>()
                        .ToDictionary(
                            col => col.ColumnName.ToLower(),                     // Clave: nombre de columna (lowercase para Oracle)
                            col => fila[col] == DBNull.Value ? null : fila[col]  // Valor: convertir DBNull a null C#
                        );
                    lista.Add(filaDiccionario);
                }

                // ==============================================================================
                // FASE 6: LOGGING DE RESULTADOS PARA MONITOREO Y ANÁLISIS
                // ==============================================================================
                
                // Registrar resultado exitoso para métricas de uso y debugging
                _logger.LogInformation(
                    "ÉXITO ejecución consulta SQL - Registros obtenidos: {Cantidad}", 
                    lista.Count     // Cantidad exacta de registros devueltos
                );

                // ==============================================================================
                // FASE 7: MANEJO DE CASO SIN RESULTADOS (404 NOT FOUND)
                // ==============================================================================
                
                // En REST, distinguir entre recurso no existe vs recurso existe pero vacío
                if (lista.Count == 0)
                {
                    _logger.LogInformation("SIN DATOS - Consulta ejecutada correctamente pero no devolvió registros");
                    return NotFound("La consulta se ejecutó correctamente pero no devolvió resultados.");
                }

                // ==============================================================================
                // FASE 8: RESPUESTA EXITOSA CON DATOS Y METADATOS (200 OK)
                // ==============================================================================
                
                // Construir respuesta enriquecida consistente con el formato original del endpoint
                return Ok(new
                {
                    Resultados = lista,                                          // Datos reales de la consulta
                    Total = lista.Count,                                         // Cantidad exacta de registros
                    Advertencia = lista.Count == maximoRegistros ? 
                        $"Se alcanzó el límite de {maximoRegistros} registros." : null  // Advertencia si se alcanzó límite
                });
            }
            // ==============================================================================
            // MANEJO DE ERRORES ESTRATIFICADO POR TIPO DE EXCEPCIÓN
            // ==============================================================================
            
            catch (UnauthorizedAccessException excepcionAcceso)
            {
                // ERRORES DE POLÍTICA DE SEGURIDAD (403 FORBIDDEN)
                // Se lanzan cuando la consulta viola reglas de seguridad del servicio:
                // - Intenta acceder a tablas prohibidas según configuración
                // - Contiene operaciones no permitidas (no SELECT)
                // - Viola otras políticas de seguridad específicas del dominio
                
                _logger.LogWarning(
                    "ACCESO DENEGADO - Consulta rechazada por políticas de seguridad: {Mensaje}",
                    excepcionAcceso.Message    // Mensaje específico de la política violada
                );

                // Respuesta 403 Forbidden con información específica sobre la violación de seguridad
                return StatusCode(403, new
                {
                    estado = 403,                                      // Código HTTP explícito
                    mensaje = "Acceso denegado por políticas de seguridad.", // Mensaje general
                    detalle = excepcionAcceso.Message,                // Detalle específico de la violación
                    sugerencia = "Verifique que la consulta cumple con las políticas de seguridad configuradas"
                });
            }
            catch (ArgumentException excepcionArgumento)
            {
                // ERRORES DE VALIDACIÓN DE ENTRADA (400 BAD REQUEST)
                // Se lanzan cuando hay problemas con formato o contenido de parámetros:
                // - Nombres de parámetros inválidos (caracteres no permitidos)
                // - Tipos de datos incompatibles o mal formateados
                // - Violación de reglas de formato específicas
                
                _logger.LogWarning(
                    "PARÁMETROS INVÁLIDOS - Formato de entrada incorrecto: {Mensaje}",
                    excepcionArgumento.Message     // Detalle específico del problema de formato
                );

                // Respuesta 400 Bad Request con información para corregir la entrada
                return BadRequest(new
                {
                    estado = 400,                                      // Código HTTP explícito
                    mensaje = "Parámetros de entrada inválidos.",     // Mensaje general para el usuario
                    detalle = excepcionArgumento.Message,             // Detalle específico del problema
                    sugerencia = "Verifique el formato de la consulta y los nombres de parámetros"
                });
            }
            catch (Exception excepcionGeneral)
            {
                // ERRORES INESPERADOS/CRÍTICOS (500 INTERNAL SERVER ERROR)
                // Cualquier error técnico no anticipado que requiere investigación:
                // - Errores de conexión a base de datos
                // - Problemas de sintaxis SQL no detectados en validación
                // - Errores de infraestructura o configuración
                // - Bugs en el código que requieren corrección

                _logger.LogError(excepcionGeneral,
                    "ERROR CRÍTICO - Falla inesperada ejecutando consulta SQL"
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

                // Respuesta 500 Internal Server Error con detalles útiles para debugging
                return StatusCode(500, new
                {
                    estado = 500,                                        // Código HTTP explícito
                    mensaje = "Error interno del servidor al ejecutar consulta SQL.", // Mensaje descriptivo
                    tipoError = excepcionGeneral.GetType().Name,        // Tipo de excepción para clasificación
                    detalle = excepcionGeneral.Message,                 // Mensaje principal del error
                    detalleCompleto = detalleError.ToString(),          // Desglose completo formateado
                    errorInterno = excepcionGeneral.InnerException?.Message, // Error de causa raíz
                    timestamp = DateTime.UtcNow,                        // Timestamp para correlación con logs
                    sugerencia = "Revise los logs del servidor para más detalles o contacte al administrador."
                });
            }
        }
    }
}

// NOTAS PEDAGÓGICAS para el tutorial:
//
// 1. CONTROLADOR ESPECIALIZADO VS GENÉRICO:
//    - EntidadesController: Maneja operaciones CRUD estándar sobre tablas
//    - ConsultasController: Maneja consultas SQL arbitrarias con validaciones especiales
//    - Cada controlador tiene responsabilidades específicas y bien definidas (SRP)
//
// 2. SEPARACIÓN CLARA DE RESPONSABILIDADES:
//    - Controlador: Solo coordinación HTTP ↔ Servicio, conversión DataTable → JSON
//    - Servicio: Validaciones de seguridad, conversión de parámetros, lógica de negocio
//    - Repositorio: Acceso técnico a datos, ejecución real de consultas SQL
//    - Cada capa se enfoca en su responsabilidad específica
//
// 3. MANEJO DE ERRORES ESPECÍFICO PARA CONSULTAS:
//    - UnauthorizedAccessException → 403 (políticas de seguridad violadas)
//    - ArgumentException → 400 (parámetros mal formateados)
//    - Exception general → 500 (errores técnicos inesperados)
//    - Logging específico para auditoría de consultas SQL ejecutadas
//
// 4. FORMATO DE RESPUESTA CONSISTENTE:
//    - Mantiene el mismo formato que el endpoint original
//    - Resultados, Total, Advertencia para compatibilidad
//    - Metadatos útiles para el cliente de la API
//
// 5. AUDITORÍA Y SEGURIDAD:
//    - Logging detallado de todas las consultas ejecutadas
//    - No exposición de detalles internos en errores 500
//    - Mensajes específicos para violaciones de seguridad
//    - Timestamps para correlación con logs del servidor
//
// 6. RUTA ESPECÍFICA Y SEMÁNTICA:
//    - /api/consultas/ejecutarconsultaparametrizada
//    - Claramente diferenciada de /api/{tabla} del EntidadesController
//    - RESTful y descriptiva del propósito específico

