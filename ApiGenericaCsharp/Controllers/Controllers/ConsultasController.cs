// IServicioConsultas.cs — Interface que define el contrato para ejecutar consultas SQL parametrizadas
// Ubicación: Servicios/Abstracciones/IServicioConsultas.cs
//
// Principios SOLID aplicados:
// - SRP: Esta interface solo se encarga de definir operaciones de consultas SQL parametrizadas
// - DIP: Permite que el controlador dependa de esta abstracción, no de implementaciones concretas
// - ISP: Interface específica y pequeña, solo métodos relacionados con consultas SQL
// - OCP: Abierta para extensión (nuevas implementaciones) pero cerrada para modificación

using System.Collections.Generic;   // Para usar List<> y Dictionary<> genéricos
using System.Threading.Tasks;       // Para programación asíncrona con async/await
using Microsoft.Data.SqlClient;     // Para SqlParameter en las consultas parametrizadas
using System.Data;                  // Para DataTable

namespace ApiGenericaCsharp.Servicios.Abstracciones
{
    /// <summary>
    /// Contrato que define cómo ejecutar consultas SQL parametrizadas de forma segura.
    /// 
    /// Esta interface es el "qué" (contrato), no el "cómo" (implementación).
    /// Permite que el controlador de consultas dependa de esta abstracción
    /// en lugar de depender de una clase concreta específica.
    /// 
    /// Diferencias con IServicioCrud:
    /// - IServicioCrud: Operaciones sobre tablas completas (SELECT * FROM tabla)
    /// - IServicioConsultas: Consultas SQL arbitrarias con parámetros personalizados
    /// 
    /// Beneficios:
    /// - Facilita testing (se pueden crear mocks de esta interface)
    /// - Permite intercambiar implementaciones sin cambiar código cliente
    /// - Desacopla la lógica de consultas del controlador que las usa
    /// - Proporciona abstracción para validaciones de seguridad
    /// </summary>
    public interface IServicioConsultas
    {
        /// <summary>
        /// Valida que una consulta SQL sea segura para ejecutar.
        /// 
        /// Esta función aplica reglas de seguridad para prevenir:
        /// - Consultas que no sean SELECT (INSERT, UPDATE, DELETE, DROP)
        /// - Acceso a tablas prohibidas según configuración
        /// - Inyección SQL mediante validaciones adicionales
        /// - Palabras clave peligrosas en la consulta
        /// 
        /// Ejemplo de uso:
        /// var (esValida, error) = ValidarConsultaSQL("SELECT * FROM productos WHERE precio > 100", tablasProhibidas);
        /// if (!esValida) return BadRequest(error);
        /// </summary>
        /// <param name="consulta">
        /// Consulta SQL a validar. Debe ser una consulta SELECT válida.
        /// No puede estar vacía y debe cumplir las reglas de seguridad.
        /// 
        /// Ejemplos válidos:
        /// - "SELECT * FROM productos WHERE precio > @precio"
        /// - "SELECT nombre, precio FROM productos WHERE categoria = @categoria"
        /// - "SELECT p.*, c.nombre as categoria_nombre FROM productos p JOIN categorias c ON p.categoria_id = c.id"
        /// 
        /// Ejemplos inválidos:
        /// - "DELETE FROM productos" (no es SELECT)
        /// - "SELECT * FROM usuarios" (si "usuarios" está en tablas prohibidas)
        /// - "" (consulta vacía)
        /// </param>
        /// <param name="tablasProhibidas">
        /// Array de nombres de tablas que no pueden ser consultadas.
        /// Típicamente viene de la configuración en appsettings.json.
        /// 
        /// Ejemplo: ["usuario", "rol_usuario", "configuracion_sistema"]
        /// </param>
        /// <returns>
        /// Tupla con el resultado de la validación:
        /// - bool esValida: true si la consulta es segura, false si hay problemas
        /// - string? mensajeError: null si es válida, mensaje descriptivo si hay error
        /// 
        /// Ejemplos de retorno:
        /// - (true, null) → Consulta válida
        /// - (false, "Solo se permiten consultas SELECT") → Consulta inválida
        /// - (false, "La tabla 'usuarios' está prohibida") → Tabla restringida
        /// </returns>
        (bool esValida, string? mensajeError) ValidarConsultaSQL(string consulta, string[] tablasProhibidas);

        /// <summary>
        /// Ejecuta una consulta SQL parametrizada de forma segura y retorna los resultados.
        /// 
        /// Este método coordina todo el proceso:
        /// 1. Aplicar validaciones de seguridad adicionales
        /// 2. Procesar los parámetros SQL para evitar inyección
        /// 3. Ejecutar la consulta en la base de datos
        /// 4. Procesar y formatear los resultados
        /// 5. Aplicar límites de seguridad (máximo de registros)
        /// 
        /// Diferencias con el repositorio:
        /// - Repositorio: Acceso técnico directo a datos
        /// - Servicio: Aplica reglas de negocio y validaciones de seguridad
        /// 
        /// Proceso interno esperado:
        /// - Validar consulta usando ValidarConsultaSQL
        /// - Normalizar parámetros SQL
        /// - Aplicar límites máximos de registros
        /// - Ejecutar en base de datos vía repositorio
        /// - Transformar DataTable a formato JSON-friendly
        /// </summary>
        /// <param name="consulta">
        /// Consulta SQL parametrizada a ejecutar. Debe ser previamente validada.
        /// 
        /// Debe incluir placeholders para parámetros usando @ syntax:
        /// "SELECT * FROM productos WHERE precio > @precio AND categoria = @categoria"
        /// </param>
        /// <param name="parametros">
        /// Lista de parámetros SQL para la consulta.
        /// Cada parámetro debe tener nombre que coincida con los placeholders.
        /// 
        /// Ejemplo:
        /// new List<SqlParameter> {
        ///     new SqlParameter("@precio", 100),
        ///     new SqlParameter("@categoria", "Electronics")
        /// }
        /// </param>
        /// <param name="maximoRegistros">
        /// Límite máximo de registros a devolver para evitar consultas masivas.
        /// Valor típico entre 1000-10000 según las políticas de la aplicación.
        /// 
        /// El servicio puede aplicar este límite:
        /// - Agregando TOP/LIMIT a la consulta SQL
        /// - Truncando resultados después de la consulta
        /// - Aplicando límites específicos según el contexto
        /// </param>
        /// <param name="esquema">
        /// Esquema de base de datos a usar. Opcional.
        /// Si es null, se usa el esquema por defecto ("dbo" para SQL Server).
        /// 
        /// El servicio puede:
        /// - Aplicar esquemas por defecto inteligentes
        /// - Validar permisos de acceso al esquema
        /// - Modificar la consulta para incluir el esquema apropiado
        /// </param>
        /// <returns>
        /// DataTable con los resultados de la consulta ejecutada.
        /// 
        /// Estructura del DataTable:
        /// - Columnas: Corresponden a las columnas devueltas por la consulta SQL
        /// - Filas: Cada fila es un registro del resultado
        /// - Tipos: Los tipos de datos se mapean automáticamente desde SQL a .NET
        /// 
        /// El DataTable será posteriormente convertido a JSON por el controlador.
        /// Ventajas de usar DataTable vs Dictionary:
        /// - Mantiene información de tipos de columnas
        /// - Más eficiente para datasets grandes
        /// - Compatible con herramientas de análisis de datos
        /// 
        /// Si no hay resultados, devuelve DataTable vacío (Count = 0), no null.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Se lanza cuando hay problemas con los parámetros de entrada:
        /// - Consulta vacía o null
        /// - Parámetros con nombres inválidos 
        /// - MaximoRegistros con valor inválido (≤ 0 o demasiado alto)
        /// - Violación de reglas de seguridad básicas
        /// </exception>
        /// <exception cref="UnauthorizedAccessException">
        /// Se lanza cuando la consulta viola políticas de seguridad:
        /// - Intenta acceder a tablas prohibidas
        /// - Contiene palabras clave no permitidas
        /// - Intenta ejecutar operaciones no autorizadas
        /// - El esquema especificado no está permitido
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Se lanza cuando hay problemas en la ejecución:
        /// - Errores de sintaxis SQL
        /// - Problemas de conectividad con la base de datos
        /// - Tablas o columnas inexistentes
        /// - Errores de permisos en base de datos
        /// - Timeout en la consulta
        /// </exception>
        Task<DataTable> EjecutarConsultaParametrizadaAsync(
            string consulta,
            List<SqlParameter> parametros,
            int maximoRegistros,
            string? esquema
        );

        /// <summary>
        /// Método específico para el controlador que recibe parámetros en formato Dictionary JSON.
        /// 
        /// Este método es el punto de entrada desde el controlador web que recibe JSON.
        /// Convierte internamente Dictionary a List<SqlParameter> y usa el método principal
        /// para mantener la separación de responsabilidades y reutilizar validaciones.
        /// 
        /// Flujo interno:
        /// 1. Recibir Dictionary desde JSON del controlador
        /// 2. Convertir Dictionary<string, object?> a List<SqlParameter>
        /// 3. Aplicar valores por defecto para maximoRegistros y esquema
        /// 4. Llamar al método principal EjecutarConsultaParametrizadaAsync
        /// 5. Devolver el mismo DataTable para que el controlador lo convierta a JSON
        /// </summary>
        /// <param name="consulta">
        /// Consulta SQL parametrizada a ejecutar, mismas reglas que el método principal.
        /// </param>
        /// <param name="parametros">
        /// Diccionario de parámetros en formato JSON-friendly que viene del controlador.
        /// Será convertido internamente a List<SqlParameter> para ejecución segura.
        /// 
        /// Ejemplo de lo que recibe desde JSON:
        /// {
        ///   "precio": 100,
        ///   "@categoria": "Electronics"
        /// }
        /// 
        /// Se convierte internamente a:
        /// new List<SqlParameter> {
        ///     new SqlParameter("@precio", 100),
        ///     new SqlParameter("@categoria", "Electronics")
        /// }
        /// </param>
        /// <returns>
        /// El mismo DataTable que el método principal, listo para ser convertido a JSON por el controlador.
        /// </returns>
        Task<DataTable> EjecutarConsultaParametrizadaDesdeJsonAsync(
            string consulta,
            Dictionary<string, object?>? parametros
        );

        /// <summary>
        /// Ejecuta un procedimiento almacenado recibiendo parámetros en formato Dictionary JSON.
        /// 
        /// Similar a EjecutarConsultaParametrizadaDesdeJsonAsync pero para procedimientos almacenados.
        /// Aplica validaciones de seguridad y convierte parámetros de Dictionary a SqlParameter.
        /// </summary>
        /// <param name="nombreSP">
        /// Nombre del procedimiento almacenado a ejecutar.
        /// Debe ser un nombre válido existente en la base de datos.
        /// </param>
        /// <param name="parametros">
        /// Diccionario de parámetros en formato JSON-friendly.
        /// El servicio convertirá internamente a SqlParameter para ejecución segura.
        /// Si es null, ejecuta el procedimiento sin parámetros.
        /// </param>
        /// <param name="camposAEncriptar">
        /// Lista de nombres de campos que deben ser encriptados antes de la ejecución.
        /// Útil para campos como contraseñas que deben hashearse.
        /// Si es null o vacía, no se aplica encriptación.
        /// </param>
        /// <returns>
        /// DataTable con los resultados del procedimiento almacenado.
        /// Si el procedimiento no devuelve resultados, será un DataTable vacío.
        /// </returns>
        Task<DataTable> EjecutarProcedimientoAlmacenadoAsync(
            string nombreSP,
            Dictionary<string, object?>? parametros,
            List<string>? camposAEncriptar
        );

// aquí podría agregarse más documentación o métodos en el futuro si es necesario
    }
}

// NOTAS PEDAGÓGICAS para el tutorial:
//
// 1. ESTA ES UNA ABSTRACCIÓN ESPECIALIZADA:
//    - Define "QUÉ" operaciones se pueden hacer con consultas SQL parametrizadas
//    - NO define "CÓMO" se ejecutan (eso va en la implementación)
//    - Es complementaria a IServicioCrud (que maneja tablas completas)
//    - Se enfoca específicamente en consultas SQL arbitrarias con parámetros
//
// 2. ¿POR QUÉ DOS MÉTODOS SIMILARES?
//    - EjecutarConsultaParametrizadaAsync: método principal con SqlParameter (máximo control)
//    - EjecutarConsultaParametrizadaDesdeJsonAsync: método de conveniencia para controladores web
//    - Permite usar la interface desde diferentes contextos sin perder funcionalidad
//    - El método JSON internamente usa el método principal (reutilización de código)
//
// 3. ¿POR QUÉ USAR SQLPARAMETER EN LA INTERFACE?
//    - Aunque parece romper la abstracción, es necesario para:
//      * Prevenir inyección SQL (parámetros seguros)
//      * Mantener tipos de datos correctos
//      * Compatibilidad con diferentes proveedores SQL
//    - La alternativa sería Dictionary<string, object> pero perdemos tipado seguro
//    - SqlParameter es estándar en .NET para consultas parametrizadas
//
// 4. ¿POR QUÉ DEVOLVER DATATABLE?
//    - Más eficiente que List<Dictionary> para datasets grandes
//    - Mantiene información de esquema (tipos de columnas, metadatos)
//    - Compatible con herramientas de análisis y reporting
//    - Fácil conversión a JSON cuando sea necesario
//    - Estándar para operaciones de datos en .NET
//
// 5. VALIDACIONES DE SEGURIDAD INCLUIDAS:
//    - Solo consultas SELECT (previene modificaciones accidentales)
//    - Tablas prohibidas (configurables vía appsettings.json)
//    - Palabras clave peligrosas (DROP, DELETE, etc.)
//    - Límites de registros (previene consultas masivas)
//    - Validación de nombres de parámetros (previene inyección)
//
// 6. ¿QUÉ VIENE DESPUÉS?
//    - Crear ServicioConsultas.cs que implemente esta interface
//    - La implementación usará IRepositorioConsultas para acceso a datos
//    - Registrar en Program.cs: AddScoped<IServicioConsultas, ServicioConsultas>
//    - Usar desde EntidadesController el método EjecutarConsultaParametrizadaDesdeJsonAsync
