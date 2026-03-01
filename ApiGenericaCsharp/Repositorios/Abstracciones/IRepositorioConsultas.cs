// IRepositorioConsultas.cs — Interface genérica limpia para ejecutar consultas SQL parametrizadas
// Ubicación: Repositorios/Abstracciones/IRepositorioConsultas.cs
//
// ARQUITECTURA LIMPIA: Solo métodos genéricos con Dictionary<string, object?>

using System.Collections.Generic;   // Para Dictionary<>
using System.Threading.Tasks;       // Para async/await
using System.Data;                  // Para DataTable

namespace ApiGenericaCsharp.Repositorios.Abstracciones
{
    /// <summary>
    /// Contrato genérico para repositorios que ejecutan consultas SQL parametrizadas.
    /// 
    /// ARQUITECTURA GENÉRICA:
    /// - Todos los métodos usan Dictionary<string, object?> como parámetros
    /// - Completamente independiente del motor de base de datos
    /// - Tipos .NET reales (DateTime, int, bool) en lugar de strings
    /// - Testing más fácil con Dictionary mock
    /// - Un solo repositorio funciona con cualquier motor de BD
    /// 
    /// FORMATO DE PARÁMETROS:
    /// Dictionary<string, object?> donde:
    /// - Key: nombre del parámetro (con o sin @)
    /// - Value: objeto .NET tipado (DateTime, int, string, bool, etc.)
    /// 
    /// EJEMPLO DE USO:
    /// var parametros = new Dictionary<string, object?> {
    ///     ["@fecha_desde"] = DateTime.Parse("2023-01-01"),  // DateTime object
    ///     ["@codigo_like"] = "IND%",                        // string object
    ///     ["@activo"] = true,                               // bool object  
    ///     ["@limite"] = 100                                 // int object
    /// };
    /// </summary>
    public interface IRepositorioConsultas
    {
        // ====================================================================
        // MÉTODOS GENÉRICOS CON DICTIONARY
        // ====================================================================

        /// <summary>
        /// Ejecuta consulta SQL parametrizada con Dictionary.
        /// 
        /// VENTAJAS:
        /// - Completamente independiente del motor de BD
        /// - Tipos .NET reales en lugar de conversiones string
        /// - Más eficiente - sin conversiones innecesarias
        /// - Testing simplificado con Dictionary mock
        /// - Funciona con PostgreSQL, SQL Server, MySQL, etc.
        /// </summary>
        /// <param name="consultaSQL">Consulta SQL parametrizada (SELECT, WITH)</param>
        /// <param name="parametros">Parámetros como Dictionary con objetos .NET tipados</param>
        /// <param name="maximoRegistros">Límite máximo de registros (default: 10000)</param>
        /// <param name="esquema">Esquema de BD opcional (default: null)</param>
        /// <returns>DataTable con resultados de la consulta</returns>
        Task<DataTable> EjecutarConsultaParametrizadaConDictionaryAsync(
            string consultaSQL,
            Dictionary<string, object?> parametros,
            int maximoRegistros = 10000,
            string? esquema = null
        );

        /// <summary>
        /// Valida consulta SQL con Dictionary sin ejecutarla.
        /// Útil para verificar sintaxis y parámetros antes de la ejecución real.
        /// </summary>
        /// <param name="consultaSQL">Consulta SQL a validar</param>
        /// <param name="parametros">Parámetros como Dictionary</param>
        /// <returns>Tupla con resultado de validación y mensaje de error si aplica</returns>
        Task<(bool esValida, string? mensajeError)> ValidarConsultaConDictionaryAsync(
            string consultaSQL,
            Dictionary<string, object?> parametros
        );

        /// <summary>
        /// Ejecuta procedimiento almacenado con Dictionary.
        /// 
        /// CARACTERÍSTICAS:
        /// - Detección automática de parámetros IN, OUT, INOUT
        /// - Mapeo automático de tipos según motor de BD
        /// - Construcción dinámica de comandos CALL/EXEC
        /// - Procesamiento automático de parámetros de salida
        /// </summary>
        /// <param name="nombreSP">Nombre del procedimiento almacenado</param>
        /// <param name="parametros">Parámetros como Dictionary</param>
        /// <returns>DataTable con resultados del procedimiento</returns>
        Task<DataTable> EjecutarProcedimientoAlmacenadoConDictionaryAsync(
            string nombreSP,
            Dictionary<string, object?> parametros
        );

        // ====================================================================
        // MÉTODOS DE METADATOS
        // ====================================================================

        /// <summary>
        /// Obtiene el esquema real donde existe una tabla específica.
        /// Busca primero en el esquema predeterminado, luego en todos los esquemas visibles.
        /// </summary>
        /// <param name="nombreTabla">Nombre de la tabla a buscar</param>
        /// <param name="esquemaPredeterminado">Esquema donde buscar primero (opcional)</param>
        /// <returns>Nombre del esquema donde existe la tabla, o null si no existe</returns>
        Task<string?> ObtenerEsquemaTablaAsync(string nombreTabla, string? esquemaPredeterminado);

        /// <summary>
        /// Obtiene la estructura detallada de una tabla específica.
        /// Incluye columnas, tipos, restricciones, valores por defecto, etc.
        /// </summary>
        /// <param name="nombreTabla">Nombre de la tabla</param>
        /// <param name="esquema">Esquema de la tabla</param>
        /// <returns>DataTable con estructura detallada de la tabla</returns>
        Task<DataTable> ObtenerEstructuraTablaAsync(string nombreTabla, string esquema);

        /// <summary>
        /// Obtiene la estructura completa de la base de datos.
        /// Incluye tablas, vistas, funciones, procedimientos, triggers, secuencias, etc.
        /// </summary>
        /// <returns>Dictionary con toda la metadata de la BD organizada por tipo de objeto</returns>
        Task<Dictionary<string, object>> ObtenerEstructuraCompletaBaseDatosAsync();
    }
}

// ============================================================================================
// GUÍA DE IMPLEMENTACIÓN
// ============================================================================================
//
// Los repositorios específicos deben implementar estos métodos según su motor:
//
// POSTGRESQL (NpgsqlParameter):
//   Dictionary → NpgsqlParameter → NpgsqlCommand → DataTable
//
// SQL SERVER (SqlParameter):
//   Dictionary → SqlParameter → SqlCommand → DataTable
//
// MYSQL (MySqlParameter):
//   Dictionary → MySqlParameter → MySqlCommand → DataTable
//
// BENEFICIOS DE ESTA ARQUITECTURA:
// ✅ Un solo servicio funciona con todos los repositorios
// ✅ Testing simplificado con Dictionary mock
// ✅ Sin dependencias específicas de motor en capas altas
// ✅ Conversión de tipos en un solo lugar (repositorio)
// ✅ Fácil agregar nuevos motores de BD
// ✅ Código más limpio y mantenible

