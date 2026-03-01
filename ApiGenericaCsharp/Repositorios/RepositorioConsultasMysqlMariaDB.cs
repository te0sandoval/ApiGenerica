// -----------------------------------------------------------------------------
// Archivo   : RepositorioConsultasMysqlMariaDB.cs
// Ruta      : ApiGenericaCsharp/Repositorios/RepositorioConsultasMysqlMariaDB.cs
// Propósito : Implementar IRepositorioConsultas para MySQL/MariaDB.
//             Expone consultas parametrizadas, validación de consulta, ejecución
//             de procedimientos/funciones y obtención de metadatos (esquemas y
//             estructura de tablas/base de datos).
// Dependencias:
//   - Paquetes NuGet: MySqlConnector (o MySql.Data), Microsoft.Data.SqlClient (para SqlParameter)
//   - Contratos: IRepositorioConsultas, IProveedorConexion
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Data.SqlClient;                     // Recibe SqlParameter según la interfaz

// -----------------------------------------------------------------------------
// Proveedor MySQL/MariaDB:
// Usar una de las dos opciones. Dejar activa solo una línea using.
// -----------------------------------------------------------------------------

// Opción 1: Conector alternativo (recomendado)
// Requiere paquete: MySqlConnector
using MySqlConnector;

// Opción 2: Conector oficial de Oracle
// Requiere paquete: MySql.Data
// using MySql.Data.MySqlClient;

using ApiGenericaCsharp.Repositorios.Abstracciones;      // IRepositorioConsultas
using ApiGenericaCsharp.Servicios.Abstracciones;         // IProveedorConexion

namespace ApiGenericaCsharp.Repositorios
{
    /// <summary>
    /// Implementación de IRepositorioConsultas para MySQL/MariaDB.
    /// Acepta parámetros tipo SqlParameter (por contrato) y los convierte a MySqlParameter.
    /// </summary>
    public sealed class RepositorioConsultasMysqlMariaDB : IRepositorioConsultas
    {
        private readonly IProveedorConexion _proveedorConexion;

        public RepositorioConsultasMysqlMariaDB(IProveedorConexion proveedorConexion)
        {
            _proveedorConexion = proveedorConexion ?? throw new ArgumentNullException(nameof(proveedorConexion));
        }

        // =========================================================================
        // MÉTODOS CON SQLPARAMETER (IMPLEMENTACIÓN ORIGINAL)
        // =========================================================================

        /// <summary>
        /// Ejecuta consulta SQL parametrizada → DataTable
        /// </summary>
        public async Task<DataTable> EjecutarConsultaParametrizadaAsync(
            string consultaSQL,
            List<SqlParameter>? parametros
        )
        {
            if (string.IsNullOrWhiteSpace(consultaSQL))
                throw new ArgumentException("La consulta SQL no puede estar vacía.", nameof(consultaSQL));

            var cadena = _proveedorConexion.ObtenerCadenaConexion();
            var tabla = new DataTable();

            await using var conexion = new MySqlConnection(cadena);
            await conexion.OpenAsync();

            await using var comando = new MySqlCommand(consultaSQL, conexion);
            AgregarParametros(comando, parametros);

            await using var lector = await comando.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
            tabla.Load(lector);
            return tabla;
        }

        /// <summary>
        /// Valida consulta SQL (sin ejecutar realmente)
        /// </summary>
        public async Task<(bool esValida, string? mensajeError)> ValidarConsultaAsync(
            string consultaSQL,
            List<SqlParameter>? parametros
        )
        {
            if (string.IsNullOrWhiteSpace(consultaSQL))
                return (false, "La consulta SQL está vacía.");

            try
            {
                var cadena = _proveedorConexion.ObtenerCadenaConexion();
                await using var conexion = new MySqlConnection(cadena);
                await conexion.OpenAsync();

                await using var comando = new MySqlCommand(consultaSQL, conexion);
                AgregarParametros(comando, parametros);

                var esSelect = consultaSQL.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);
                if (esSelect)
                {
                    await using var validar = new MySqlCommand($"EXPLAIN {consultaSQL}", conexion);
                    CopiarParametros(comando, validar);
                    await using var lectorValidar = await validar.ExecuteReaderAsync();
                }
                else
                {
                    _ = comando.CommandText;
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Ejecuta procedimiento almacenado → DataTable
        /// </summary>
        public async Task<DataTable> EjecutarProcedimientoAlmacenadoAsync(
            string nombreSP,
            List<SqlParameter>? parametros
        )
        {
            if (string.IsNullOrWhiteSpace(nombreSP))
                throw new ArgumentException("El nombre del procedimiento no puede estar vacío.", nameof(nombreSP));

            var cadena = _proveedorConexion.ObtenerCadenaConexion();
            var tabla = new DataTable();
            var placeholders = ConstruirPlaceholders(parametros);

            await using var conexion = new MySqlConnection(cadena);
            await conexion.OpenAsync();

            // MySQL: CALL nombreSP(@a, @b, ...)
            string sqlCall = $"CALL {nombreSP}({placeholders})";
            await using var cmdCall = new MySqlCommand(sqlCall, conexion);
            AgregarParametros(cmdCall, parametros);

            await using var lectorCall = await cmdCall.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
            tabla.Load(lectorCall);

            return tabla;
        }

        // =========================================================================
        // MÉTODOS CON DICTIONARY (REQUERIDOS POR LA INTERFAZ)
        // =========================================================================

        /// <summary>
        /// Ejecuta consulta SQL parametrizada usando Dictionary - MÉTODO REQUERIDO POR INTERFAZ
        /// </summary>
        public async Task<DataTable> EjecutarConsultaParametrizadaConDictionaryAsync(
            string consultaSQL,
            Dictionary<string, object?> parametros,
            int maximoRegistros = 10000,
            string? esquema = null)
        {
            var listaParametros = ConvertirDictionaryASqlParameter(parametros);
            return await EjecutarConsultaParametrizadaAsync(consultaSQL, listaParametros);
        }

        /// <summary>
        /// Valida consulta SQL con Dictionary - MÉTODO REQUERIDO POR INTERFAZ
        /// </summary>
        public async Task<(bool esValida, string? mensajeError)> ValidarConsultaConDictionaryAsync(
            string consultaSQL,
            Dictionary<string, object?> parametros)
        {
            var listaParametros = ConvertirDictionaryASqlParameter(parametros);
            return await ValidarConsultaAsync(consultaSQL, listaParametros);
        }

        /// <summary>
        /// Ejecuta procedimiento almacenado con Dictionary - MÉTODO REQUERIDO POR INTERFAZ
        /// </summary>
        public async Task<DataTable> EjecutarProcedimientoAlmacenadoConDictionaryAsync(
            string nombreSP,
            Dictionary<string, object?> parametros)
        {
            var listaParametros = ConvertirDictionaryASqlParameter(parametros);
            return await EjecutarProcedimientoAlmacenadoAsync(nombreSP, listaParametros);
        }

        // =========================================================================
        // MÉTODOS DE METADATOS
        // =========================================================================

        /// <summary>
        /// Obtiene el esquema real de una tabla
        /// </summary>
        public async Task<string?> ObtenerEsquemaTablaAsync(string nombreTabla, string? esquemaPredeterminado)
        {
            if (string.IsNullOrWhiteSpace(nombreTabla))
                throw new ArgumentException("El nombre de la tabla no puede estar vacío.", nameof(nombreTabla));

            var cadena = _proveedorConexion.ObtenerCadenaConexion();
            await using var conexion = new MySqlConnection(cadena);
            await conexion.OpenAsync();

            // Buscar primero en el esquema indicado
            const string sql1 = @"
                SELECT TABLE_SCHEMA
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = @esquema AND TABLE_NAME = @tabla
                LIMIT 1;";

            await using (var cmd1 = new MySqlCommand(sql1, conexion))
            {
                cmd1.Parameters.AddWithValue("@esquema", string.IsNullOrWhiteSpace(esquemaPredeterminado) ? DatabaseActual(conexion) : esquemaPredeterminado);
                cmd1.Parameters.AddWithValue("@tabla", nombreTabla);

                var r1 = await cmd1.ExecuteScalarAsync();
                if (r1 != null && r1 is string s1) return s1;
            }

            // Si no está, buscar en cualquier esquema visible
            const string sql2 = @"
                SELECT TABLE_SCHEMA
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_NAME = @tabla
                ORDER BY TABLE_SCHEMA
                LIMIT 1;";

            await using var cmd2 = new MySqlCommand(sql2, conexion);
            cmd2.Parameters.AddWithValue("@tabla", nombreTabla);

            var r2 = await cmd2.ExecuteScalarAsync();
            return r2 == null ? null : Convert.ToString(r2);
        }

        /// <summary>
        /// Obtiene la estructura detallada de una tabla → DataTable
        /// </summary>
        public async Task<DataTable> ObtenerEstructuraTablaAsync(string nombreTabla, string esquema)
        {
            if (string.IsNullOrWhiteSpace(nombreTabla))
                throw new ArgumentException("El nombre de la tabla no puede estar vacío.", nameof(nombreTabla));

            var cadena = _proveedorConexion.ObtenerCadenaConexion();
            var tabla = new DataTable();

            // Query completa con constraints (PK, FK, UNIQUE, CHECK, DEFAULT)
            const string sql = @"
                SELECT
                    c.COLUMN_NAME AS column_name,
                    c.DATA_TYPE AS data_type,
                    c.CHARACTER_MAXIMUM_LENGTH AS character_maximum_length,
                    c.NUMERIC_PRECISION AS numeric_precision,
                    c.NUMERIC_SCALE AS numeric_scale,
                    c.IS_NULLABLE AS is_nullable,
                    c.COLUMN_DEFAULT AS column_default,
                    c.ORDINAL_POSITION AS ordinal_position,
                    CASE WHEN c.COLUMN_KEY = 'PRI' THEN 'YES' ELSE 'NO' END AS is_primary_key,
                    CASE WHEN c.COLUMN_KEY = 'UNI' THEN 'YES' ELSE 'NO' END AS is_unique,
                    CASE WHEN c.EXTRA LIKE '%auto_increment%' THEN 'YES' ELSE 'NO' END AS is_identity,
                    fk.REFERENCED_TABLE_NAME AS foreign_table_name,
                    fk.REFERENCED_COLUMN_NAME AS foreign_column_name,
                    fk.CONSTRAINT_NAME AS fk_constraint_name,
                    chk.CHECK_CLAUSE AS check_clause
                FROM INFORMATION_SCHEMA.COLUMNS c
                -- Foreign Key
                LEFT JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE fk
                    ON c.TABLE_SCHEMA = fk.TABLE_SCHEMA
                    AND c.TABLE_NAME = fk.TABLE_NAME
                    AND c.COLUMN_NAME = fk.COLUMN_NAME
                    AND fk.REFERENCED_TABLE_NAME IS NOT NULL
                -- Check constraints (MySQL 8.0.16+)
                LEFT JOIN INFORMATION_SCHEMA.CHECK_CONSTRAINTS chk
                    ON c.TABLE_SCHEMA = chk.CONSTRAINT_SCHEMA
                    AND chk.CONSTRAINT_NAME LIKE CONCAT(c.TABLE_NAME, '_chk_%')
                WHERE c.TABLE_SCHEMA = @esquema
                  AND c.TABLE_NAME = @tabla
                ORDER BY c.ORDINAL_POSITION;";

            await using var conexion = new MySqlConnection(cadena);
            await conexion.OpenAsync();

            await using var cmd = new MySqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@esquema", string.IsNullOrWhiteSpace(esquema) ? DatabaseActual(conexion) : esquema);
            cmd.Parameters.AddWithValue("@tabla", nombreTabla);

            await using var lector = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
            tabla.Load(lector);
            return tabla;
        }

        /// <summary>
        /// Obtiene la estructura completa de la base de datos → DataTable
        /// </summary>
        public async Task<DataTable> ObtenerEstructuraBaseDatosAsync()
        {
            var cadena = _proveedorConexion.ObtenerCadenaConexion();
            var tabla = new DataTable();

            const string sql = @"
                SELECT
                    c.TABLE_NAME AS table_name,
                    c.COLUMN_NAME AS column_name,
                    c.DATA_TYPE AS data_type,
                    c.CHARACTER_MAXIMUM_LENGTH AS character_maximum_length,
                    c.IS_NULLABLE AS is_nullable
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_SCHEMA = DATABASE()
                ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;";

            await using var conexion = new MySqlConnection(cadena);
            await conexion.OpenAsync();

            await using var cmd = new MySqlCommand(sql, conexion);

            await using var lector = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
            tabla.Load(lector);
            return tabla;
        }

        public async Task<Dictionary<string, object>> ObtenerEstructuraCompletaBaseDatosAsync()
        {
            var resultado = new Dictionary<string, object>();
            var cadena = _proveedorConexion.ObtenerCadenaConexion();

            await using var conexion = new MySqlConnection(cadena);
            await conexion.OpenAsync();

            var esquemaActual = DatabaseActual(conexion);

            // 1. Tablas con columnas y constraints
            resultado["tablas"] = await ObtenerTablasConColumnasAsync(conexion, esquemaActual);

            // 2. Vistas
            resultado["vistas"] = await ObtenerVistasAsync(conexion, esquemaActual);

            // 3. Procedimientos almacenados
            resultado["procedimientos"] = await ObtenerProcedimientosAsync(conexion, esquemaActual);

            // 4. Funciones
            resultado["funciones"] = await ObtenerFuncionesAsync(conexion, esquemaActual);

            // 5. Triggers
            resultado["triggers"] = await ObtenerTriggersAsync(conexion, esquemaActual);

            // 6. Índices
            resultado["indices"] = await ObtenerIndicesAsync(conexion, esquemaActual);

            // 7. Eventos (MySQL scheduler)
            resultado["eventos"] = await ObtenerEventosAsync(conexion, esquemaActual);

            return resultado;
        }

        // =========================================================================
        // MÉTODOS AUXILIARES PARA ESTRUCTURA COMPLETA DE BD
        // =========================================================================

        private async Task<List<Dictionary<string, object?>>> ObtenerTablasConColumnasAsync(MySqlConnection conexion, string esquema)
        {
            var tablas = new List<Dictionary<string, object?>>();

            string sqlTablas = @"
                SELECT
                    TABLE_SCHEMA,
                    TABLE_NAME,
                    TABLE_COMMENT,
                    ENGINE,
                    TABLE_ROWS,
                    AUTO_INCREMENT,
                    TABLE_COLLATION
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = @esquema
                  AND TABLE_TYPE = 'BASE TABLE'
                ORDER BY TABLE_NAME";

            await using var cmdTablas = new MySqlCommand(sqlTablas, conexion);
            cmdTablas.Parameters.AddWithValue("@esquema", esquema);
            await using var readerTablas = await cmdTablas.ExecuteReaderAsync();

            var listaTablas = new List<(string Schema, string Tabla, string? Comentario, string? Engine, long? Rows, long? AutoInc, string? Collation)>();
            while (await readerTablas.ReadAsync())
            {
                listaTablas.Add((
                    readerTablas.GetString(0),
                    readerTablas.GetString(1),
                    readerTablas.IsDBNull(2) ? null : readerTablas.GetString(2),
                    readerTablas.IsDBNull(3) ? null : readerTablas.GetString(3),
                    readerTablas.IsDBNull(4) ? null : readerTablas.GetInt64(4),
                    readerTablas.IsDBNull(5) ? null : readerTablas.GetInt64(5),
                    readerTablas.IsDBNull(6) ? null : readerTablas.GetString(6)
                ));
            }
            await readerTablas.CloseAsync();

            foreach (var (schema, tabla, comentario, engine, rows, autoInc, collation) in listaTablas)
            {
                var tablaDict = new Dictionary<string, object?>
                {
                    ["schema"] = schema,
                    ["nombre"] = tabla,
                    ["comentario"] = comentario,
                    ["engine"] = engine,
                    ["filas_estimadas"] = rows,
                    ["auto_increment"] = autoInc,
                    ["collation"] = collation,
                    ["columnas"] = await ObtenerColumnasTablaAsync(conexion, schema, tabla),
                    ["foreign_keys"] = await ObtenerForeignKeysTablaAsync(conexion, schema, tabla),
                    ["indices"] = await ObtenerIndicesTablaAsync(conexion, schema, tabla)
                };
                tablas.Add(tablaDict);
            }

            return tablas;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerColumnasTablaAsync(MySqlConnection conexion, string schema, string tabla)
        {
            var columnas = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    c.COLUMN_NAME,
                    c.DATA_TYPE,
                    c.COLUMN_TYPE,
                    c.CHARACTER_MAXIMUM_LENGTH,
                    c.NUMERIC_PRECISION,
                    c.NUMERIC_SCALE,
                    c.IS_NULLABLE,
                    c.COLUMN_DEFAULT,
                    c.ORDINAL_POSITION,
                    c.COLUMN_KEY,
                    c.EXTRA,
                    c.COLUMN_COMMENT
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_SCHEMA = @schema AND c.TABLE_NAME = @tabla
                ORDER BY c.ORDINAL_POSITION";

            await using var cmd = new MySqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@tabla", tabla);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var columnKey = reader.IsDBNull(9) ? "" : reader.GetString(9);
                var extra = reader.IsDBNull(10) ? "" : reader.GetString(10);

                columnas.Add(new Dictionary<string, object?>
                {
                    ["nombre"] = reader.GetString(0),
                    ["tipo"] = reader.GetString(1),
                    ["tipo_completo"] = reader.GetString(2),
                    ["longitud_maxima"] = reader.IsDBNull(3) ? null : reader.GetInt64(3),
                    ["precision"] = reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    ["escala"] = reader.IsDBNull(5) ? null : reader.GetInt64(5),
                    ["nullable"] = reader.GetString(6) == "YES",
                    ["valor_default"] = reader.IsDBNull(7) ? null : reader.GetString(7),
                    ["posicion"] = reader.GetInt32(8),
                    ["es_primary_key"] = columnKey == "PRI",
                    ["es_unique"] = columnKey == "UNI",
                    ["es_auto_increment"] = extra.Contains("auto_increment"),
                    ["comentario"] = reader.IsDBNull(11) ? null : reader.GetString(11)
                });
            }

            return columnas;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerForeignKeysTablaAsync(MySqlConnection conexion, string schema, string tabla)
        {
            var fks = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    kcu.CONSTRAINT_NAME,
                    kcu.COLUMN_NAME,
                    kcu.REFERENCED_TABLE_SCHEMA,
                    kcu.REFERENCED_TABLE_NAME,
                    kcu.REFERENCED_COLUMN_NAME,
                    rc.UPDATE_RULE,
                    rc.DELETE_RULE
                FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                JOIN INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
                    ON kcu.CONSTRAINT_SCHEMA = rc.CONSTRAINT_SCHEMA
                    AND kcu.CONSTRAINT_NAME = rc.CONSTRAINT_NAME
                WHERE kcu.TABLE_SCHEMA = @schema
                  AND kcu.TABLE_NAME = @tabla
                  AND kcu.REFERENCED_TABLE_NAME IS NOT NULL
                ORDER BY kcu.CONSTRAINT_NAME, kcu.ORDINAL_POSITION";

            await using var cmd = new MySqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@tabla", tabla);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                fks.Add(new Dictionary<string, object?>
                {
                    // Nombres compatibles con GeneradorSPController.DetectarRelacionesMaestroDetalle
                    ["constraint_name"] = reader.GetString(0),
                    ["column_name"] = reader.GetString(1),
                    ["foreign_schema"] = reader.IsDBNull(2) ? null : reader.GetString(2),
                    ["foreign_table_name"] = reader.GetString(3),
                    ["foreign_column_name"] = reader.GetString(4),
                    ["on_update"] = reader.GetString(5),
                    ["on_delete"] = reader.GetString(6)
                });
            }

            return fks;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerIndicesTablaAsync(MySqlConnection conexion, string schema, string tabla)
        {
            var indices = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    INDEX_NAME,
                    INDEX_TYPE,
                    NON_UNIQUE,
                    GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX SEPARATOR ', ') AS columns,
                    NULLABLE
                FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @tabla
                GROUP BY INDEX_NAME, INDEX_TYPE, NON_UNIQUE, NULLABLE
                ORDER BY INDEX_NAME";

            await using var cmd = new MySqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@tabla", tabla);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                indices.Add(new Dictionary<string, object?>
                {
                    ["nombre"] = reader.GetString(0),
                    ["tipo"] = reader.GetString(1),
                    ["es_unique"] = reader.GetInt32(2) == 0,
                    ["columnas"] = reader.GetString(3),
                    ["nullable"] = reader.IsDBNull(4) ? null : reader.GetString(4)
                });
            }

            return indices;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerVistasAsync(MySqlConnection conexion, string esquema)
        {
            var vistas = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    TABLE_SCHEMA,
                    TABLE_NAME,
                    VIEW_DEFINITION,
                    CHECK_OPTION,
                    IS_UPDATABLE,
                    SECURITY_TYPE
                FROM INFORMATION_SCHEMA.VIEWS
                WHERE TABLE_SCHEMA = @esquema
                ORDER BY TABLE_NAME";

            await using var cmd = new MySqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@esquema", esquema);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                vistas.Add(new Dictionary<string, object?>
                {
                    ["schema"] = reader.GetString(0),
                    ["nombre"] = reader.GetString(1),
                    ["definicion"] = reader.IsDBNull(2) ? null : reader.GetString(2),
                    ["check_option"] = reader.GetString(3),
                    ["es_actualizable"] = reader.GetString(4) == "YES",
                    ["tipo_seguridad"] = reader.GetString(5)
                });
            }

            return vistas;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerProcedimientosAsync(MySqlConnection conexion, string esquema)
        {
            var procedimientos = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    ROUTINE_SCHEMA,
                    ROUTINE_NAME,
                    ROUTINE_DEFINITION,
                    DATA_TYPE,
                    CREATED,
                    LAST_ALTERED,
                    ROUTINE_COMMENT,
                    SECURITY_TYPE,
                    SQL_MODE,
                    DEFINER
                FROM INFORMATION_SCHEMA.ROUTINES
                WHERE ROUTINE_SCHEMA = @esquema
                  AND ROUTINE_TYPE = 'PROCEDURE'
                ORDER BY ROUTINE_NAME";

            await using var cmdProcs = new MySqlCommand(sql, conexion);
            cmdProcs.Parameters.AddWithValue("@esquema", esquema);
            await using var readerProcs = await cmdProcs.ExecuteReaderAsync();

            var listaProcs = new List<(string Schema, string Nombre, string? Definicion, string? DataType, DateTime? Created, DateTime? Modified, string? Comentario, string? Security, string? SqlMode, string? Definer)>();
            while (await readerProcs.ReadAsync())
            {
                listaProcs.Add((
                    readerProcs.GetString(0),
                    readerProcs.GetString(1),
                    readerProcs.IsDBNull(2) ? null : readerProcs.GetString(2),
                    readerProcs.IsDBNull(3) ? null : readerProcs.GetString(3),
                    readerProcs.IsDBNull(4) ? null : readerProcs.GetDateTime(4),
                    readerProcs.IsDBNull(5) ? null : readerProcs.GetDateTime(5),
                    readerProcs.IsDBNull(6) ? null : readerProcs.GetString(6),
                    readerProcs.IsDBNull(7) ? null : readerProcs.GetString(7),
                    readerProcs.IsDBNull(8) ? null : readerProcs.GetString(8),
                    readerProcs.IsDBNull(9) ? null : readerProcs.GetString(9)
                ));
            }
            await readerProcs.CloseAsync();

            foreach (var (schema, nombre, definicion, dataType, created, modified, comentario, security, sqlMode, definer) in listaProcs)
            {
                var procDict = new Dictionary<string, object?>
                {
                    ["schema"] = schema,
                    ["nombre"] = nombre,
                    ["definicion"] = definicion,
                    ["tipo_retorno"] = dataType,
                    ["fecha_creacion"] = created,
                    ["fecha_modificacion"] = modified,
                    ["comentario"] = comentario,
                    ["tipo_seguridad"] = security,
                    ["sql_mode"] = sqlMode,
                    ["definer"] = definer,
                    ["parametros"] = await ObtenerParametrosRutinaAsync(conexion, schema, nombre)
                };
                procedimientos.Add(procDict);
            }

            return procedimientos;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerFuncionesAsync(MySqlConnection conexion, string esquema)
        {
            var funciones = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    ROUTINE_SCHEMA,
                    ROUTINE_NAME,
                    ROUTINE_DEFINITION,
                    DATA_TYPE,
                    CREATED,
                    LAST_ALTERED,
                    ROUTINE_COMMENT,
                    SECURITY_TYPE,
                    IS_DETERMINISTIC,
                    DEFINER
                FROM INFORMATION_SCHEMA.ROUTINES
                WHERE ROUTINE_SCHEMA = @esquema
                  AND ROUTINE_TYPE = 'FUNCTION'
                ORDER BY ROUTINE_NAME";

            await using var cmdFuncs = new MySqlCommand(sql, conexion);
            cmdFuncs.Parameters.AddWithValue("@esquema", esquema);
            await using var readerFuncs = await cmdFuncs.ExecuteReaderAsync();

            var listaFuncs = new List<(string Schema, string Nombre, string? Definicion, string? ReturnType, DateTime? Created, DateTime? Modified, string? Comentario, string? Security, string? IsDeterministic, string? Definer)>();
            while (await readerFuncs.ReadAsync())
            {
                listaFuncs.Add((
                    readerFuncs.GetString(0),
                    readerFuncs.GetString(1),
                    readerFuncs.IsDBNull(2) ? null : readerFuncs.GetString(2),
                    readerFuncs.IsDBNull(3) ? null : readerFuncs.GetString(3),
                    readerFuncs.IsDBNull(4) ? null : readerFuncs.GetDateTime(4),
                    readerFuncs.IsDBNull(5) ? null : readerFuncs.GetDateTime(5),
                    readerFuncs.IsDBNull(6) ? null : readerFuncs.GetString(6),
                    readerFuncs.IsDBNull(7) ? null : readerFuncs.GetString(7),
                    readerFuncs.IsDBNull(8) ? null : readerFuncs.GetString(8),
                    readerFuncs.IsDBNull(9) ? null : readerFuncs.GetString(9)
                ));
            }
            await readerFuncs.CloseAsync();

            foreach (var (schema, nombre, definicion, returnType, created, modified, comentario, security, isDeterministic, definer) in listaFuncs)
            {
                var funcDict = new Dictionary<string, object?>
                {
                    ["schema"] = schema,
                    ["nombre"] = nombre,
                    ["definicion"] = definicion,
                    ["tipo_retorno"] = returnType,
                    ["fecha_creacion"] = created,
                    ["fecha_modificacion"] = modified,
                    ["comentario"] = comentario,
                    ["tipo_seguridad"] = security,
                    ["es_deterministica"] = isDeterministic == "YES",
                    ["definer"] = definer,
                    ["parametros"] = await ObtenerParametrosRutinaAsync(conexion, schema, nombre)
                };
                funciones.Add(funcDict);
            }

            return funciones;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerParametrosRutinaAsync(MySqlConnection conexion, string schema, string rutina)
        {
            var parametros = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    PARAMETER_NAME,
                    DATA_TYPE,
                    CHARACTER_MAXIMUM_LENGTH,
                    NUMERIC_PRECISION,
                    NUMERIC_SCALE,
                    PARAMETER_MODE,
                    ORDINAL_POSITION
                FROM INFORMATION_SCHEMA.PARAMETERS
                WHERE SPECIFIC_SCHEMA = @schema
                  AND SPECIFIC_NAME = @rutina
                  AND PARAMETER_NAME IS NOT NULL
                ORDER BY ORDINAL_POSITION";

            await using var cmd = new MySqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@rutina", rutina);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                parametros.Add(new Dictionary<string, object?>
                {
                    ["nombre"] = reader.GetString(0),
                    ["tipo"] = reader.IsDBNull(1) ? null : reader.GetString(1),
                    ["longitud_maxima"] = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                    ["precision"] = reader.IsDBNull(3) ? null : reader.GetInt64(3),
                    ["escala"] = reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    ["direccion"] = reader.IsDBNull(5) ? null : reader.GetString(5),
                    ["posicion"] = reader.GetInt32(6)
                });
            }

            return parametros;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerTriggersAsync(MySqlConnection conexion, string esquema)
        {
            var triggers = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    TRIGGER_SCHEMA,
                    TRIGGER_NAME,
                    EVENT_MANIPULATION,
                    EVENT_OBJECT_SCHEMA,
                    EVENT_OBJECT_TABLE,
                    ACTION_TIMING,
                    ACTION_STATEMENT,
                    CREATED,
                    DEFINER
                FROM INFORMATION_SCHEMA.TRIGGERS
                WHERE TRIGGER_SCHEMA = @esquema
                ORDER BY EVENT_OBJECT_TABLE, TRIGGER_NAME";

            await using var cmd = new MySqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@esquema", esquema);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                triggers.Add(new Dictionary<string, object?>
                {
                    ["schema"] = reader.GetString(0),
                    ["nombre"] = reader.GetString(1),
                    ["evento"] = reader.GetString(2),
                    ["schema_tabla"] = reader.GetString(3),
                    ["tabla"] = reader.GetString(4),
                    ["timing"] = reader.GetString(5),
                    ["cuerpo"] = reader.IsDBNull(6) ? null : reader.GetString(6),
                    ["fecha_creacion"] = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    ["definer"] = reader.IsDBNull(8) ? null : reader.GetString(8)
                });
            }

            return triggers;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerIndicesAsync(MySqlConnection conexion, string esquema)
        {
            var indices = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    TABLE_SCHEMA,
                    TABLE_NAME,
                    INDEX_NAME,
                    INDEX_TYPE,
                    NON_UNIQUE,
                    GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX SEPARATOR ', ') AS columns,
                    NULLABLE
                FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = @esquema
                GROUP BY TABLE_SCHEMA, TABLE_NAME, INDEX_NAME, INDEX_TYPE, NON_UNIQUE, NULLABLE
                ORDER BY TABLE_NAME, INDEX_NAME";

            await using var cmd = new MySqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@esquema", esquema);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                indices.Add(new Dictionary<string, object?>
                {
                    ["schema"] = reader.GetString(0),
                    ["tabla"] = reader.GetString(1),
                    ["nombre"] = reader.GetString(2),
                    ["tipo"] = reader.GetString(3),
                    ["es_unique"] = reader.GetInt32(4) == 0,
                    ["columnas"] = reader.GetString(5),
                    ["nullable"] = reader.IsDBNull(6) ? null : reader.GetString(6)
                });
            }

            return indices;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerEventosAsync(MySqlConnection conexion, string esquema)
        {
            var eventos = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    EVENT_SCHEMA,
                    EVENT_NAME,
                    EVENT_DEFINITION,
                    EVENT_TYPE,
                    EXECUTE_AT,
                    INTERVAL_VALUE,
                    INTERVAL_FIELD,
                    STARTS,
                    ENDS,
                    STATUS,
                    ON_COMPLETION,
                    CREATED,
                    LAST_ALTERED,
                    EVENT_COMMENT,
                    DEFINER
                FROM INFORMATION_SCHEMA.EVENTS
                WHERE EVENT_SCHEMA = @esquema
                ORDER BY EVENT_NAME";

            await using var cmd = new MySqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@esquema", esquema);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                eventos.Add(new Dictionary<string, object?>
                {
                    ["schema"] = reader.GetString(0),
                    ["nombre"] = reader.GetString(1),
                    ["definicion"] = reader.IsDBNull(2) ? null : reader.GetString(2),
                    ["tipo"] = reader.GetString(3),
                    ["ejecutar_en"] = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                    ["intervalo_valor"] = reader.IsDBNull(5) ? null : reader.GetString(5),
                    ["intervalo_campo"] = reader.IsDBNull(6) ? null : reader.GetString(6),
                    ["inicio"] = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    ["fin"] = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                    ["estado"] = reader.GetString(9),
                    ["al_completar"] = reader.GetString(10),
                    ["fecha_creacion"] = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                    ["fecha_modificacion"] = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
                    ["comentario"] = reader.IsDBNull(13) ? null : reader.GetString(13),
                    ["definer"] = reader.IsDBNull(14) ? null : reader.GetString(14)
                });
            }

            return eventos;
        }

        // =========================================================================
        // MÉTODOS AUXILIARES
        // =========================================================================

        /// <summary>
        /// Convierte Dictionary a List<SqlParameter> para reutilizar métodos existentes
        /// </summary>
        private static List<SqlParameter> ConvertirDictionaryASqlParameter(Dictionary<string, object?>? parametros)
        {
            var lista = new List<SqlParameter>();
            if (parametros == null || parametros.Count == 0) return lista;

            foreach (var kvp in parametros)
            {
                string nombre = kvp.Key.StartsWith("@") ? kvp.Key : $"@{kvp.Key}";
                object? valor = kvp.Value ?? DBNull.Value;
                lista.Add(new SqlParameter(nombre, valor));
            }
            return lista;
        }

        /// <summary>
        /// Convierte y agrega parámetros SqlParameter a MySqlCommand
        /// </summary>
        private static void AgregarParametros(MySqlCommand comando, List<SqlParameter>? parametros)
        {
            if (parametros == null || parametros.Count == 0) return;

            foreach (var p in parametros)
            {
                string nombre = p.ParameterName?.StartsWith("@") == true ? p.ParameterName : $"@{p.ParameterName}";

                var mp = new MySqlParameter(nombre, p.Value ?? DBNull.Value)
                {
                    Direction = p.Direction switch
                    {
                        ParameterDirection.Input => ParameterDirection.Input,
                        ParameterDirection.Output => ParameterDirection.Output,
                        ParameterDirection.InputOutput => ParameterDirection.InputOutput,
                        ParameterDirection.ReturnValue => ParameterDirection.ReturnValue,
                        _ => ParameterDirection.Input
                    }
                };

                if (p.Size > 0) mp.Size = p.Size;

                if (p.DbType != DbType.Object && p.DbType != 0)
                {
                    mp.DbType = p.DbType;
                }

                comando.Parameters.Add(mp);
            }
        }

        /// <summary>
        /// Copia parámetros de un comando a otro
        /// </summary>
        private static void CopiarParametros(MySqlCommand origen, MySqlCommand destino)
        {
            foreach (MySqlParameter p in origen.Parameters)
            {
                var copia = new MySqlParameter(p.ParameterName, p.Value)
                {
                    Direction = p.Direction,
                    Size = p.Size,
                    DbType = p.DbType
                };
                destino.Parameters.Add(copia);
            }
        }

        /// <summary>
        /// Construye la lista de placeholders "@a, @b, @c" para CALL/SELECT
        /// </summary>
        private static string ConstruirPlaceholders(List<SqlParameter>? parametros)
        {
            if (parametros == null || parametros.Count == 0) return string.Empty;

            var nombres = new List<string>(parametros.Count);
            foreach (var p in parametros)
            {
                string nombre = p.ParameterName?.StartsWith("@") == true ? p.ParameterName : $"@{p.ParameterName}";
                nombres.Add(nombre);
            }
            return string.Join(", ", nombres);
        }

        /// <summary>
        /// Obtiene el nombre de la base de datos actual para la conexión dada
        /// </summary>
        private static string DatabaseActual(MySqlConnection conexion)
        {
            return string.IsNullOrWhiteSpace(conexion.Database) ? "information_schema" : conexion.Database;
        }
    }
}

