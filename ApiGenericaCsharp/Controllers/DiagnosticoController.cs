// DiagnosticoController.cs — Controlador especializado para diagnóstico de conexiones de base de datos
// Ubicación: Controllers/DiagnosticoController.cs
//
// Principios SOLID aplicados:
// - SRP: Este controlador solo maneja diagnóstico de conexiones, nada más
// - DIP: Depende de IRepositorioLecturaTabla (abstracción), no de implementaciones concretas
// - OCP: Abierto para extensión (nuevos tipos de diagnóstico) sin modificar código existente

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using ApiGenericaCsharp.Repositorios.Abstracciones;

namespace ApiGenericaCsharp.Controllers
{
    /// <summary>
    /// Controlador especializado para operaciones de diagnóstico de conexiones de base de datos.
    ///
    /// RESPONSABILIDAD ÚNICA (SRP):
    /// Este controlador se encarga exclusivamente de proporcionar información de diagnóstico
    /// sobre las conexiones activas a la base de datos. No maneja operaciones CRUD ni lógica de negocio.
    ///
    /// SEPARACIÓN DE PREOCUPACIONES:
    /// - EntidadesController: operaciones CRUD genéricas
    /// - DiagnosticoController: información de diagnóstico y troubleshooting
    /// - Cada uno con responsabilidades claras y no solapadas
    ///
    /// USO PRINCIPAL:
    /// - Verificar a qué instancia de BD está conectada la API (local vs Docker)
    /// - Diagnosticar problemas de conexión
    /// - Obtener metadatos del servidor sin exponer credenciales
    /// </summary>
    [Route("api/diagnostico")]
    [ApiController]
    public class DiagnosticoController : ControllerBase
    {
        // Dependencias inyectadas siguiendo DIP
        private readonly IRepositorioLecturaTabla _repositorio;
        private readonly ILogger<DiagnosticoController> _logger;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Constructor con inyección de dependencias.
        ///
        /// APLICACIÓN DE DIP (Dependency Inversion Principle):
        /// - Dependemos de IRepositorioLecturaTabla (abstracción)
        /// - No dependemos de implementaciones concretas (RepositorioLecturaPostgreSQL, etc.)
        /// - El contenedor DI resuelve automáticamente qué implementación usar según DatabaseProvider
        /// </summary>
        /// <param name="repositorio">
        /// Repositorio genérico de lectura. La implementación concreta se determina
        /// en Program.cs según el DatabaseProvider configurado.
        /// </param>
        /// <param name="logger">Logger para auditoría y troubleshooting</param>
        /// <param name="configuration">Configuración de la aplicación</param>
        public DiagnosticoController(
            IRepositorioLecturaTabla repositorio,
            ILogger<DiagnosticoController> logger,
            IConfiguration configuration)
        {
            _repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// Obtiene información de diagnóstico sobre la conexión actual a la base de datos.
        ///
        /// Ruta: GET /api/diagnostico/conexion
        ///
        /// FUNCIONAMIENTO:
        /// 1. Delega al repositorio correspondiente según DatabaseProvider
        /// 2. El repositorio ejecuta consultas específicas del motor de BD
        /// 3. Devuelve información estructurada sin exponer credenciales
        ///
        /// INFORMACIÓN DEVUELTA (varía según el motor):
        /// - Nombre de la base de datos actual
        /// - Versión del motor de base de datos
        /// - Servidor/host conectado
        /// - Puerto de conexión
        /// - Hora de inicio del servidor (clave para distinguir Docker vs local)
        /// - Usuario conectado
        /// - ID del proceso/sesión
        ///
        /// NOTA DE SEGURIDAD:
        /// Este endpoint NO expone credenciales ni la cadena de conexión completa.
        /// Solo muestra metadatos públicos del servidor para propósitos de diagnóstico.
        /// </summary>
        /// <returns>
        /// Objeto JSON con información de diagnóstico estructurada
        /// </returns>
        [AllowAnonymous]
        [HttpGet("conexion")]
        public async Task<IActionResult> ObtenerDiagnosticoConexionAsync()
        {
            try
            {
                // LOGGING DE AUDITORÍA
                _logger.LogInformation("INICIO diagnóstico de conexión");

                // Obtener proveedor configurado
                var proveedorConfigurado = _configuration.GetValue<string>("DatabaseProvider") ?? "SqlServer";

                // DELEGACIÓN AL REPOSITORIO (aplicando SRP y DIP)
                // El repositorio inyectado ya es el correcto según DatabaseProvider
                // No necesitamos switch ni lógica específica de BD aquí
                var diagnostico = await _repositorio.ObtenerDiagnosticoConexionAsync();

                // LOGGING DE RESULTADO
                _logger.LogInformation(
                    "DIAGNÓSTICO exitoso - Proveedor: {Proveedor}",
                    proveedorConfigurado
                );

                // CONSTRUCCIÓN DE RESPUESTA
                return Ok(new
                {
                    estado = 200,
                    mensaje = "Diagnóstico de conexión obtenido exitosamente.",
                    servidor = diagnostico,
                    configuracion = new
                    {
                        proveedorConfigurado = proveedorConfigurado,
                        descripcion = "Proveedor configurado en appsettings.json"
                    },
                    timestamp = DateTime.UtcNow
                });
            }
            catch (NotImplementedException)
            {
                // El repositorio actual no implementa diagnóstico
                var proveedor = _configuration.GetValue<string>("DatabaseProvider") ?? "desconocido";

                _logger.LogWarning(
                    "Diagnóstico no implementado para proveedor: {Proveedor}",
                    proveedor
                );

                return StatusCode(501, new
                {
                    estado = 501,
                    mensaje = $"El diagnóstico de conexión no está implementado para el proveedor '{proveedor}'.",
                    detalle = "Esta funcionalidad aún no está disponible para este motor de base de datos.",
                    proveedorConfigurado = proveedor
                });
            }
            catch (Exception excepcionGeneral)
            {
                // ERROR GENERAL NO ESPERADO
                _logger.LogError(excepcionGeneral,
                    "ERROR CRÍTICO - Falla en diagnóstico de conexión"
                );

                return StatusCode(500, new
                {
                    estado = 500,
                    mensaje = "Error interno al obtener diagnóstico de conexión.",
                    detalle = excepcionGeneral.Message,
                    tipoError = excepcionGeneral.GetType().Name,
                    timestamp = DateTime.UtcNow,
                    sugerencia = "Revise los logs del servidor para más detalles."
                });
            }
        }
    }
}

// NOTAS PEDAGÓGICAS para el tutorial:
//
// 1. CONTROLADOR ESPECIALIZADO:
//    - Responsabilidad única: solo diagnóstico
//    - No mezcla con operaciones CRUD
//    - Fácil de mantener y extender
//
// 2. APLICACIÓN CORRECTA DE DIP:
//    - Depende de IRepositorioLecturaTabla (abstracción)
//    - No conoce las implementaciones concretas
//    - No tiene switch/case por proveedor
//    - El contenedor DI resuelve la implementación correcta
//
// 3. ARQUITECTURA LIMPIA:
//    DiagnosticoController → IRepositorioLecturaTabla → Implementación específica
//    (Presentación)          (Abstracción)              (RepositorioLecturaPostgreSQL, etc.)
//
// 4. EXTENSIBILIDAD (OCP):
//    - Para agregar soporte a un nuevo motor de BD:
//      1. Crear RepositorioLecturaNuevoMotor : IRepositorioLecturaTabla
//      2. Implementar ObtenerDiagnosticoConexionAsync()
//      3. Registrar en Program.cs
//      4. Este controlador funciona automáticamente sin cambios
//
// 5. SIN ACOPLAMIENTO:
//    - No hay referencias a Npgsql, SqlClient, MySqlConnector, Oracle
//    - Todo el código específico de BD está en los repositorios
//    - El controlador solo coordina HTTP ↔ Repositorio


