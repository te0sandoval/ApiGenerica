// ProcedimientosController.cs — Controlador específico para ejecutar procedimientos almacenados
// Ubicación: Controllers/ProcedimientosController.cs
//
// Principios SOLID aplicados:
// - SRP: El controlador solo coordina peticiones HTTP para procedimientos almacenados
// - DIP: Depende de IServicioConsultas (abstracción), no de ServicioConsultas (implementación concreta)
// - ISP: Consume solo los métodos necesarios de IServicioConsultas
// - OCP: Preparado para agregar más endpoints sin modificar código existente

using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ApiGenericaCsharp.Servicios.Abstracciones;

namespace ApiGenericaCsharp.Controllers
{
    /// <summary>
    /// Controlador específico para ejecutar procedimientos almacenados de forma segura.
    /// 
    /// Este controlador se especializa en permitir la ejecución de procedimientos almacenados
    /// con parámetros seguros y capacidades de encriptación para campos sensibles.
    /// 
    /// Diferencias con ConsultasController:
    /// - ConsultasController: Consultas SQL SELECT arbitrarias con validaciones estrictas
    /// - ProcedimientosController: Procedimientos almacenados que pueden hacer INSERT/UPDATE/DELETE
    /// 
    /// Responsabilidades específicas:
    /// - Recibir nombre del SP y parámetros desde peticiones HTTP POST
    /// - Procesar parámetros JSON complejos para procedimientos almacenados
    /// - Convertir DataTable a formato JSON amigable para respuestas HTTP
    /// - Manejar errores específicos de procedimientos almacenados
    /// </summary>
    [Route("api/procedimientos")]
    [ApiController]
    public class ProcedimientosController : ControllerBase
    {
        private readonly IServicioConsultas _servicioConsultas;
        private readonly ILogger<ProcedimientosController> _logger;

        /// <summary>
        /// Constructor que recibe dependencias mediante inyección de dependencias.
        /// </summary>
        /// <param name="servicioConsultas">Servicio para ejecutar procedimientos almacenados</param>
        /// <param name="logger">Logger específico para este controlador</param>
        public ProcedimientosController(
            IServicioConsultas servicioConsultas,
            ILogger<ProcedimientosController> logger)
        {
            _servicioConsultas = servicioConsultas ?? throw new ArgumentNullException(nameof(servicioConsultas));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Endpoint para ejecutar procedimientos almacenados con parámetros opcionales.
        /// 
        /// Ruta completa: POST /api/procedimientos/ejecutarsp
        /// 
        /// Formato de entrada JSON esperado:
        /// {
        ///   "nombreSP": "select_json_entity",
        ///   "p_table_name": "usuario",
        ///   "mensaje": "",
        ///   "where_condition": null,
        ///   "order_by": null,
        ///   "limit_clause": null,
        ///   "json_params": "{}",
        ///   "select_columns": "*"
        /// }
        /// </summary>
        /// <param name="parametrosSP">Diccionario con nombreSP y parámetros del procedimiento</param>
        /// <param name="camposEncriptar">Campos que deben ser encriptados, separados por coma (IGNORADO por ahora)</param>
        /// <returns>Resultados de la ejecución del procedimiento almacenado</returns>
        //[Authorize]
        [HttpPost("ejecutarsp")]
        public async Task<IActionResult> EjecutarProcedimientoAlmacenadoAsync(
            [FromBody] Dictionary<string, object?> parametrosSP,
            [FromQuery] string? camposEncriptar = null)
        {
            try
            {
                // FASE 1: VALIDACIÓN DE ENTRADA
                if (parametrosSP == null || !parametrosSP.TryGetValue("nombreSP", out var nombreSPObj) || nombreSPObj == null)
                {
                    return BadRequest("El parámetro 'nombreSP' es requerido.");
                }

                string nombreSP = nombreSPObj.ToString()!;
                
                // FASE 2: PROCESAMIENTO DE CAMPOS A ENCRIPTAR
                var camposAEncriptar = string.IsNullOrWhiteSpace(camposEncriptar)
                    ? new List<string>()
                    : camposEncriptar.Split(',').Select(c => c.Trim()).ToList();

                // FASE 3: PREPARAR PARÁMETROS (EXCLUIR nombreSP)
                var parametrosLimpios = new Dictionary<string, object?>();
                foreach (var kvp in parametrosSP)
                {
                    if (!kvp.Key.Equals("nombreSP", StringComparison.OrdinalIgnoreCase))
                    {
                        parametrosLimpios[kvp.Key] = kvp.Value;
                    }
                }

                // FASE 4: LOGGING DE AUDITORÍA
                _logger.LogInformation(
                    "INICIO ejecución SP - Procedimiento: {NombreSP}, Parámetros: {CantidadParametros}",
                    nombreSP,
                    parametrosLimpios.Count
                );

                // FASE 5: DELEGACIÓN AL SERVICIO
                var resultado = await _servicioConsultas.EjecutarProcedimientoAlmacenadoAsync(
                    nombreSP, 
                    parametrosLimpios, 
                    camposAEncriptar);

                // FASE 6: CONVERSIÓN DE DATATABLE A JSON-FRIENDLY
                var lista = new List<Dictionary<string, object?>>();
                foreach (DataRow fila in resultado.Rows)
                {
                    // Normalizar nombres de columna a lowercase para compatibilidad Oracle
                    var filaDiccionario = resultado.Columns.Cast<DataColumn>()
                        .ToDictionary(
                            col => col.ColumnName.ToLower(),
                            col => fila[col] == DBNull.Value ? null : fila[col]
                        );
                    lista.Add(filaDiccionario);
                }

                // FASE 7: LOGGING DE RESULTADO
                _logger.LogInformation(
                    "ÉXITO ejecución SP - Procedimiento: {NombreSP}, Registros: {Cantidad}",
                    nombreSP,
                    lista.Count
                );

                // FASE 8: RESPUESTA EXITOSA
                return Ok(new
                {
                    Procedimiento = nombreSP,
                    Resultados = lista,
                    Total = lista.Count,
                    Mensaje = "Procedimiento ejecutado correctamente"
                });
            }
            catch (ArgumentException excepcionArgumento)
            {
                _logger.LogWarning(
                    "PARÁMETROS INVÁLIDOS - SP: {Mensaje}",
                    excepcionArgumento.Message
                );

                return BadRequest(new
                {
                    estado = 400,
                    mensaje = "Parámetros de entrada inválidos.",
                    detalle = excepcionArgumento.Message
                });
            }
            catch (Exception excepcionGeneral)
            {
                _logger.LogError(excepcionGeneral,
                    "ERROR CRÍTICO - Falla inesperada ejecutando SP"
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

                return StatusCode(500, new
                {
                    estado = 500,
                    mensaje = "Error interno del servidor al ejecutar procedimiento almacenado.",
                    tipoError = excepcionGeneral.GetType().Name,
                    detalle = excepcionGeneral.Message,
                    detalleCompleto = detalleError.ToString(),
                    errorInterno = excepcionGeneral.InnerException?.Message,
                    timestamp = DateTime.UtcNow,
                    sugerencia = "Revise los logs del servidor para más detalles o contacte al administrador."
                });
            }
        }
    }
}

