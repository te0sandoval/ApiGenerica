// --------------------------------------------------------------
// Archivo: RepositorioConsultasSqlServer.cs 
// Ruta: ApiGenericaCsharp/Repositorios/RepositorioConsultasSqlServer.cs
// Mejora: Manejo inteligente de DateTime con hora 00:00:00 como DATE
//         EN CONSULTAS Y PROCEDIMIENTOS ALMACENADOS
// --------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using ApiGenericaCsharp.Repositorios.Abstracciones;
using ApiGenericaCsharp.Servicios.Abstracciones;

namespace ApiGenericaCsharp.Repositorios
{
    /// <summary>
    /// Implementación de repositorio para ejecutar consultas y procedimientos almacenados en SQL Server.
    /// 
    /// MEJORA IMPLEMENTADA:
    /// Detecta DateTime con hora 00:00:00 y los convierte a DateOnly automáticamente
    /// tanto en consultas como en procedimientos almacenados.
    /// </summary>
    public sealed class RepositorioConsultasSqlServer : IRepositorioConsultas
    {
        private readonly IProveedorConexion _proveedorConexion;

        public RepositorioConsultasSqlServer(IProveedorConexion proveedorConexion)
        {
            _proveedorConexion = proveedorConexion ?? throw new ArgumentNullException(nameof(proveedorConexion));
        }

        // ================================================================
        // MÉTODO AUXILIAR: Mapea tipos de datos de SQL Server a SqlDbType
        // ================================================================
        private SqlDbType MapearTipo(string tipo)
        {
            return tipo.ToLower() switch
            {
                "varchar" => SqlDbType.VarChar,
                "nvarchar" => SqlDbType.NVarChar,
                "char" => SqlDbType.Char,
                "nchar" => SqlDbType.NChar,
                "text" => SqlDbType.Text,
                "ntext" => SqlDbType.NText,
                "int" => SqlDbType.Int,
                "bigint" => SqlDbType.BigInt,
                "smallint" => SqlDbType.SmallInt,
                "tinyint" => SqlDbType.TinyInt,
                "bit" => SqlDbType.Bit,
                "decimal" => SqlDbType.Decimal,
                "numeric" => SqlDbType.Decimal,
                "money" => SqlDbType.Money,
                "smallmoney" => SqlDbType.SmallMoney,
                "float" => SqlDbType.Float,
                "real" => SqlDbType.Real,
                "datetime" => SqlDbType.DateTime,
                "datetime2" => SqlDbType.DateTime2,
                "smalldatetime" => SqlDbType.SmallDateTime,
                "date" => SqlDbType.Date,
                "time" => SqlDbType.Time,
                "datetimeoffset" => SqlDbType.DateTimeOffset,
                "uniqueidentifier" => SqlDbType.UniqueIdentifier,
                "binary" => SqlDbType.Binary,
                "varbinary" => SqlDbType.VarBinary,
                "image" => SqlDbType.Image,
                "xml" => SqlDbType.Xml,
                _ => SqlDbType.NVarChar
            };
        }

        // ================================================================
        // MÉTODO AUXILIAR: Obtiene metadatos de parámetros de un SP en SQL Server
        // ================================================================
        private async Task<List<(string Nombre, bool EsOutput, string Tipo, int? MaxLength)>> ObtenerMetadatosParametrosAsync(
            SqlConnection conexion,
            string nombreSP)
        {
            var lista = new List<(string, bool, string, int?)>();

            string sql = @"
                SELECT 
                    PARAMETER_NAME,
                    CASE WHEN PARAMETER_MODE = 'OUT' OR PARAMETER_MODE = 'INOUT' THEN 1 ELSE 0 END AS IsOutput,
                    DATA_TYPE,
                    CHARACTER_MAXIMUM_LENGTH
                FROM INFORMATION_SCHEMA.PARAMETERS
                WHERE SPECIFIC_NAME = @spName
                ORDER BY ORDINAL_POSITION;";

            await using var comando = new SqlCommand(sql, conexion);
            comando.Parameters.AddWithValue("@spName", nombreSP);

            await using var reader = await comando.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string nombre = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                bool esOutput = reader.IsDBNull(1) ? false : reader.GetInt32(1) == 1;
                string tipo = reader.IsDBNull(2) ? "nvarchar" : reader.GetString(2);
                int? maxLength = reader.IsDBNull(3) ? null : reader.GetInt32(3);

                lista.Add((nombre, esOutput, tipo, maxLength));
            }

            return lista;
        }

        // ================================================================
        // MÉTODO PRINCIPAL MEJORADO: Ejecuta un procedimiento almacenado genérico
        // MEJORA CRÍTICA: Ahora convierte DateTime con hora 00:00:00 a Date
        // ================================================================

// ================================================================
// MÉTODO PRINCIPAL MEJORADO: Ejecuta un procedimiento almacenado genérico
// MEJORA CRÍTICA: Ahora convierte DateTime con hora 00:00:00 a Date
// DETECTA SI ES FUNCTION O PROCEDURE
// ================================================================
public async Task<DataTable> EjecutarProcedimientoAlmacenadoConDictionaryAsync(
    string nombreSP,
    Dictionary<string, object?> parametros)
{
    if (string.IsNullOrWhiteSpace(nombreSP))
        throw new ArgumentException("El nombre del procedimiento no puede estar vacío.");

    string cadenaConexion = _proveedorConexion.ObtenerCadenaConexion();
    await using var conexion = new SqlConnection(cadenaConexion);
    await conexion.OpenAsync();

    // Detectar si es FUNCTION o PROCEDURE
    string sqlTipo = "SELECT ROUTINE_TYPE FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_NAME = @spName";
    string tipoRutina = "PROCEDURE";
    await using (var cmdTipo = new SqlCommand(sqlTipo, conexion))
    {
        cmdTipo.Parameters.AddWithValue("@spName", nombreSP);
        var resultado = await cmdTipo.ExecuteScalarAsync();
        tipoRutina = resultado?.ToString() ?? "PROCEDURE";
    }

    var metadatos = await ObtenerMetadatosParametrosAsync(conexion, nombreSP);

    var parametrosNormalizados = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    foreach (var kv in parametros ?? new Dictionary<string, object?>())
    {
        var clave = kv.Key.StartsWith("@") ? kv.Key.Substring(1) : kv.Key;
        parametrosNormalizados[clave] = kv.Value;
    }

    var tabla = new DataTable();

    // Procesar según el tipo de rutina
    if (tipoRutina == "FUNCTION")
    {
        // ============================================================
        // MANEJO DE FUNCIONES
        // ============================================================
        var parametrosEntrada = metadatos.Where(m => !m.EsOutput).ToList();
        var parametrosQuery = string.Join(", ", parametrosEntrada.Select((_, i) => $"@p{i}"));
        var sqlLlamada = $"SELECT dbo.{nombreSP}({parametrosQuery}) AS Resultado";
        
        await using var comando = new SqlCommand(sqlLlamada, conexion);
        comando.CommandType = CommandType.Text;
        comando.CommandTimeout = 300;

        // Agregar parámetros con nombres @p0, @p1, etc.
        for (int i = 0; i < parametrosEntrada.Count; i++)
        {
            var meta = parametrosEntrada[i];
            string clave = meta.Nombre.StartsWith("@") ? meta.Nombre.Substring(1) : meta.Nombre;
            object valor = parametrosNormalizados.TryGetValue(clave, out var v) && v != null ? v : DBNull.Value;

            // ============================================================
            // MEJORA: Detección de JSON de 3 formas (como PostgreSQL)
            // ============================================================
            bool esJSON = meta.Tipo.ToLower() == "nvarchar" && meta.MaxLength == -1;

            if (!esJSON && valor is string sValor && !string.IsNullOrWhiteSpace(sValor))
            {
                var nombreParam = meta.Nombre?.ToLower() ?? "";
                var sValorTrim = sValor.TrimStart();

                if (sValorTrim.StartsWith("{") || sValorTrim.StartsWith("["))
                {
                    esJSON = true;
                }
                else if (nombreParam.Contains("roles") || nombreParam.Contains("detalles") ||
                         nombreParam.Contains("json") || nombreParam.Contains("data"))
                {
                    if (sValorTrim.StartsWith("{") || sValorTrim.StartsWith("["))
                    {
                        esJSON = true;
                    }
                }
            }

            if (esJSON)
            {
                var param = new SqlParameter($"@p{i}", SqlDbType.NVarChar) { Value = valor == DBNull.Value ? DBNull.Value : valor.ToString() };
                param.Size = -1;
                comando.Parameters.Add(param);
            }
            // ============================================================
            // MEJORA: VARCHAR/NVARCHAR - Convertir a string (como PostgreSQL)
            // ============================================================
            else if ((meta.Tipo.ToLower() == "varchar" || meta.Tipo.ToLower() == "nvarchar" || meta.Tipo.ToLower() == "char" || meta.Tipo.ToLower() == "nchar")
                     && valor != DBNull.Value)
            {
                string valorStr = valor.ToString() ?? string.Empty;
                var param = new SqlParameter($"@p{i}", MapearTipo(meta.Tipo)) { Value = valorStr };
                if (meta.MaxLength.HasValue && meta.MaxLength.Value > 0)
                    param.Size = meta.MaxLength.Value;
                comando.Parameters.Add(param);
            }
            else if (valor is DateTime dt && dt.TimeOfDay == TimeSpan.Zero && meta.Tipo.ToLower() == "date")
            {
                comando.Parameters.Add(new SqlParameter($"@p{i}", SqlDbType.Date) { Value = DateOnly.FromDateTime(dt) });
            }
            else if (meta.Tipo.ToLower() == "int" && valor != DBNull.Value)
            {
                comando.Parameters.Add(new SqlParameter($"@p{i}", SqlDbType.Int) { Value = Convert.ToInt32(valor) });
            }
            else if (meta.Tipo.ToLower() == "bigint" && valor != DBNull.Value)
            {
                comando.Parameters.Add(new SqlParameter($"@p{i}", SqlDbType.BigInt) { Value = Convert.ToInt64(valor) });
            }
            else if ((meta.Tipo.ToLower() == "decimal" || meta.Tipo.ToLower() == "numeric") && valor != DBNull.Value)
            {
                comando.Parameters.Add(new SqlParameter($"@p{i}", SqlDbType.Decimal) { Value = Convert.ToDecimal(valor) });
            }
            else if (meta.Tipo.ToLower() == "bit" && valor != DBNull.Value)
            {
                comando.Parameters.Add(new SqlParameter($"@p{i}", SqlDbType.Bit) { Value = Convert.ToBoolean(valor) });
            }
            else
            {
                var param = new SqlParameter($"@p{i}", MapearTipo(meta.Tipo)) { Value = valor };
                if (meta.MaxLength.HasValue && meta.MaxLength.Value > 0)
                    param.Size = meta.MaxLength.Value;
                comando.Parameters.Add(param);
            }
        }

        await using var reader = await comando.ExecuteReaderAsync();
        tabla.Load(reader);
    }
    else
    {
        // ============================================================
        // MANEJO DE PROCEDIMIENTOS
        // ============================================================
        await using var comando = new SqlCommand(nombreSP, conexion);
        comando.CommandType = CommandType.StoredProcedure;
        comando.CommandTimeout = 300;

        foreach (var meta in metadatos)
        {
            string clave = meta.Nombre?.StartsWith("@") == true ? meta.Nombre.Substring(1) : (meta.Nombre ?? string.Empty);
            var sqlDbTipo = MapearTipo(meta.Tipo);

            if (!meta.EsOutput)
            {
                object valor = parametrosNormalizados.TryGetValue(clave, out var v) && v != null
                    ? v
                    : DBNull.Value;

                // ============================================================
                // MEJORA: Detección de JSON de 3 formas (como PostgreSQL)
                // 1. Por tipo (nvarchar(max) es el tipo JSON en SQL Server)
                // 2. Por contenido (empieza con { o [)
                // 3. Por nombre común de parámetro JSON
                // ============================================================
                bool esJSON = meta.Tipo.ToLower() == "nvarchar" && meta.MaxLength == -1; // nvarchar(max)

                if (!esJSON && valor is string sValor && !string.IsNullOrWhiteSpace(sValor))
                {
                    var nombreParam = meta.Nombre?.ToLower() ?? "";
                    var sValorTrim = sValor.TrimStart();

                    // Detectar por contenido
                    if (sValorTrim.StartsWith("{") || sValorTrim.StartsWith("["))
                    {
                        esJSON = true;
                    }
                    // Detectar por nombre común de parámetros JSON
                    else if (nombreParam.Contains("roles") || nombreParam.Contains("detalles") ||
                             nombreParam.Contains("json") || nombreParam.Contains("data"))
                    {
                        if (sValorTrim.StartsWith("{") || sValorTrim.StartsWith("["))
                        {
                            esJSON = true;
                        }
                    }
                }

                if (esJSON)
                {
                    // Tratar como JSON (NVARCHAR(MAX))
                    var param = new SqlParameter($"@{clave}", SqlDbType.NVarChar)
                    {
                        Direction = ParameterDirection.Input,
                        Value = valor == DBNull.Value ? DBNull.Value : valor.ToString()
                    };
                    param.Size = -1; // -1 indica MAX
                    comando.Parameters.Add(param);
                }
                // ============================================================
                // MEJORA: VARCHAR/NVARCHAR - Convertir a string si no lo es
                // Resuelve el caso de Int32 enviado como contraseña (como PostgreSQL)
                // ============================================================
                else if ((meta.Tipo.ToLower() == "varchar" || meta.Tipo.ToLower() == "nvarchar" || meta.Tipo.ToLower() == "char" || meta.Tipo.ToLower() == "nchar")
                         && valor != DBNull.Value)
                {
                    string valorStr = valor.ToString() ?? string.Empty;
                    var param = new SqlParameter($"@{clave}", MapearTipo(meta.Tipo))
                    {
                        Direction = ParameterDirection.Input,
                        Value = valorStr
                    };
                    if (meta.MaxLength.HasValue && meta.MaxLength.Value > 0)
                        param.Size = meta.MaxLength.Value;
                    comando.Parameters.Add(param);
                }
                else if (valor is DateTime dt && dt.TimeOfDay == TimeSpan.Zero && meta.Tipo.ToLower() == "date")
                {
                    var param = new SqlParameter($"@{clave}", SqlDbType.Date)
                    {
                        Direction = ParameterDirection.Input,
                        Value = DateOnly.FromDateTime(dt)
                    };
                    comando.Parameters.Add(param);
                }
                else if (meta.Tipo.ToLower() == "int" && valor != DBNull.Value)
                {
                    int valorInt = Convert.ToInt32(valor);
                    var param = new SqlParameter($"@{clave}", SqlDbType.Int)
                    {
                        Direction = ParameterDirection.Input,
                        Value = valorInt
                    };
                    comando.Parameters.Add(param);
                }
                else if (meta.Tipo.ToLower() == "bigint" && valor != DBNull.Value)
                {
                    long valorLong = Convert.ToInt64(valor);
                    var param = new SqlParameter($"@{clave}", SqlDbType.BigInt)
                    {
                        Direction = ParameterDirection.Input,
                        Value = valorLong
                    };
                    comando.Parameters.Add(param);
                }
                else if ((meta.Tipo.ToLower() == "decimal" || meta.Tipo.ToLower() == "numeric") && valor != DBNull.Value)
                {
                    decimal valorDec = Convert.ToDecimal(valor);
                    var param = new SqlParameter($"@{clave}", SqlDbType.Decimal)
                    {
                        Direction = ParameterDirection.Input,
                        Value = valorDec
                    };
                    comando.Parameters.Add(param);
                }
                else if (meta.Tipo.ToLower() == "bit" && valor != DBNull.Value)
                {
                    bool valorBool = Convert.ToBoolean(valor);
                    var param = new SqlParameter($"@{clave}", SqlDbType.Bit)
                    {
                        Direction = ParameterDirection.Input,
                        Value = valorBool
                    };
                    comando.Parameters.Add(param);
                }
                else
                {
                    var param = new SqlParameter($"@{clave}", sqlDbTipo)
                    {
                        Direction = ParameterDirection.Input,
                        Value = valor
                    };

                    if (meta.MaxLength.HasValue && meta.MaxLength.Value > 0)
                        param.Size = meta.MaxLength.Value;

                    comando.Parameters.Add(param);
                }
            }
            else
            {
                var param = new SqlParameter($"@{clave}", sqlDbTipo)
                {
                    Direction = ParameterDirection.Output
                };

                if (meta.MaxLength.HasValue && meta.MaxLength.Value > 0)
                    param.Size = meta.MaxLength.Value;

                comando.Parameters.Add(param);
            }
        }

        try
        {
            await using var reader = await comando.ExecuteReaderAsync();
            tabla.Load(reader);
        }
        catch
        {
            await comando.ExecuteNonQueryAsync();
        }

        foreach (SqlParameter param in comando.Parameters)
        {
            if (param.Direction == ParameterDirection.Output || param.Direction == ParameterDirection.InputOutput)
            {
                if (!tabla.Columns.Contains(param.ParameterName))
                    tabla.Columns.Add(param.ParameterName);

                if (tabla.Rows.Count == 0)
                    tabla.Rows.Add(tabla.NewRow());

                tabla.Rows[0][param.ParameterName] = param.Value == null ? DBNull.Value : param.Value;
            }
        }
    }

    return tabla;
}

        // ================================================================
        // MÉTODO MEJORADO: Ejecuta una consulta SQL parametrizada
        // MEJORA: Convierte DateTime con hora 00:00:00 a Date
        // ================================================================
        public async Task<DataTable> EjecutarConsultaParametrizadaConDictionaryAsync(
            string consultaSQL,
            Dictionary<string, object?> parametros,
            int maximoRegistros = 10000,
            string? esquema = null)
        {
            var tabla = new DataTable();
            string cadenaConexion = _proveedorConexion.ObtenerCadenaConexion();
            await using var conexion = new SqlConnection(cadenaConexion);
            await conexion.OpenAsync();
            await using var comando = new SqlCommand(consultaSQL, conexion);

            foreach (var p in parametros ?? new Dictionary<string, object?>())
            {
                string nombreParam = p.Key.StartsWith("@") ? p.Key : $"@{p.Key}";
                object? valor = p.Value ?? DBNull.Value;

                // MEJORA CRÍTICA: Detectar DateTime con hora 00:00:00
                if (valor is DateTime dt && dt.TimeOfDay == TimeSpan.Zero)
                {
                    // Si la hora es 00:00:00, probablemente es una fecha sin hora
                    // Convertir a Date para que SQL Server lo trate como DATE
                    // Esto evita problemas de comparación con columnas tipo DATE
                    comando.Parameters.Add(new SqlParameter(nombreParam, SqlDbType.Date)
                    {
                        Value = DateOnly.FromDateTime(dt)
                    });
                }
                else
                {
                    // Caso normal: dejar que AddWithValue infiera el tipo
                    comando.Parameters.AddWithValue(nombreParam, valor);
                }
            }

            await using var reader = await comando.ExecuteReaderAsync();
            tabla.Load(reader);
            return tabla;
        }

        // ================================================================
        // MÉTODO: Valida si una consulta SQL con parámetros es sintácticamente correcta
        // ================================================================
        public async Task<(bool esValida, string? mensajeError)> ValidarConsultaConDictionaryAsync(
            string consultaSQL, 
            Dictionary<string, object?> parametros)
        {
            try
            {
                string cadenaConexion = _proveedorConexion.ObtenerCadenaConexion();
                await using var conexion = new SqlConnection(cadenaConexion);
                await conexion.OpenAsync();
                
                // Activar modo PARSEONLY para validación sin ejecución
                await using var comandoParseOn = new SqlCommand("SET PARSEONLY ON", conexion);
                await comandoParseOn.ExecuteNonQueryAsync();

                await using var comando = new SqlCommand(consultaSQL, conexion);
                comando.CommandTimeout = 5;

                foreach (var p in parametros ?? new Dictionary<string, object?>())
                {
                    string nombreParam = p.Key.StartsWith("@") ? p.Key : $"@{p.Key}";
                    comando.Parameters.AddWithValue(nombreParam, p.Value ?? DBNull.Value);
                }

                await comando.ExecuteNonQueryAsync();

                // Desactivar modo PARSEONLY
                await using var comandoParseOff = new SqlCommand("SET PARSEONLY OFF", conexion);
                await comandoParseOff.ExecuteNonQueryAsync();

                return (true, null);
            }
            catch (SqlException sqlEx)
            {
                string mensajeError = sqlEx.Number switch
                {
                    102 => "Error de sintaxis SQL: revise la estructura de la consulta",
                    207 => "Nombre de columna inválido: verifique que las columnas existan",
                    208 => "Objeto no válido: tabla o vista no existe en la base de datos",
                    156 => "Palabra clave SQL incorrecta o en posición incorrecta",
                    170 => "Error de sintaxis cerca de palabra reservada",
                    _ => $"Error de validación SQL Server (Código {sqlEx.Number}): {sqlEx.Message}"
                };

                return (false, mensajeError);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // ================================================================
        // MÉTODOS: Consultas de metadatos de base de datos/tablas
        // ================================================================
        public async Task<string?> ObtenerEsquemaTablaAsync(string nombreTabla, string? esquemaPredeterminado)
        {
            string cadenaConexion = _proveedorConexion.ObtenerCadenaConexion();
            await using var conexion = new SqlConnection(cadenaConexion);
            await conexion.OpenAsync();

            // Si se proporciona un esquema específico, verificar que la tabla existe en ese esquema
            if (!string.IsNullOrWhiteSpace(esquemaPredeterminado))
            {
                await using var cmdVerificar = new SqlCommand(
                    "SELECT TABLE_SCHEMA FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tabla AND TABLE_SCHEMA = @esquema", conexion);
                cmdVerificar.Parameters.AddWithValue("@tabla", nombreTabla);
                cmdVerificar.Parameters.AddWithValue("@esquema", esquemaPredeterminado);
                var resultadoVerificado = await cmdVerificar.ExecuteScalarAsync();
                if (resultadoVerificado != null)
                    return resultadoVerificado.ToString();
            }

            // Si no se proporciona esquema o no se encontró, buscar primero en 'dbo', luego en cualquier esquema
            string sql = @"
                SELECT TOP 1 TABLE_SCHEMA
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_NAME = @tabla
                ORDER BY CASE WHEN TABLE_SCHEMA = 'dbo' THEN 0 ELSE 1 END";

            await using var comando = new SqlCommand(sql, conexion);
            comando.Parameters.AddWithValue("@tabla", nombreTabla);

            var resultado = await comando.ExecuteScalarAsync();
            return resultado?.ToString();
        }

        public async Task<DataTable> ObtenerEstructuraTablaAsync(string nombreTabla, string esquema)
        {
            var tabla = new DataTable();
            string cadenaConexion = _proveedorConexion.ObtenerCadenaConexion();
            await using var conexion = new SqlConnection(cadenaConexion);
            await conexion.OpenAsync();

            // Query completa con constraints (PK, FK, UNIQUE, CHECK, DEFAULT, IDENTITY)
            string sql = @"
                SELECT
                    c.COLUMN_NAME AS column_name,
                    c.DATA_TYPE AS data_type,
                    c.CHARACTER_MAXIMUM_LENGTH AS character_maximum_length,
                    c.NUMERIC_PRECISION AS numeric_precision,
                    c.NUMERIC_SCALE AS numeric_scale,
                    c.IS_NULLABLE AS is_nullable,
                    c.COLUMN_DEFAULT AS column_default,
                    c.ORDINAL_POSITION AS ordinal_position,
                    CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 'YES' ELSE 'NO' END AS is_primary_key,
                    CASE WHEN uq.COLUMN_NAME IS NOT NULL THEN 'YES' ELSE 'NO' END AS is_unique,
                    CASE WHEN COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)), c.COLUMN_NAME, 'IsIdentity') = 1 THEN 'YES' ELSE 'NO' END AS is_identity,
                    fk.foreign_table_name,
                    fk.foreign_column_name,
                    fk.fk_constraint_name,
                    chk.check_clause
                FROM INFORMATION_SCHEMA.COLUMNS c
                -- Primary Key
                LEFT JOIN (
                    SELECT kcu.TABLE_SCHEMA, kcu.TABLE_NAME, kcu.COLUMN_NAME
                    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                        ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                        AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
                    WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                ) pk ON c.TABLE_SCHEMA = pk.TABLE_SCHEMA
                    AND c.TABLE_NAME = pk.TABLE_NAME
                    AND c.COLUMN_NAME = pk.COLUMN_NAME
                -- Unique
                LEFT JOIN (
                    SELECT kcu.TABLE_SCHEMA, kcu.TABLE_NAME, kcu.COLUMN_NAME
                    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                        ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                        AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
                    WHERE tc.CONSTRAINT_TYPE = 'UNIQUE'
                ) uq ON c.TABLE_SCHEMA = uq.TABLE_SCHEMA
                    AND c.TABLE_NAME = uq.TABLE_NAME
                    AND c.COLUMN_NAME = uq.COLUMN_NAME
                -- Foreign Key
                LEFT JOIN (
                    SELECT
                        kcu.TABLE_SCHEMA,
                        kcu.TABLE_NAME,
                        kcu.COLUMN_NAME,
                        ccu.TABLE_NAME AS foreign_table_name,
                        ccu.COLUMN_NAME AS foreign_column_name,
                        tc.CONSTRAINT_NAME AS fk_constraint_name
                    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                        ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                        AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
                    JOIN INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE ccu
                        ON tc.CONSTRAINT_NAME = ccu.CONSTRAINT_NAME
                        AND tc.TABLE_SCHEMA = ccu.TABLE_SCHEMA
                    WHERE tc.CONSTRAINT_TYPE = 'FOREIGN KEY'
                ) fk ON c.TABLE_SCHEMA = fk.TABLE_SCHEMA
                    AND c.TABLE_NAME = fk.TABLE_NAME
                    AND c.COLUMN_NAME = fk.COLUMN_NAME
                -- Check constraints
                LEFT JOIN (
                    SELECT
                        ccu.TABLE_SCHEMA,
                        ccu.TABLE_NAME,
                        ccu.COLUMN_NAME,
                        cc.CHECK_CLAUSE AS check_clause
                    FROM INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE ccu
                    JOIN INFORMATION_SCHEMA.CHECK_CONSTRAINTS cc
                        ON ccu.CONSTRAINT_NAME = cc.CONSTRAINT_NAME
                        AND ccu.CONSTRAINT_SCHEMA = cc.CONSTRAINT_SCHEMA
                ) chk ON c.TABLE_SCHEMA = chk.TABLE_SCHEMA
                    AND c.TABLE_NAME = chk.TABLE_NAME
                    AND c.COLUMN_NAME = chk.COLUMN_NAME
                WHERE c.TABLE_NAME = @tabla AND c.TABLE_SCHEMA = @esquema
                ORDER BY c.ORDINAL_POSITION";

            await using var comando = new SqlCommand(sql, conexion);
            comando.Parameters.AddWithValue("@tabla", nombreTabla);
            comando.Parameters.AddWithValue("@esquema", esquema);

            await using var reader = await comando.ExecuteReaderAsync();
            tabla.Load(reader);
            return tabla;
        }

        public async Task<DataTable> ObtenerEstructuraBaseDatosAsync()
        {
            var tabla = new DataTable();
            string cadenaConexion = _proveedorConexion.ObtenerCadenaConexion();
            await using var conexion = new SqlConnection(cadenaConexion);
            await conexion.OpenAsync();
            
            string sql = @"
                SELECT
                    t.TABLE_NAME AS table_name,
                    c.COLUMN_NAME AS column_name,
                    c.DATA_TYPE AS data_type,
                    c.CHARACTER_MAXIMUM_LENGTH AS character_maximum_length,
                    c.IS_NULLABLE AS is_nullable
                FROM INFORMATION_SCHEMA.TABLES t
                INNER JOIN INFORMATION_SCHEMA.COLUMNS c
                    ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
                WHERE t.TABLE_TYPE = 'BASE TABLE'
                ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME, c.ORDINAL_POSITION";

            await using var comando = new SqlCommand(sql, conexion);
            await using var reader = await comando.ExecuteReaderAsync();
            tabla.Load(reader);
            return tabla;
        }

        public async Task<Dictionary<string, object>> ObtenerEstructuraCompletaBaseDatosAsync()
        {
            var resultado = new Dictionary<string, object>();
            string cadenaConexion = _proveedorConexion.ObtenerCadenaConexion();

            await using var conexion = new SqlConnection(cadenaConexion);
            await conexion.OpenAsync();

            // 1. Tablas con columnas y constraints
            resultado["tablas"] = await ObtenerTablasConColumnasAsync(conexion);

            // 2. Vistas
            resultado["vistas"] = await ObtenerVistasAsync(conexion);

            // 3. Procedimientos almacenados
            resultado["procedimientos"] = await ObtenerProcedimientosAsync(conexion);

            // 4. Funciones
            resultado["funciones"] = await ObtenerFuncionesAsync(conexion);

            // 5. Triggers
            resultado["triggers"] = await ObtenerTriggersAsync(conexion);

            // 6. Índices
            resultado["indices"] = await ObtenerIndicesAsync(conexion);

            // 7. Secuencias
            resultado["secuencias"] = await ObtenerSecuenciasAsync(conexion);

            // 8. Tipos definidos por usuario
            resultado["tipos"] = await ObtenerTiposPersonalizadosAsync(conexion);

            // 9. Sinónimos
            resultado["sinonimos"] = await ObtenerSinonimosAsync(conexion);

            return resultado;
        }

        // ================================================================
        // MÉTODOS AUXILIARES PARA ESTRUCTURA COMPLETA DE BD
        // ================================================================

        private async Task<List<Dictionary<string, object?>>> ObtenerTablasConColumnasAsync(SqlConnection conexion)
        {
            var tablas = new List<Dictionary<string, object?>>();

            // Obtener todas las tablas
            string sqlTablas = @"
                SELECT
                    s.name AS schema_name,
                    t.name AS table_name,
                    ep.value AS table_comment
                FROM sys.tables t
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                LEFT JOIN sys.extended_properties ep
                    ON ep.major_id = t.object_id
                    AND ep.minor_id = 0
                    AND ep.name = 'MS_Description'
                WHERE t.is_ms_shipped = 0
                ORDER BY s.name, t.name";

            await using var cmdTablas = new SqlCommand(sqlTablas, conexion);
            await using var readerTablas = await cmdTablas.ExecuteReaderAsync();

            var listaTablas = new List<(string Schema, string Tabla, string? Comentario)>();
            while (await readerTablas.ReadAsync())
            {
                listaTablas.Add((
                    readerTablas.GetString(0),
                    readerTablas.GetString(1),
                    readerTablas.IsDBNull(2) ? null : readerTablas.GetString(2)
                ));
            }
            await readerTablas.CloseAsync();

            foreach (var (schema, tabla, comentario) in listaTablas)
            {
                var tablaDict = new Dictionary<string, object?>
                {
                    ["schema"] = schema,
                    ["nombre"] = tabla,
                    ["comentario"] = comentario,
                    ["columnas"] = await ObtenerColumnasTablaAsync(conexion, schema, tabla),
                    ["foreign_keys"] = await ObtenerForeignKeysTablaAsync(conexion, schema, tabla),
                    ["indices"] = await ObtenerIndicesTablaAsync(conexion, schema, tabla)
                };
                tablas.Add(tablaDict);
            }

            return tablas;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerColumnasTablaAsync(SqlConnection conexion, string schema, string tabla)
        {
            var columnas = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    c.name AS column_name,
                    tp.name AS data_type,
                    c.max_length AS character_maximum_length,
                    c.precision AS numeric_precision,
                    c.scale AS numeric_scale,
                    c.is_nullable,
                    dc.definition AS column_default,
                    c.column_id AS ordinal_position,
                    CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS is_primary_key,
                    CASE WHEN uq.column_id IS NOT NULL THEN 1 ELSE 0 END AS is_unique,
                    c.is_identity,
                    ep.value AS column_comment
                FROM sys.columns c
                INNER JOIN sys.tables t ON c.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                INNER JOIN sys.types tp ON c.user_type_id = tp.user_type_id
                LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
                LEFT JOIN (
                    SELECT ic.object_id, ic.column_id
                    FROM sys.index_columns ic
                    INNER JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                    WHERE i.is_primary_key = 1
                ) pk ON c.object_id = pk.object_id AND c.column_id = pk.column_id
                LEFT JOIN (
                    SELECT ic.object_id, ic.column_id
                    FROM sys.index_columns ic
                    INNER JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                    WHERE i.is_unique = 1 AND i.is_primary_key = 0
                ) uq ON c.object_id = uq.object_id AND c.column_id = uq.column_id
                LEFT JOIN sys.extended_properties ep
                    ON ep.major_id = c.object_id
                    AND ep.minor_id = c.column_id
                    AND ep.name = 'MS_Description'
                WHERE s.name = @schema AND t.name = @tabla
                ORDER BY c.column_id";

            await using var cmd = new SqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@tabla", tabla);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columnas.Add(new Dictionary<string, object?>
                {
                    ["nombre"] = reader.GetString(0),
                    ["tipo"] = reader.GetString(1),
                    ["longitud_maxima"] = reader.IsDBNull(2) ? null : reader.GetInt16(2),
                    ["precision"] = reader.IsDBNull(3) ? null : reader.GetByte(3),
                    ["escala"] = reader.IsDBNull(4) ? null : reader.GetByte(4),
                    ["nullable"] = reader.GetBoolean(5),
                    ["valor_default"] = reader.IsDBNull(6) ? null : reader.GetString(6),
                    ["posicion"] = reader.GetInt32(7),
                    ["es_primary_key"] = reader.GetInt32(8) == 1,
                    ["es_unique"] = reader.GetInt32(9) == 1,
                    ["es_identity"] = reader.GetBoolean(10),
                    ["comentario"] = reader.IsDBNull(11) ? null : reader.GetString(11)
                });
            }

            return columnas;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerForeignKeysTablaAsync(SqlConnection conexion, string schema, string tabla)
        {
            var fks = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    fk.name AS constraint_name,
                    COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS column_name,
                    OBJECT_SCHEMA_NAME(fkc.referenced_object_id) AS referenced_schema,
                    OBJECT_NAME(fkc.referenced_object_id) AS referenced_table,
                    COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS referenced_column,
                    fk.update_referential_action_desc AS on_update,
                    fk.delete_referential_action_desc AS on_delete
                FROM sys.foreign_keys fk
                INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
                INNER JOIN sys.tables t ON fk.parent_object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE s.name = @schema AND t.name = @tabla
                ORDER BY fk.name";

            await using var cmd = new SqlCommand(sql, conexion);
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
                    ["foreign_schema_name"] = reader.GetString(2),
                    ["foreign_table_name"] = reader.GetString(3),
                    ["foreign_column_name"] = reader.GetString(4),
                    ["on_update"] = reader.GetString(5),
                    ["on_delete"] = reader.GetString(6)
                });
            }

            return fks;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerIndicesTablaAsync(SqlConnection conexion, string schema, string tabla)
        {
            var indices = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    i.name AS index_name,
                    i.type_desc AS index_type,
                    i.is_unique,
                    i.is_primary_key,
                    STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS columns
                FROM sys.indexes i
                INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                INNER JOIN sys.tables t ON i.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE s.name = @schema AND t.name = @tabla AND i.name IS NOT NULL
                GROUP BY i.name, i.type_desc, i.is_unique, i.is_primary_key
                ORDER BY i.name";

            await using var cmd = new SqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@tabla", tabla);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                indices.Add(new Dictionary<string, object?>
                {
                    ["nombre"] = reader.GetString(0),
                    ["tipo"] = reader.GetString(1),
                    ["es_unique"] = reader.GetBoolean(2),
                    ["es_primary_key"] = reader.GetBoolean(3),
                    ["columnas"] = reader.GetString(4)
                });
            }

            return indices;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerVistasAsync(SqlConnection conexion)
        {
            var vistas = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    s.name AS schema_name,
                    v.name AS view_name,
                    m.definition AS view_definition,
                    ep.value AS view_comment
                FROM sys.views v
                INNER JOIN sys.schemas s ON v.schema_id = s.schema_id
                LEFT JOIN sys.sql_modules m ON v.object_id = m.object_id
                LEFT JOIN sys.extended_properties ep
                    ON ep.major_id = v.object_id
                    AND ep.minor_id = 0
                    AND ep.name = 'MS_Description'
                WHERE v.is_ms_shipped = 0
                ORDER BY s.name, v.name";

            await using var cmd = new SqlCommand(sql, conexion);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                vistas.Add(new Dictionary<string, object?>
                {
                    ["schema"] = reader.GetString(0),
                    ["nombre"] = reader.GetString(1),
                    ["definicion"] = reader.IsDBNull(2) ? null : reader.GetString(2),
                    ["comentario"] = reader.IsDBNull(3) ? null : reader.GetString(3)
                });
            }

            return vistas;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerProcedimientosAsync(SqlConnection conexion)
        {
            var procedimientos = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    s.name AS schema_name,
                    p.name AS procedure_name,
                    m.definition AS procedure_definition,
                    p.create_date,
                    p.modify_date,
                    ep.value AS procedure_comment
                FROM sys.procedures p
                INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
                LEFT JOIN sys.sql_modules m ON p.object_id = m.object_id
                LEFT JOIN sys.extended_properties ep
                    ON ep.major_id = p.object_id
                    AND ep.minor_id = 0
                    AND ep.name = 'MS_Description'
                WHERE p.is_ms_shipped = 0
                ORDER BY s.name, p.name";

            await using var cmdProcs = new SqlCommand(sql, conexion);
            await using var readerProcs = await cmdProcs.ExecuteReaderAsync();

            var listaProcs = new List<(string Schema, string Nombre, string? Definicion, DateTime Created, DateTime Modified, string? Comentario)>();
            while (await readerProcs.ReadAsync())
            {
                listaProcs.Add((
                    readerProcs.GetString(0),
                    readerProcs.GetString(1),
                    readerProcs.IsDBNull(2) ? null : readerProcs.GetString(2),
                    readerProcs.GetDateTime(3),
                    readerProcs.GetDateTime(4),
                    readerProcs.IsDBNull(5) ? null : readerProcs.GetString(5)
                ));
            }
            await readerProcs.CloseAsync();

            foreach (var (schema, nombre, definicion, created, modified, comentario) in listaProcs)
            {
                var procDict = new Dictionary<string, object?>
                {
                    ["schema"] = schema,
                    ["nombre"] = nombre,
                    ["definicion"] = definicion,
                    ["fecha_creacion"] = created,
                    ["fecha_modificacion"] = modified,
                    ["comentario"] = comentario,
                    ["parametros"] = await ObtenerParametrosRutinaAsync(conexion, schema, nombre)
                };
                procedimientos.Add(procDict);
            }

            return procedimientos;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerFuncionesAsync(SqlConnection conexion)
        {
            var funciones = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    s.name AS schema_name,
                    o.name AS function_name,
                    o.type_desc AS function_type,
                    m.definition AS function_definition,
                    o.create_date,
                    o.modify_date,
                    ep.value AS function_comment
                FROM sys.objects o
                INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                LEFT JOIN sys.sql_modules m ON o.object_id = m.object_id
                LEFT JOIN sys.extended_properties ep
                    ON ep.major_id = o.object_id
                    AND ep.minor_id = 0
                    AND ep.name = 'MS_Description'
                WHERE o.type IN ('FN', 'IF', 'TF', 'FS', 'FT')  -- Scalar, Inline Table, Table-valued, CLR Scalar, CLR Table
                AND o.is_ms_shipped = 0
                ORDER BY s.name, o.name";

            await using var cmdFuncs = new SqlCommand(sql, conexion);
            await using var readerFuncs = await cmdFuncs.ExecuteReaderAsync();

            var listaFuncs = new List<(string Schema, string Nombre, string Tipo, string? Definicion, DateTime Created, DateTime Modified, string? Comentario)>();
            while (await readerFuncs.ReadAsync())
            {
                listaFuncs.Add((
                    readerFuncs.GetString(0),
                    readerFuncs.GetString(1),
                    readerFuncs.GetString(2),
                    readerFuncs.IsDBNull(3) ? null : readerFuncs.GetString(3),
                    readerFuncs.GetDateTime(4),
                    readerFuncs.GetDateTime(5),
                    readerFuncs.IsDBNull(6) ? null : readerFuncs.GetString(6)
                ));
            }
            await readerFuncs.CloseAsync();

            foreach (var (schema, nombre, tipo, definicion, created, modified, comentario) in listaFuncs)
            {
                var funcDict = new Dictionary<string, object?>
                {
                    ["schema"] = schema,
                    ["nombre"] = nombre,
                    ["tipo"] = tipo,
                    ["definicion"] = definicion,
                    ["fecha_creacion"] = created,
                    ["fecha_modificacion"] = modified,
                    ["comentario"] = comentario,
                    ["parametros"] = await ObtenerParametrosRutinaAsync(conexion, schema, nombre)
                };
                funciones.Add(funcDict);
            }

            return funciones;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerParametrosRutinaAsync(SqlConnection conexion, string schema, string rutina)
        {
            var parametros = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    p.name AS parameter_name,
                    TYPE_NAME(p.user_type_id) AS data_type,
                    p.max_length,
                    p.precision,
                    p.scale,
                    p.is_output,
                    p.has_default_value,
                    p.default_value
                FROM sys.parameters p
                INNER JOIN sys.objects o ON p.object_id = o.object_id
                INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                WHERE s.name = @schema AND o.name = @rutina
                ORDER BY p.parameter_id";

            await using var cmd = new SqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@rutina", rutina);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var nombreParam = reader.GetString(0);
                if (string.IsNullOrEmpty(nombreParam)) continue; // Saltar return value

                parametros.Add(new Dictionary<string, object?>
                {
                    ["nombre"] = nombreParam,
                    ["tipo"] = reader.GetString(1),
                    ["longitud_maxima"] = reader.IsDBNull(2) ? null : reader.GetInt16(2),
                    ["precision"] = reader.IsDBNull(3) ? null : reader.GetByte(3),
                    ["escala"] = reader.IsDBNull(4) ? null : reader.GetByte(4),
                    ["es_output"] = reader.GetBoolean(5),
                    ["tiene_default"] = reader.GetBoolean(6),
                    ["valor_default"] = reader.IsDBNull(7) ? null : reader.GetValue(7)
                });
            }

            return parametros;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerTriggersAsync(SqlConnection conexion)
        {
            var triggers = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    s.name AS schema_name,
                    tr.name AS trigger_name,
                    OBJECT_NAME(tr.parent_id) AS table_name,
                    tr.is_disabled,
                    tr.is_instead_of_trigger,
                    m.definition AS trigger_definition,
                    CASE
                        WHEN te.type_desc = 'INSERT' THEN 'INSERT'
                        WHEN te.type_desc = 'UPDATE' THEN 'UPDATE'
                        WHEN te.type_desc = 'DELETE' THEN 'DELETE'
                        ELSE te.type_desc
                    END AS trigger_event,
                    tr.create_date,
                    tr.modify_date
                FROM sys.triggers tr
                INNER JOIN sys.objects o ON tr.parent_id = o.object_id
                INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                LEFT JOIN sys.sql_modules m ON tr.object_id = m.object_id
                LEFT JOIN sys.trigger_events te ON tr.object_id = te.object_id
                WHERE tr.is_ms_shipped = 0
                ORDER BY s.name, tr.name";

            await using var cmd = new SqlCommand(sql, conexion);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                triggers.Add(new Dictionary<string, object?>
                {
                    ["schema"] = reader.GetString(0),
                    ["nombre"] = reader.GetString(1),
                    ["tabla"] = reader.GetString(2),
                    ["deshabilitado"] = reader.GetBoolean(3),
                    ["es_instead_of"] = reader.GetBoolean(4),
                    ["definicion"] = reader.IsDBNull(5) ? null : reader.GetString(5),
                    ["evento"] = reader.IsDBNull(6) ? null : reader.GetString(6),
                    ["fecha_creacion"] = reader.GetDateTime(7),
                    ["fecha_modificacion"] = reader.GetDateTime(8)
                });
            }

            return triggers;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerIndicesAsync(SqlConnection conexion)
        {
            var indices = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    s.name AS schema_name,
                    t.name AS table_name,
                    i.name AS index_name,
                    i.type_desc AS index_type,
                    i.is_unique,
                    i.is_primary_key,
                    i.is_unique_constraint,
                    STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS columns,
                    i.filter_definition
                FROM sys.indexes i
                INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                INNER JOIN sys.tables t ON i.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE i.name IS NOT NULL AND t.is_ms_shipped = 0
                GROUP BY s.name, t.name, i.name, i.type_desc, i.is_unique, i.is_primary_key, i.is_unique_constraint, i.filter_definition
                ORDER BY s.name, t.name, i.name";

            await using var cmd = new SqlCommand(sql, conexion);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                indices.Add(new Dictionary<string, object?>
                {
                    ["schema"] = reader.GetString(0),
                    ["tabla"] = reader.GetString(1),
                    ["nombre"] = reader.GetString(2),
                    ["tipo"] = reader.GetString(3),
                    ["es_unique"] = reader.GetBoolean(4),
                    ["es_primary_key"] = reader.GetBoolean(5),
                    ["es_unique_constraint"] = reader.GetBoolean(6),
                    ["columnas"] = reader.GetString(7),
                    ["filtro"] = reader.IsDBNull(8) ? null : reader.GetString(8)
                });
            }

            return indices;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerSecuenciasAsync(SqlConnection conexion)
        {
            var secuencias = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    s.name AS schema_name,
                    seq.name AS sequence_name,
                    TYPE_NAME(seq.user_type_id) AS data_type,
                    seq.start_value,
                    seq.increment,
                    seq.minimum_value,
                    seq.maximum_value,
                    seq.is_cycling,
                    seq.current_value
                FROM sys.sequences seq
                INNER JOIN sys.schemas s ON seq.schema_id = s.schema_id
                ORDER BY s.name, seq.name";

            await using var cmd = new SqlCommand(sql, conexion);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                secuencias.Add(new Dictionary<string, object?>
                {
                    ["schema"] = reader.GetString(0),
                    ["nombre"] = reader.GetString(1),
                    ["tipo"] = reader.GetString(2),
                    ["valor_inicial"] = reader.GetValue(3),
                    ["incremento"] = reader.GetValue(4),
                    ["valor_minimo"] = reader.GetValue(5),
                    ["valor_maximo"] = reader.GetValue(6),
                    ["es_ciclica"] = reader.GetBoolean(7),
                    ["valor_actual"] = reader.GetValue(8)
                });
            }

            return secuencias;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerTiposPersonalizadosAsync(SqlConnection conexion)
        {
            var tipos = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    s.name AS schema_name,
                    t.name AS type_name,
                    CASE
                        WHEN t.is_table_type = 1 THEN 'TABLE TYPE'
                        WHEN t.is_user_defined = 1 THEN 'USER DEFINED TYPE'
                        ELSE 'ALIAS TYPE'
                    END AS type_category,
                    bt.name AS base_type,
                    t.max_length,
                    t.precision,
                    t.scale,
                    t.is_nullable
                FROM sys.types t
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                LEFT JOIN sys.types bt ON t.system_type_id = bt.user_type_id AND bt.is_user_defined = 0
                WHERE t.is_user_defined = 1
                ORDER BY s.name, t.name";

            await using var cmd = new SqlCommand(sql, conexion);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                tipos.Add(new Dictionary<string, object?>
                {
                    ["schema"] = reader.GetString(0),
                    ["nombre"] = reader.GetString(1),
                    ["categoria"] = reader.GetString(2),
                    ["tipo_base"] = reader.IsDBNull(3) ? null : reader.GetString(3),
                    ["longitud_maxima"] = reader.GetInt16(4),
                    ["precision"] = reader.GetByte(5),
                    ["escala"] = reader.GetByte(6),
                    ["nullable"] = reader.GetBoolean(7)
                });
            }

            return tipos;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerSinonimosAsync(SqlConnection conexion)
        {
            var sinonimos = new List<Dictionary<string, object?>>();

            string sql = @"
                SELECT
                    s.name AS schema_name,
                    syn.name AS synonym_name,
                    syn.base_object_name AS target_object,
                    syn.create_date
                FROM sys.synonyms syn
                INNER JOIN sys.schemas s ON syn.schema_id = s.schema_id
                ORDER BY s.name, syn.name";

            await using var cmd = new SqlCommand(sql, conexion);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                sinonimos.Add(new Dictionary<string, object?>
                {
                    ["schema"] = reader.GetString(0),
                    ["nombre"] = reader.GetString(1),
                    ["objeto_destino"] = reader.GetString(2),
                    ["fecha_creacion"] = reader.GetDateTime(3)
                });
            }

            return sinonimos;
        }
    }
}
