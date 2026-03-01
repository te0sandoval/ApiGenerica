// Controllers/EstructurasController
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Extensions.Logging;
using ApiGenericaCsharp.Repositorios.Abstracciones;

namespace ApiGenericaCsharp.Controllers
{
    [Route("api/estructuras")]
    [ApiController]
    public class EstructurasController : ControllerBase
    {
        private readonly IRepositorioConsultas _repositorioConsultas;
        private readonly ILogger<EstructurasController> _logger;

        public EstructurasController(IRepositorioConsultas repositorioConsultas, ILogger<EstructurasController> logger)
        {
            _repositorioConsultas = repositorioConsultas;
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpGet("{nombreTabla}/modelo")]
        public async Task<IActionResult> ObtenerModeloAsync(string nombreTabla, [FromQuery] string? esquema = null)
        {
            if (string.IsNullOrWhiteSpace(nombreTabla))
                return BadRequest("El nombre de la tabla no puede estar vacío.");

            try
            {
                var esquemaReal = await _repositorioConsultas.ObtenerEsquemaTablaAsync(nombreTabla, esquema);
                
                if (string.IsNullOrWhiteSpace(esquemaReal))
                    return NotFound($"No se encontró la tabla '{nombreTabla}' en ningún esquema.");

                var estructura = await _repositorioConsultas.ObtenerEstructuraTablaAsync(nombreTabla, esquemaReal);
                
                var lista = ConvertirDataTableALista(estructura);
                
                return Ok(new { datos = lista, total = lista.Count });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error al obtener modelo de tabla {Tabla}", nombreTabla);
                return StatusCode(500, "Error interno del servidor");
            }
        }

        [AllowAnonymous]
        [HttpGet("basedatos")]
        public async Task<IActionResult> ObtenerEstructuraBaseDatosAsync()
        {
            try
            {
                var estructura = await _repositorioConsultas.ObtenerEstructuraCompletaBaseDatosAsync();
                return Ok(estructura);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error al obtener estructura de BD");
                return StatusCode(500, "Error interno del servidor");
            }
        }

        private List<Dictionary<string, object?>> ConvertirDataTableALista(DataTable dataTable)
        {
            var lista = new List<Dictionary<string, object?>>();
            foreach (DataRow fila in dataTable.Rows)
            {
                // Normalizar nombres de columna a lowercase para compatibilidad Oracle
                var filaDiccionario = dataTable.Columns.Cast<DataColumn>()
                    .ToDictionary(col => col.ColumnName.ToLower(), col => fila[col] == DBNull.Value ? null : fila[col]);
                lista.Add(filaDiccionario);
            }
            return lista;
        }
    }
}

