// --------------------------------------------------------------
// Archivo: RepositorioConsultasPostgreSQL.cs 
// Ruta: ApiGenericaCsharp/Repositorios/RepositorioConsultasPostgreSQL.cs
// Mejora: Manejo inteligente de tipos (json, numéricos, booleanos y fechas)
//         y DateTime con hora 00:00:00 como DATE en consultas
// --------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using ApiGenericaCsharp.Repositorios.Abstracciones;
using ApiGenericaCsharp.Servicios.Abstracciones;

namespace ApiGenericaCsharp.Repositorios
{
    /// <summary>
    /// Implementación de repositorio para ejecutar consultas y procedimientos almacenados en PostgreSQL.
    /// 
    /// Mejoras:
    /// - Detecta DateTime con hora 00:00:00 y los convierte a DateOnly automáticamente en consultas.
    /// - Detecta y asigna tipos correctos al ejecutar procedimientos (json/jsonb, enteros, numéricos, booleanos, fechas).
    /// </summary>
    public sealed class RepositorioConsultasPostgreSQL : IRepositorioConsultas
    {
        private readonly IProveedorConexion _proveedorConexion;

        public RepositorioConsultasPostgreSQL(IProveedorConexion proveedorConexion)
        {
            _proveedorConexion = proveedorConexion ?? throw new ArgumentNullException(nameof(proveedorConexion));
        }

        // ================================================================
        // MÉTODO AUXILIAR: Mapea tipos de datos de PostgreSQL a NpgsqlDbType
        // ================================================================
        private NpgsqlDbType MapearTipo(string tipo)
        {
            return tipo.ToLower() switch
            {
                "text" => NpgsqlDbType.Text,
                "varchar" => NpgsqlDbType.Varchar,
                "character varying" => NpgsqlDbType.Varchar,
                "integer" => NpgsqlDbType.Integer,
                "int" => NpgsqlDbType.Integer,
                "int4" => NpgsqlDbType.Integer,
                "bigint" => NpgsqlDbType.Bigint,
                "int8" => NpgsqlDbType.Bigint,
                "smallint" => NpgsqlDbType.Smallint,
                "int2" => NpgsqlDbType.Smallint,
                "boolean" => NpgsqlDbType.Boolean,
                "bool" => NpgsqlDbType.Boolean,
                "json" => NpgsqlDbType.Json,
                "jsonb" => NpgsqlDbType.Jsonb,
                "timestamp" => NpgsqlDbType.Timestamp,
                "timestamp without time zone" => NpgsqlDbType.Timestamp,
                "timestamptz" => NpgsqlDbType.TimestampTz,
                "timestamp with time zone" => NpgsqlDbType.TimestampTz,
                "date" => NpgsqlDbType.Date,
                "numeric" => NpgsqlDbType.Numeric,
                "decimal" => NpgsqlDbType.Numeric,
                "real" => NpgsqlDbType.Real,
                "float4" => NpgsqlDbType.Real,
                "double precision" => NpgsqlDbType.Double,
                "float8" => NpgsqlDbType.Double,
                _ => NpgsqlDbType.Text
            };
        }

        // ================================================================
        // MÉTODO AUXILIAR: Obtiene metadatos de parámetros de un SP en PostgreSQL
        // ================================================================
/// <summary>
/// MEJORA: Ahora soporta esquemas personalizados (ventas.mi_funcion).
/// Busca primero en el esquema especificado, luego en public, finalmente en todos los esquemas.
/// </summary>
private async Task<List<(string Nombre, string Modo, string Tipo)>> ObtenerMetadatosParametrosAsync(
    NpgsqlConnection conexion,
    string nombreSP,
    string? esquema = null)
{
    var lista = new List<(string, string, string)>();

    // MEJORA: Construir consulta dinámica según si hay esquema o no
    string sql;
    if (!string.IsNullOrWhiteSpace(esquema))
    {
        // Búsqueda en esquema específico
        sql = @"
            SELECT parameter_name, parameter_mode, data_type
            FROM information_schema.parameters
            WHERE specific_name = (
                SELECT specific_name
                FROM information_schema.routines
                WHERE routine_schema = @esquema
                  AND routine_name = @spName
                LIMIT 1
            )
            ORDER BY ordinal_position;";
    }
    else
    {
        // MEJORA: Búsqueda en public primero, luego en cualquier esquema
        sql = @"
            SELECT parameter_name, parameter_mode, data_type
            FROM information_schema.parameters
            WHERE specific_name = (
                SELECT specific_name
                FROM information_schema.routines
                WHERE routine_name = @spName
                ORDER BY CASE
                    WHEN routine_schema = 'public' THEN 1
                    ELSE 2
                END
                LIMIT 1
            )
            ORDER BY ordinal_position;";
    }

    await using var comando = new NpgsqlCommand(sql, conexion);
    comando.Parameters.AddWithValue("@spName", nombreSP);
    if (!string.IsNullOrWhiteSpace(esquema))
    {
        comando.Parameters.AddWithValue("@esquema", esquema);
    }

    await using var reader = await comando.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        string nombre = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
        string modo = reader.IsDBNull(1) ? "IN" : reader.GetString(1);
        string tipo = reader.IsDBNull(2) ? "text" : reader.GetString(2);

        lista.Add((nombre, modo, tipo));
    }

    return lista;
}

        // ================================================================
        // MÉTODO PRINCIPAL: Ejecuta un procedimiento almacenado genérico
        // ================================================================

        /// <summary>
        /// MEJORA: Ahora soporta esquemas personalizados en el nombre del SP.
        ///
        /// Formatos soportados:
        /// - "mi_funcion" → Busca en public primero, luego en otros esquemas
        /// - "ventas.mi_funcion" → Busca específicamente en el esquema ventas
        /// - "public.mi_funcion" → Busca específicamente en public
        /// </summary>
        public async Task<DataTable> EjecutarProcedimientoAlmacenadoConDictionaryAsync(
            string nombreSP,
            Dictionary<string, object?> parametros)
        {
            if (string.IsNullOrWhiteSpace(nombreSP))
                throw new ArgumentException("El nombre del procedimiento no puede estar vacío.");

            string cadenaConexion = _proveedorConexion.ObtenerCadenaConexion();
            await using var conexion = new NpgsqlConnection(cadenaConexion);
            await conexion.OpenAsync();

            // MEJORA: Detectar si el nombre incluye esquema (ventas.mi_funcion)
            string? esquema = null;
            string nombreSPSinEsquema = nombreSP;

            if (nombreSP.Contains('.'))
            {
                var partes = nombreSP.Split('.', 2);
                esquema = partes[0].Trim();
                nombreSPSinEsquema = partes[1].Trim();
            }

            // MEJORA: Detectar si es FUNCTION o PROCEDURE (con soporte de esquemas)
            string sqlTipo;
            if (!string.IsNullOrWhiteSpace(esquema))
            {
                // Buscar en esquema específico
                sqlTipo = "SELECT routine_type FROM information_schema.routines WHERE routine_schema = @esquema AND routine_name = @spName LIMIT 1";
            }
            else
            {
                // MEJORA: Buscar en public primero, luego en cualquier esquema
                sqlTipo = @"SELECT routine_type FROM information_schema.routines
                           WHERE routine_name = @spName
                           ORDER BY CASE WHEN routine_schema = 'public' THEN 1 ELSE 2 END
                           LIMIT 1";
            }

            string tipoRutina = "PROCEDURE";
            await using (var cmdTipo = new NpgsqlCommand(sqlTipo, conexion))
            {
                cmdTipo.Parameters.AddWithValue("@spName", nombreSPSinEsquema);
                if (!string.IsNullOrWhiteSpace(esquema))
                {
                    cmdTipo.Parameters.AddWithValue("@esquema", esquema);
                }
                var resultado = await cmdTipo.ExecuteScalarAsync();
                tipoRutina = resultado?.ToString() ?? "PROCEDURE";
            }

            var metadatos = await ObtenerMetadatosParametrosAsync(conexion, nombreSPSinEsquema, esquema);
            var parametrosNormalizados = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in parametros ?? new Dictionary<string, object?>())
            {
                var clave = kv.Key.StartsWith("@") ? kv.Key.Substring(1) : kv.Key;
                parametrosNormalizados[clave] = kv.Value;
            }

            // MEJORA: Incluir parámetros IN e INOUT para la llamada
            var parametrosEntrada = metadatos.Where(m => m.Modo == "IN" || m.Modo == "INOUT").ToList();
            var parametrosInout = metadatos.Where(m => m.Modo == "INOUT").ToList();
            var placeholders = string.Join(", ", parametrosEntrada.Select((_, i) => $"${i + 1}"));

            // MEJORA: Construir nombre completo del SP (con esquema si está presente)
            string nombreCompletoSP = !string.IsNullOrWhiteSpace(esquema)
                ? $"{esquema}.{nombreSPSinEsquema}"
                : nombreSPSinEsquema;

            var sqlLlamada = tipoRutina == "FUNCTION"
                ? $"SELECT * FROM {nombreCompletoSP}({placeholders})"
                : $"CALL {nombreCompletoSP}({placeholders})";

            await using var comando = new NpgsqlCommand(sqlLlamada, conexion) { CommandTimeout = 300 };

            for (int i = 0; i < parametrosEntrada.Count; i++)
            {
                var meta = parametrosEntrada[i];
                string tipoMeta = meta.Tipo?.ToLower() ?? "text";
                object valor = parametrosNormalizados.TryGetValue(meta.Nombre, out var v) ? v ?? DBNull.Value : DBNull.Value;

                // JSON - Detectar de 3 formas:
                // 1. Por tipo de metadato (json/jsonb)
                // 2. Por contenido (empieza con { o [)
                // 3. Por nombre común de parámetro JSON (p_roles, p_detalles, roles, detalles) + contenido string
                bool esJSON = tipoMeta is "json" or "jsonb";

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
                        // Si el nombre sugiere JSON, tratarlo como JSON
                        if (sValorTrim.StartsWith("{") || sValorTrim.StartsWith("["))
                        {
                            esJSON = true;
                        }
                    }
                }

                if (esJSON)
                {
                    string valorJson = valor == DBNull.Value ? "{}" : valor?.ToString() ?? "{}";
                    comando.Parameters.Add(new NpgsqlParameter { Value = valorJson, NpgsqlDbType = tipoMeta == "jsonb" ? NpgsqlDbType.Jsonb : NpgsqlDbType.Json });
                }
                // Integer
                else if (tipoMeta is "integer" or "int" or "int4")
                {
                    int valorInt = valor == DBNull.Value ? 0 : Convert.ToInt32(valor);
                    comando.Parameters.Add(new NpgsqlParameter { Value = valorInt, NpgsqlDbType = NpgsqlDbType.Integer });
                }
                // Bigint
                else if (tipoMeta is "bigint" or "int8")
                {
                    long valorLong = valor == DBNull.Value ? 0 : Convert.ToInt64(valor);
                    comando.Parameters.Add(new NpgsqlParameter { Value = valorLong, NpgsqlDbType = NpgsqlDbType.Bigint });
                }
                // Numeric
                else if (tipoMeta is "numeric" or "decimal")
                {
                    decimal valorDec = valor == DBNull.Value ? 0 : Convert.ToDecimal(valor);
                    comando.Parameters.Add(new NpgsqlParameter { Value = valorDec, NpgsqlDbType = NpgsqlDbType.Numeric });
                }
                // VARCHAR/TEXT - Convertir a string si no lo es (resuelve Int32 enviado como contraseña)
                else if (tipoMeta is "character varying" or "varchar" or "text")
                {
                    string valorStr = valor == DBNull.Value ? string.Empty : valor?.ToString() ?? string.Empty;
                    comando.Parameters.Add(new NpgsqlParameter { Value = valorStr, NpgsqlDbType = MapearTipo(tipoMeta) });
                }
                else
                {
                    comando.Parameters.Add(new NpgsqlParameter { Value = valor, NpgsqlDbType = MapearTipo(tipoMeta) });
                }
            }

            var tabla = new DataTable();
            if (tipoRutina == "FUNCTION")
            {
                // FUNCIÓN: Ejecutar y leer resultado directamente
                await using var reader = await comando.ExecuteReaderAsync();
                tabla.Load(reader);
            }
            else if (parametrosInout.Count > 0)
            {
                // PROCEDURE CON INOUT: Usar ExecuteReader para capturar valores INOUT
                // PostgreSQL retorna los valores INOUT como resultado del CALL
                await using var reader = await comando.ExecuteReaderAsync();
                tabla.Load(reader);
            }
            else
            {
                // PROCEDURE SIN INOUT: Comportamiento original (ExecuteNonQuery)
                await comando.ExecuteNonQueryAsync();
            }

            return tabla;
        }

        // ================================================================
        // MÉTODO MEJORADO: Ejecuta una consulta SQL parametrizada
        // Convierte DateTime con hora 00:00:00 a DateOnly (DATE)
        // ================================================================
        public async Task<DataTable> EjecutarConsultaParametrizadaConDictionaryAsync(
            string consultaSQL,
            Dictionary<string, object?> parametros,
            int maximoRegistros = 10000,
            string? esquema = null)
        {
            var tabla = new DataTable();
            string cadenaConexion = _proveedorConexion.ObtenerCadenaConexion();
            await using var conexion = new NpgsqlConnection(cadenaConexion);
            await conexion.OpenAsync();
            await using var comando = new NpgsqlCommand(consultaSQL, conexion);

            foreach (var p in parametros ?? new Dictionary<string, object?>())
            {
                string nombreParam = p.Key.StartsWith("@") ? p.Key : $"@{p.Key}";
                object? valor = p.Value ?? DBNull.Value;

                if (valor is DateTime dt && dt.TimeOfDay == TimeSpan.Zero)
                {
                    comando.Parameters.Add(new NpgsqlParameter(nombreParam, NpgsqlDbType.Date)
                    {
                        Value = DateOnly.FromDateTime(dt)
                    });
                }
                else
                {
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
                await using var conexion = new NpgsqlConnection(cadenaConexion);
                await conexion.OpenAsync();
                
                string consultaValidacion = $"EXPLAIN {consultaSQL}";
                await using var comando = new NpgsqlCommand(consultaValidacion, conexion);

                foreach (var p in parametros ?? new Dictionary<string, object?>())
                {
                    string nombreParam = p.Key.StartsWith("@") ? p.Key : $"@{p.Key}";
                    comando.Parameters.AddWithValue(nombreParam, p.Value ?? DBNull.Value);
                }

                await comando.ExecuteNonQueryAsync();
                return (true, null);
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
            await using var conexion = new NpgsqlConnection(cadenaConexion);
            await conexion.OpenAsync();

            // Si se proporciona un esquema específico, verificar que la tabla existe en ese esquema
            if (!string.IsNullOrWhiteSpace(esquemaPredeterminado))
            {
                await using var cmdVerificar = new NpgsqlCommand(
                    "SELECT table_schema FROM information_schema.tables WHERE table_name = @tabla AND table_schema = @esquema LIMIT 1", conexion);
                cmdVerificar.Parameters.AddWithValue("@tabla", nombreTabla);
                cmdVerificar.Parameters.AddWithValue("@esquema", esquemaPredeterminado);
                var resultadoVerificado = await cmdVerificar.ExecuteScalarAsync();
                if (resultadoVerificado != null)
                    return resultadoVerificado.ToString();
            }

            // Si no se proporciona esquema o no se encontró, buscar primero en 'public', luego en cualquier esquema
            await using var comando = new NpgsqlCommand(@"
                SELECT table_schema FROM information_schema.tables
                WHERE table_name = @tabla
                ORDER BY CASE WHEN table_schema = 'public' THEN 0 ELSE 1 END
                LIMIT 1", conexion);
            comando.Parameters.AddWithValue("@tabla", nombreTabla);
            var resultado = await comando.ExecuteScalarAsync();
            return resultado?.ToString();
        }

        public async Task<DataTable> ObtenerEstructuraTablaAsync(string nombreTabla, string esquema)
        {
            var tabla = new DataTable();
            string cadenaConexion = _proveedorConexion.ObtenerCadenaConexion();
            await using var conexion = new NpgsqlConnection(cadenaConexion);
            await conexion.OpenAsync();

            // Query completa con constraints (PK, FK, UNIQUE, CHECK, DEFAULT)
            string sql = @"
                SELECT
                    c.column_name,
                    c.data_type,
                    c.character_maximum_length,
                    c.numeric_precision,
                    c.numeric_scale,
                    c.is_nullable,
                    c.column_default,
                    c.ordinal_position,
                    CASE WHEN pk.column_name IS NOT NULL THEN 'YES' ELSE 'NO' END AS is_primary_key,
                    CASE WHEN uq.column_name IS NOT NULL THEN 'YES' ELSE 'NO' END AS is_unique,
                    fk.foreign_table_name,
                    fk.foreign_column_name,
                    fk.constraint_name AS fk_constraint_name,
                    chk.check_clause
                FROM information_schema.columns c
                -- Primary Key
                LEFT JOIN (
                    SELECT kcu.table_schema, kcu.table_name, kcu.column_name
                    FROM information_schema.table_constraints tc
                    JOIN information_schema.key_column_usage kcu
                        ON tc.constraint_name = kcu.constraint_name
                        AND tc.table_schema = kcu.table_schema
                    WHERE tc.constraint_type = 'PRIMARY KEY'
                ) pk ON c.table_schema = pk.table_schema
                    AND c.table_name = pk.table_name
                    AND c.column_name = pk.column_name
                -- Unique
                LEFT JOIN (
                    SELECT kcu.table_schema, kcu.table_name, kcu.column_name
                    FROM information_schema.table_constraints tc
                    JOIN information_schema.key_column_usage kcu
                        ON tc.constraint_name = kcu.constraint_name
                        AND tc.table_schema = kcu.table_schema
                    WHERE tc.constraint_type = 'UNIQUE'
                ) uq ON c.table_schema = uq.table_schema
                    AND c.table_name = uq.table_name
                    AND c.column_name = uq.column_name
                -- Foreign Key
                LEFT JOIN (
                    SELECT
                        kcu.table_schema,
                        kcu.table_name,
                        kcu.column_name,
                        ccu.table_name AS foreign_table_name,
                        ccu.column_name AS foreign_column_name,
                        tc.constraint_name
                    FROM information_schema.table_constraints tc
                    JOIN information_schema.key_column_usage kcu
                        ON tc.constraint_name = kcu.constraint_name
                        AND tc.table_schema = kcu.table_schema
                    JOIN information_schema.constraint_column_usage ccu
                        ON tc.constraint_name = ccu.constraint_name
                        AND tc.table_schema = ccu.table_schema
                    WHERE tc.constraint_type = 'FOREIGN KEY'
                ) fk ON c.table_schema = fk.table_schema
                    AND c.table_name = fk.table_name
                    AND c.column_name = fk.column_name
                -- Check constraints
                LEFT JOIN (
                    SELECT
                        ccu.table_schema,
                        ccu.table_name,
                        ccu.column_name,
                        cc.check_clause
                    FROM information_schema.constraint_column_usage ccu
                    JOIN information_schema.check_constraints cc
                        ON ccu.constraint_name = cc.constraint_name
                        AND ccu.constraint_schema = cc.constraint_schema
                ) chk ON c.table_schema = chk.table_schema
                    AND c.table_name = chk.table_name
                    AND c.column_name = chk.column_name
                WHERE c.table_name = @tabla
                  AND c.table_schema = @esquema
                ORDER BY c.ordinal_position";

            await using var comando = new NpgsqlCommand(sql, conexion);
            comando.Parameters.AddWithValue("@tabla", nombreTabla);
            comando.Parameters.AddWithValue("@esquema", esquema);
            await using var reader = await comando.ExecuteReaderAsync();
            tabla.Load(reader);
            return tabla;
        }

        public async Task<Dictionary<string, object>> ObtenerEstructuraCompletaBaseDatosAsync()
        {
            var resultado = new Dictionary<string, object>();
            string cadenaConexion = _proveedorConexion.ObtenerCadenaConexion();
            await using var conexion = new NpgsqlConnection(cadenaConexion);
            await conexion.OpenAsync();

            // 1. TABLAS con sus columnas y constraints
            resultado["tablas"] = await ObtenerTablasConColumnasAsync(conexion);

            // 2. VISTAS
            resultado["vistas"] = await ObtenerVistasAsync(conexion);

            // 3. FUNCIONES
            resultado["funciones"] = await ObtenerFuncionesAsync(conexion);

            // 4. PROCEDIMIENTOS ALMACENADOS (PostgreSQL 11+)
            resultado["procedimientos"] = await ObtenerProcedimientosAsync(conexion);

            // 5. TRIGGERS
            resultado["triggers"] = await ObtenerTriggersAsync(conexion);

            // 6. SECUENCIAS
            resultado["secuencias"] = await ObtenerSecuenciasAsync(conexion);

            // 7. ÍNDICES
            resultado["indices"] = await ObtenerIndicesAsync(conexion);

            // 8. TIPOS PERSONALIZADOS (ENUMS, COMPOSITES)
            resultado["tipos"] = await ObtenerTiposPersonalizadosAsync(conexion);

            // 9. EXTENSIONES
            resultado["extensiones"] = await ObtenerExtensionesAsync(conexion);

            return resultado;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerTablasConColumnasAsync(NpgsqlConnection conexion)
        {
            var tablas = new List<Dictionary<string, object?>>();

            // Obtener tablas
            string sqlTablas = @"
                SELECT
                    t.table_name,
                    t.table_type,
                    obj_description((quote_ident(t.table_schema) || '.' || quote_ident(t.table_name))::regclass) AS table_comment,
                    (SELECT COUNT(*) FROM information_schema.columns c WHERE c.table_name = t.table_name AND c.table_schema = t.table_schema) AS column_count
                FROM information_schema.tables t
                WHERE t.table_schema = 'public' AND t.table_type = 'BASE TABLE'
                ORDER BY t.table_name";

            await using var cmdTablas = new NpgsqlCommand(sqlTablas, conexion);
            await using var readerTablas = await cmdTablas.ExecuteReaderAsync();

            var nombresTablas = new List<string>();
            while (await readerTablas.ReadAsync())
            {
                var tabla = new Dictionary<string, object?>
                {
                    ["table_name"] = readerTablas["table_name"],
                    ["table_type"] = readerTablas["table_type"],
                    ["table_comment"] = readerTablas.IsDBNull(readerTablas.GetOrdinal("table_comment")) ? null : readerTablas["table_comment"],
                    ["column_count"] = readerTablas["column_count"]
                };
                tablas.Add(tabla);
                nombresTablas.Add(readerTablas["table_name"].ToString()!);
            }
            await readerTablas.CloseAsync();

            // Obtener columnas para cada tabla con constraints
            foreach (var tabla in tablas)
            {
                string nombreTabla = tabla["table_name"]?.ToString() ?? "";
                tabla["columnas"] = await ObtenerColumnasTablaAsync(conexion, nombreTabla);
                tabla["foreign_keys"] = await ObtenerForeignKeysTablaAsync(conexion, nombreTabla);
            }

            return tablas;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerColumnasTablaAsync(NpgsqlConnection conexion, string nombreTabla)
        {
            var columnas = new List<Dictionary<string, object?>>();
            string sql = @"
                SELECT
                    c.column_name,
                    c.data_type,
                    c.udt_name,
                    c.character_maximum_length,
                    c.numeric_precision,
                    c.numeric_scale,
                    c.is_nullable,
                    c.column_default,
                    c.ordinal_position,
                    CASE WHEN pk.column_name IS NOT NULL THEN 'YES' ELSE 'NO' END AS is_primary_key,
                    CASE WHEN uq.column_name IS NOT NULL THEN 'YES' ELSE 'NO' END AS is_unique,
                    col_description((quote_ident(c.table_schema) || '.' || quote_ident(c.table_name))::regclass, c.ordinal_position) AS column_comment
                FROM information_schema.columns c
                LEFT JOIN (
                    SELECT kcu.table_schema, kcu.table_name, kcu.column_name
                    FROM information_schema.table_constraints tc
                    JOIN information_schema.key_column_usage kcu ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
                    WHERE tc.constraint_type = 'PRIMARY KEY'
                ) pk ON c.table_schema = pk.table_schema AND c.table_name = pk.table_name AND c.column_name = pk.column_name
                LEFT JOIN (
                    SELECT kcu.table_schema, kcu.table_name, kcu.column_name
                    FROM information_schema.table_constraints tc
                    JOIN information_schema.key_column_usage kcu ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
                    WHERE tc.constraint_type = 'UNIQUE'
                ) uq ON c.table_schema = uq.table_schema AND c.table_name = uq.table_name AND c.column_name = uq.column_name
                WHERE c.table_name = @tabla AND c.table_schema = 'public'
                ORDER BY c.ordinal_position";

            await using var cmd = new NpgsqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@tabla", nombreTabla);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                columnas.Add(new Dictionary<string, object?>
                {
                    ["column_name"] = reader["column_name"],
                    ["data_type"] = reader["data_type"],
                    ["udt_name"] = reader["udt_name"],
                    ["character_maximum_length"] = reader.IsDBNull(reader.GetOrdinal("character_maximum_length")) ? null : reader["character_maximum_length"],
                    ["numeric_precision"] = reader.IsDBNull(reader.GetOrdinal("numeric_precision")) ? null : reader["numeric_precision"],
                    ["numeric_scale"] = reader.IsDBNull(reader.GetOrdinal("numeric_scale")) ? null : reader["numeric_scale"],
                    ["is_nullable"] = reader["is_nullable"],
                    ["column_default"] = reader.IsDBNull(reader.GetOrdinal("column_default")) ? null : reader["column_default"],
                    ["ordinal_position"] = reader["ordinal_position"],
                    ["is_primary_key"] = reader["is_primary_key"],
                    ["is_unique"] = reader["is_unique"],
                    ["column_comment"] = reader.IsDBNull(reader.GetOrdinal("column_comment")) ? null : reader["column_comment"]
                });
            }
            return columnas;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerForeignKeysTablaAsync(NpgsqlConnection conexion, string nombreTabla)
        {
            var fks = new List<Dictionary<string, object?>>();
            string sql = @"
                SELECT
                    tc.constraint_name,
                    kcu.column_name,
                    ccu.table_name AS foreign_table_name,
                    ccu.column_name AS foreign_column_name,
                    rc.update_rule,
                    rc.delete_rule
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
                JOIN information_schema.constraint_column_usage ccu ON tc.constraint_name = ccu.constraint_name AND tc.table_schema = ccu.table_schema
                JOIN information_schema.referential_constraints rc ON tc.constraint_name = rc.constraint_name
                WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_name = @tabla AND tc.table_schema = 'public'";

            await using var cmd = new NpgsqlCommand(sql, conexion);
            cmd.Parameters.AddWithValue("@tabla", nombreTabla);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                fks.Add(new Dictionary<string, object?>
                {
                    ["constraint_name"] = reader["constraint_name"],
                    ["column_name"] = reader["column_name"],
                    ["foreign_table_name"] = reader["foreign_table_name"],
                    ["foreign_column_name"] = reader["foreign_column_name"],
                    ["update_rule"] = reader["update_rule"],
                    ["delete_rule"] = reader["delete_rule"]
                });
            }
            return fks;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerVistasAsync(NpgsqlConnection conexion)
        {
            var vistas = new List<Dictionary<string, object?>>();
            string sql = @"
                SELECT
                    v.table_name AS view_name,
                    v.view_definition,
                    v.is_updatable,
                    v.is_insertable_into,
                    obj_description((quote_ident(v.table_schema) || '.' || quote_ident(v.table_name))::regclass) AS view_comment
                FROM information_schema.views v
                WHERE v.table_schema = 'public'
                ORDER BY v.table_name";

            await using var cmd = new NpgsqlCommand(sql, conexion);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                vistas.Add(new Dictionary<string, object?>
                {
                    ["view_name"] = reader["view_name"],
                    ["view_definition"] = reader["view_definition"],
                    ["is_updatable"] = reader["is_updatable"],
                    ["is_insertable_into"] = reader["is_insertable_into"],
                    ["view_comment"] = reader.IsDBNull(reader.GetOrdinal("view_comment")) ? null : reader["view_comment"]
                });
            }
            return vistas;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerFuncionesAsync(NpgsqlConnection conexion)
        {
            var funciones = new List<Dictionary<string, object?>>();
            string sql = @"
                SELECT
                    p.proname AS function_name,
                    pg_get_function_arguments(p.oid) AS arguments,
                    pg_get_function_result(p.oid) AS return_type,
                    CASE p.prokind
                        WHEN 'f' THEN 'function'
                        WHEN 'p' THEN 'procedure'
                        WHEN 'a' THEN 'aggregate'
                        WHEN 'w' THEN 'window'
                    END AS routine_type,
                    l.lanname AS language,
                    p.prosrc AS source_code,
                    CASE p.provolatile
                        WHEN 'i' THEN 'IMMUTABLE'
                        WHEN 's' THEN 'STABLE'
                        WHEN 'v' THEN 'VOLATILE'
                    END AS volatility,
                    p.proisstrict AS is_strict,
                    p.prosecdef AS security_definer,
                    obj_description(p.oid) AS function_comment
                FROM pg_proc p
                JOIN pg_namespace n ON p.pronamespace = n.oid
                JOIN pg_language l ON p.prolang = l.oid
                WHERE n.nspname = 'public' AND p.prokind = 'f'
                ORDER BY p.proname";

            await using var cmd = new NpgsqlCommand(sql, conexion);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                funciones.Add(new Dictionary<string, object?>
                {
                    ["function_name"] = reader["function_name"],
                    ["arguments"] = reader["arguments"],
                    ["return_type"] = reader["return_type"],
                    ["routine_type"] = reader["routine_type"],
                    ["language"] = reader["language"],
                    ["source_code"] = reader["source_code"],
                    ["volatility"] = reader["volatility"],
                    ["is_strict"] = reader["is_strict"],
                    ["security_definer"] = reader["security_definer"],
                    ["function_comment"] = reader.IsDBNull(reader.GetOrdinal("function_comment")) ? null : reader["function_comment"]
                });
            }
            return funciones;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerProcedimientosAsync(NpgsqlConnection conexion)
        {
            var procedimientos = new List<Dictionary<string, object?>>();
            string sql = @"
                SELECT
                    p.proname AS procedure_name,
                    pg_get_function_arguments(p.oid) AS arguments,
                    l.lanname AS language,
                    p.prosrc AS source_code,
                    obj_description(p.oid) AS procedure_comment
                FROM pg_proc p
                JOIN pg_namespace n ON p.pronamespace = n.oid
                JOIN pg_language l ON p.prolang = l.oid
                WHERE n.nspname = 'public' AND p.prokind = 'p'
                ORDER BY p.proname";

            await using var cmd = new NpgsqlCommand(sql, conexion);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                procedimientos.Add(new Dictionary<string, object?>
                {
                    ["procedure_name"] = reader["procedure_name"],
                    ["arguments"] = reader["arguments"],
                    ["language"] = reader["language"],
                    ["source_code"] = reader["source_code"],
                    ["procedure_comment"] = reader.IsDBNull(reader.GetOrdinal("procedure_comment")) ? null : reader["procedure_comment"]
                });
            }
            return procedimientos;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerTriggersAsync(NpgsqlConnection conexion)
        {
            var triggers = new List<Dictionary<string, object?>>();
            string sql = @"
                SELECT
                    t.trigger_name,
                    t.event_manipulation,
                    t.event_object_table AS table_name,
                    t.action_timing,
                    t.action_orientation,
                    t.action_statement,
                    t.action_condition
                FROM information_schema.triggers t
                WHERE t.trigger_schema = 'public'
                ORDER BY t.event_object_table, t.trigger_name";

            await using var cmd = new NpgsqlCommand(sql, conexion);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                triggers.Add(new Dictionary<string, object?>
                {
                    ["trigger_name"] = reader["trigger_name"],
                    ["event_manipulation"] = reader["event_manipulation"],
                    ["table_name"] = reader["table_name"],
                    ["action_timing"] = reader["action_timing"],
                    ["action_orientation"] = reader["action_orientation"],
                    ["action_statement"] = reader["action_statement"],
                    ["action_condition"] = reader.IsDBNull(reader.GetOrdinal("action_condition")) ? null : reader["action_condition"]
                });
            }
            return triggers;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerSecuenciasAsync(NpgsqlConnection conexion)
        {
            var secuencias = new List<Dictionary<string, object?>>();
            string sql = @"
                SELECT
                    s.sequence_name,
                    s.data_type,
                    s.start_value,
                    s.minimum_value,
                    s.maximum_value,
                    s.increment,
                    s.cycle_option
                FROM information_schema.sequences s
                WHERE s.sequence_schema = 'public'
                ORDER BY s.sequence_name";

            await using var cmd = new NpgsqlCommand(sql, conexion);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                secuencias.Add(new Dictionary<string, object?>
                {
                    ["sequence_name"] = reader["sequence_name"],
                    ["data_type"] = reader["data_type"],
                    ["start_value"] = reader["start_value"],
                    ["minimum_value"] = reader["minimum_value"],
                    ["maximum_value"] = reader["maximum_value"],
                    ["increment"] = reader["increment"],
                    ["cycle_option"] = reader["cycle_option"]
                });
            }
            return secuencias;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerIndicesAsync(NpgsqlConnection conexion)
        {
            var indices = new List<Dictionary<string, object?>>();
            string sql = @"
                SELECT
                    i.indexname AS index_name,
                    i.tablename AS table_name,
                    i.indexdef AS index_definition,
                    ix.indisunique AS is_unique,
                    ix.indisprimary AS is_primary,
                    am.amname AS index_type
                FROM pg_indexes i
                JOIN pg_class c ON c.relname = i.indexname
                JOIN pg_index ix ON ix.indexrelid = c.oid
                JOIN pg_am am ON am.oid = c.relam
                WHERE i.schemaname = 'public'
                ORDER BY i.tablename, i.indexname";

            await using var cmd = new NpgsqlCommand(sql, conexion);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                indices.Add(new Dictionary<string, object?>
                {
                    ["index_name"] = reader["index_name"],
                    ["table_name"] = reader["table_name"],
                    ["index_definition"] = reader["index_definition"],
                    ["is_unique"] = reader["is_unique"],
                    ["is_primary"] = reader["is_primary"],
                    ["index_type"] = reader["index_type"]
                });
            }
            return indices;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerTiposPersonalizadosAsync(NpgsqlConnection conexion)
        {
            var tipos = new List<Dictionary<string, object?>>();
            string sql = @"
                SELECT
                    t.typname AS type_name,
                    CASE t.typtype
                        WHEN 'e' THEN 'enum'
                        WHEN 'c' THEN 'composite'
                        WHEN 'd' THEN 'domain'
                        WHEN 'r' THEN 'range'
                    END AS type_category,
                    CASE
                        WHEN t.typtype = 'e' THEN (
                            SELECT array_agg(e.enumlabel ORDER BY e.enumsortorder)::text
                            FROM pg_enum e WHERE e.enumtypid = t.oid
                        )
                        ELSE NULL
                    END AS enum_values,
                    obj_description(t.oid) AS type_comment
                FROM pg_type t
                JOIN pg_namespace n ON t.typnamespace = n.oid
                WHERE n.nspname = 'public' AND t.typtype IN ('e', 'c', 'd', 'r')
                ORDER BY t.typname";

            await using var cmd = new NpgsqlCommand(sql, conexion);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                tipos.Add(new Dictionary<string, object?>
                {
                    ["type_name"] = reader["type_name"],
                    ["type_category"] = reader["type_category"],
                    ["enum_values"] = reader.IsDBNull(reader.GetOrdinal("enum_values")) ? null : reader["enum_values"],
                    ["type_comment"] = reader.IsDBNull(reader.GetOrdinal("type_comment")) ? null : reader["type_comment"]
                });
            }
            return tipos;
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerExtensionesAsync(NpgsqlConnection conexion)
        {
            var extensiones = new List<Dictionary<string, object?>>();
            string sql = @"
                SELECT
                    e.extname AS extension_name,
                    e.extversion AS version,
                    n.nspname AS schema_name,
                    obj_description(e.oid) AS extension_comment
                FROM pg_extension e
                JOIN pg_namespace n ON e.extnamespace = n.oid
                ORDER BY e.extname";

            await using var cmd = new NpgsqlCommand(sql, conexion);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                extensiones.Add(new Dictionary<string, object?>
                {
                    ["extension_name"] = reader["extension_name"],
                    ["version"] = reader["version"],
                    ["schema_name"] = reader["schema_name"],
                    ["extension_comment"] = reader.IsDBNull(reader.GetOrdinal("extension_comment")) ? null : reader["extension_comment"]
                });
            }
            return extensiones;
        }
    }
}

