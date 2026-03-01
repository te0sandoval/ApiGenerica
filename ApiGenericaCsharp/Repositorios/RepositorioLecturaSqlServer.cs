// --------------------------------------------------------------
// Archivo: RepositorioLecturaSqlServer.cs (VERSIÓN MEJORADA CON DETECCIÓN DE TIPOS)
// Ruta: ApiGenericaCsharp/Repositorios/RepositorioLecturaSqlServer.cs
// Mejoras: Detección automática de tipos, conversión inteligente, manejo DATE vs DATETIME
// --------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using ApiGenericaCsharp.Repositorios.Abstracciones;
using ApiGenericaCsharp.Servicios.Abstracciones;
using ApiGenericaCsharp.Servicios.Utilidades;

namespace ApiGenericaCsharp.Repositorios
{
    /// <summary>
    /// Implementación mejorada para SQL Server con detección automática de tipos.
    /// 
    /// MEJORAS IMPLEMENTADAS:
    /// 1. DetectarTipoColumnaAsync() - Consulta information_schema
    /// 2. MapearTipoSqlServer() - Mapeo tipos SQL Server a SqlDbType
    /// 3. ConvertirValor() - Conversión inteligente de strings a tipos apropiados
    /// 4. ExtraerSoloFecha() - Manejo especial DATE vs DATETIME
    /// 5. Búsquedas inteligentes en DATETIME con solo fecha
    /// </summary>
    public class RepositorioLecturaSqlServer : IRepositorioLecturaTabla
    {
        private readonly IProveedorConexion _proveedorConexion;

        public RepositorioLecturaSqlServer(IProveedorConexion proveedorConexion)
        {
            _proveedorConexion = proveedorConexion ?? throw new ArgumentNullException(
                nameof(proveedorConexion),
                "IProveedorConexion no puede ser null."
            );
        }

        /// <summary>
        /// Detecta el tipo de una columna consultando information_schema.
        /// </summary>
        private async Task<SqlDbType?> DetectarTipoColumnaAsync(string nombreTabla, string esquema, string nombreColumna)
        {
            string sql = @"
                SELECT DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_SCHEMA = @esquema 
                AND TABLE_NAME = @tabla 
                AND COLUMN_NAME = @columna";

            try
            {
                string cadena = _proveedorConexion.ObtenerCadenaConexion();
                using var conexion = new SqlConnection(cadena);
                await conexion.OpenAsync();

                using var comando = new SqlCommand(sql, conexion);
                comando.Parameters.AddWithValue("@esquema", esquema);
                comando.Parameters.AddWithValue("@tabla", nombreTabla);
                comando.Parameters.AddWithValue("@columna", nombreColumna);

                using var lector = await comando.ExecuteReaderAsync();
                if (await lector.ReadAsync())
                {
                    string dataType = lector.GetString(0);
                    return MapearTipoSqlServer(dataType);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Advertencia: No se pudo detectar tipo de columna {nombreColumna}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Mapea tipos de SQL Server a SqlDbType.
        /// </summary>
        private SqlDbType? MapearTipoSqlServer(string dataType)
        {
            return dataType.ToLower() switch
            {
                // TIPOS ENTEROS
                "int" => SqlDbType.Int,
                "bigint" => SqlDbType.BigInt,
                "smallint" => SqlDbType.SmallInt,
                "tinyint" => SqlDbType.TinyInt,

                // TIPOS DECIMALES
                "decimal" => SqlDbType.Decimal,
                "numeric" => SqlDbType.Decimal,
                "money" => SqlDbType.Money,
                "smallmoney" => SqlDbType.SmallMoney,
                "float" => SqlDbType.Float,
                "real" => SqlDbType.Real,

                // TIPOS DE TEXTO
                "varchar" => SqlDbType.VarChar,
                "char" => SqlDbType.Char,
                "text" => SqlDbType.Text,
                "nvarchar" => SqlDbType.NVarChar,
                "nchar" => SqlDbType.NChar,
                "ntext" => SqlDbType.NText,

                // TIPOS ESPECIALES
                "bit" => SqlDbType.Bit,
                "uniqueidentifier" => SqlDbType.UniqueIdentifier,

                // TIPOS DE FECHA Y HORA
                "datetime" => SqlDbType.DateTime,
                "datetime2" => SqlDbType.DateTime2,
                "date" => SqlDbType.Date,
                "time" => SqlDbType.Time,
                "datetimeoffset" => SqlDbType.DateTimeOffset,
                "smalldatetime" => SqlDbType.SmallDateTime,

                // TIPOS BINARIOS
                "binary" => SqlDbType.Binary,
                "varbinary" => SqlDbType.VarBinary,
                "image" => SqlDbType.Image,

                _ => null
            };
        }

        /// <summary>
        /// Convierte un valor string al tipo apropiado según SqlDbType detectado.
        /// </summary>
        private object ConvertirValor(string valor, SqlDbType? tipoDestino)
        {
            if (tipoDestino == null) return valor;

            try
            {
                return tipoDestino switch
                {
                    SqlDbType.Int => int.Parse(valor),
                    SqlDbType.BigInt => long.Parse(valor),
                    SqlDbType.SmallInt => short.Parse(valor),
                    SqlDbType.TinyInt => byte.Parse(valor),
                    SqlDbType.Decimal => decimal.Parse(valor),
                    SqlDbType.Money => decimal.Parse(valor),
                    SqlDbType.SmallMoney => decimal.Parse(valor),
                    SqlDbType.Float => double.Parse(valor),
                    SqlDbType.Real => float.Parse(valor),
                    SqlDbType.Bit => bool.Parse(valor),
                    SqlDbType.UniqueIdentifier => Guid.Parse(valor),
                    SqlDbType.DateTime => DateTime.Parse(valor),
                    SqlDbType.DateTime2 => DateTime.Parse(valor),
                    SqlDbType.Date => ExtraerSoloFecha(valor),
                    SqlDbType.Time => TimeOnly.Parse(valor),
                    SqlDbType.DateTimeOffset => DateTimeOffset.Parse(valor),
                    SqlDbType.SmallDateTime => DateTime.Parse(valor),
                    SqlDbType.VarChar => valor,
                    SqlDbType.Char => valor,
                    SqlDbType.Text => valor,
                    SqlDbType.NVarChar => valor,
                    SqlDbType.NChar => valor,
                    SqlDbType.NText => valor,
                    _ => valor
                };
            }
            catch
            {
                return valor;
            }
        }

        /// <summary>
        /// Extrae solo la fecha de un string.
        /// </summary>
        private DateOnly ExtraerSoloFecha(string valor)
        {
            if (DateTime.TryParse(valor, out DateTime fechaCompleta))
                return DateOnly.FromDateTime(fechaCompleta);
            
            if (DateOnly.TryParse(valor, out DateOnly soloFecha))
                return soloFecha;
            
            throw new FormatException($"No se pudo convertir '{valor}' a fecha.");
        }

        /// <summary>
        /// Detecta si un valor parece ser una fecha sin hora (YYYY-MM-DD).
        /// </summary>
        private bool EsFechaSinHora(string valor)
        {
            return valor.Length == 10 && 
                   valor.Count(c => c == '-') == 2 &&
                   !valor.Contains("T") && 
                   !valor.Contains(":");
        }

        public async Task<IReadOnlyList<Dictionary<string, object?>>> ObtenerFilasAsync(
            string nombreTabla,
            string? esquema,
            int? limite)
        {
            if (string.IsNullOrWhiteSpace(nombreTabla))
                throw new ArgumentException("El nombre de la tabla no puede estar vacío.", nameof(nombreTabla));

            string esquemaFinal = string.IsNullOrWhiteSpace(esquema) ? "dbo" : esquema.Trim();
            int limiteFinal = limite ?? 1000;

            string sql = $"SELECT TOP ({limiteFinal}) * FROM [{esquemaFinal}].[{nombreTabla}]";
            var resultados = new List<Dictionary<string, object?>>();

            try
            {
                string cadenaConexion = _proveedorConexion.ObtenerCadenaConexion();

                using var conexion = new SqlConnection(cadenaConexion);
                await conexion.OpenAsync();

                using var comando = new SqlCommand(sql, conexion);
                using var lector = await comando.ExecuteReaderAsync();

                while (await lector.ReadAsync())
                {
                    var fila = new Dictionary<string, object?>();
                    for (int i = 0; i < lector.FieldCount; i++)
                    {
                        string nombreColumna = lector.GetName(i);
                        object? valorColumna = lector.IsDBNull(i) ? null : lector.GetValue(i);
                        fila[nombreColumna] = valorColumna;
                    }
                    resultados.Add(fila);
                }
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException(
                    $"Error SQL al consultar '{esquemaFinal}.{nombreTabla}': {ex.Message}", ex);
            }

            return resultados;
        }

        public async Task<IReadOnlyList<Dictionary<string, object?>>> ObtenerPorClaveAsync(
            string nombreTabla,
            string? esquema,
            string nombreClave,
            string valor)
        {
            if (string.IsNullOrWhiteSpace(nombreTabla))
                throw new ArgumentException("El nombre de la tabla no puede estar vacío.", nameof(nombreTabla));
            if (string.IsNullOrWhiteSpace(nombreClave))
                throw new ArgumentException("El nombre de la clave no puede estar vacío.", nameof(nombreClave));
            if (string.IsNullOrWhiteSpace(valor))
                throw new ArgumentException("El valor no puede estar vacío.", nameof(valor));

            string esquemaFinal = string.IsNullOrWhiteSpace(esquema) ? "dbo" : esquema.Trim();
            var filas = new List<Dictionary<string, object?>>();

            try
            {
                var tipoColumna = await DetectarTipoColumnaAsync(nombreTabla, esquemaFinal, nombreClave);
                
                bool esBusquedaFechaSoloEnDatetime = 
                    (tipoColumna == SqlDbType.DateTime || tipoColumna == SqlDbType.DateTime2) && 
                    EsFechaSinHora(valor);
                
                string sql;
                object valorConvertido;
                SqlDbType tipoParametro;
                
                if (esBusquedaFechaSoloEnDatetime)
                {
                    sql = $"SELECT * FROM [{esquemaFinal}].[{nombreTabla}] WHERE CAST([{nombreClave}] AS DATE) = @valor";
                    valorConvertido = ExtraerSoloFecha(valor);
                    tipoParametro = SqlDbType.Date;
                }
                else
                {
                    sql = $"SELECT * FROM [{esquemaFinal}].[{nombreTabla}] WHERE [{nombreClave}] = @valor";
                    valorConvertido = ConvertirValor(valor, tipoColumna);
                    tipoParametro = tipoColumna ?? SqlDbType.NVarChar;
                }

                string cadena = _proveedorConexion.ObtenerCadenaConexion();

                using var conexion = new SqlConnection(cadena);
                await conexion.OpenAsync();

                using var comando = new SqlCommand(sql, conexion);
                
                if (tipoColumna.HasValue || esBusquedaFechaSoloEnDatetime)
                {
                    var parametro = comando.Parameters.Add("@valor", tipoParametro);
                    parametro.Value = valorConvertido;
                }
                else
                {
                    comando.Parameters.AddWithValue("@valor", valor);
                }

                using var lector = await comando.ExecuteReaderAsync();
                while (await lector.ReadAsync())
                {
                    var fila = new Dictionary<string, object?>();
                    for (int i = 0; i < lector.FieldCount; i++)
                    {
                        string nombreColumna = lector.GetName(i);
                        object? valorColumna = lector.IsDBNull(i) ? null : lector.GetValue(i);
                        fila[nombreColumna] = valorColumna;
                    }
                    filas.Add(fila);
                }
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException(
                    $"Error SQL al filtrar '{esquemaFinal}.{nombreTabla}': {ex.Message}", ex);
            }

            return filas;
        }

        public async Task<bool> CrearAsync(
            string nombreTabla,
            string? esquema,
            Dictionary<string, object?> datos,
            string? camposEncriptar = null)
        {
            if (string.IsNullOrWhiteSpace(nombreTabla))
                throw new ArgumentException("El nombre de la tabla no puede estar vacío.", nameof(nombreTabla));
            if (datos == null || !datos.Any())
                throw new ArgumentException("Los datos no pueden estar vacíos.", nameof(datos));

            string esquemaFinal = string.IsNullOrWhiteSpace(esquema) ? "dbo" : esquema.Trim();

            var datosFinales = new Dictionary<string, object?>(datos);
            
            // Encriptar campos si se especificaron
            if (!string.IsNullOrWhiteSpace(camposEncriptar))
            {
                var camposAEncriptar = camposEncriptar.Split(',')
                    .Select(c => c.Trim())
                    .Where(c => !string.IsNullOrEmpty(c))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var campo in camposAEncriptar)
                {
                    if (datosFinales.ContainsKey(campo) && datosFinales[campo] != null)
                    {
                        string valorOriginal = datosFinales[campo]?.ToString() ?? "";
                        datosFinales[campo] = EncriptacionBCrypt.Encriptar(valorOriginal);
                    }
                }
            }

            var columnas = string.Join(", ", datosFinales.Keys.Select(k => $"[{k}]"));
            var parametros = string.Join(", ", datosFinales.Keys.Select(k => $"@{k}"));
            string sql = $"INSERT INTO [{esquemaFinal}].[{nombreTabla}] ({columnas}) VALUES ({parametros})";

            try
            {
                string cadena = _proveedorConexion.ObtenerCadenaConexion();

                using var conexion = new SqlConnection(cadena);
                await conexion.OpenAsync();

                using var comando = new SqlCommand(sql, conexion);

                foreach (var kvp in datosFinales)
                {
                    var tipoColumna = await DetectarTipoColumnaAsync(nombreTabla, esquemaFinal, kvp.Key);
                    
                    if (kvp.Value == null)
                    {
                        comando.Parameters.AddWithValue($"@{kvp.Key}", DBNull.Value);
                    }
                    else if (tipoColumna.HasValue && kvp.Value is string valorString)
                    {
                        object valorConvertido = ConvertirValor(valorString, tipoColumna);
                        var parametro = comando.Parameters.Add($"@{kvp.Key}", tipoColumna.Value);
                        parametro.Value = valorConvertido;
                    }
                    else
                    {
                        comando.Parameters.AddWithValue($"@{kvp.Key}", kvp.Value);
                    }
                }

                int filasAfectadas = await comando.ExecuteNonQueryAsync();
                return filasAfectadas > 0;
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException(
                    $"Error SQL al insertar en '{esquemaFinal}.{nombreTabla}': {ex.Message}", ex);
            }
        }

        public async Task<int> ActualizarAsync(
            string nombreTabla,
            string? esquema,
            string nombreClave,
            string valorClave,
            Dictionary<string, object?> datos,
            string? camposEncriptar = null)
        {
            if (string.IsNullOrWhiteSpace(nombreTabla))
                throw new ArgumentException("El nombre de la tabla no puede estar vacío.", nameof(nombreTabla));
            if (string.IsNullOrWhiteSpace(nombreClave))
                throw new ArgumentException("El nombre de la clave no puede estar vacío.", nameof(nombreClave));
            if (string.IsNullOrWhiteSpace(valorClave))
                throw new ArgumentException("El valor de la clave no puede estar vacío.", nameof(valorClave));
            if (datos == null || !datos.Any())
                throw new ArgumentException("Los datos no pueden estar vacíos.", nameof(datos));

            string esquemaFinal = string.IsNullOrWhiteSpace(esquema) ? "dbo" : esquema.Trim();

            var datosFinales = new Dictionary<string, object?>(datos);
            
            if (!string.IsNullOrWhiteSpace(camposEncriptar))
            {
                var camposAEncriptar = camposEncriptar.Split(',')
                    .Select(c => c.Trim())
                    .Where(c => !string.IsNullOrEmpty(c))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var campo in camposAEncriptar)
                {
                    if (datosFinales.ContainsKey(campo) && datosFinales[campo] != null)
                    {
                        string valorOriginal = datosFinales[campo]?.ToString() ?? "";
                        datosFinales[campo] = EncriptacionBCrypt.Encriptar(valorOriginal);
                    }
                }
            }

            try
            {
                var tipoColumna = await DetectarTipoColumnaAsync(nombreTabla, esquemaFinal, nombreClave);
                object valorClaveConvertido = ConvertirValor(valorClave, tipoColumna);

                var clausulaSet = string.Join(", ", datosFinales.Keys.Select(k => $"[{k}] = @{k}"));
                string sql = $"UPDATE [{esquemaFinal}].[{nombreTabla}] SET {clausulaSet} WHERE [{nombreClave}] = @valorClave";

                string cadena = _proveedorConexion.ObtenerCadenaConexion();

                using var conexion = new SqlConnection(cadena);
                await conexion.OpenAsync();

                using var comando = new SqlCommand(sql, conexion);

                foreach (var kvp in datosFinales)
                {
                    var tipoColumnaSet = await DetectarTipoColumnaAsync(nombreTabla, esquemaFinal, kvp.Key);
                    
                    if (kvp.Value == null)
                    {
                        comando.Parameters.AddWithValue($"@{kvp.Key}", DBNull.Value);
                    }
                    else if (tipoColumnaSet.HasValue && kvp.Value is string valorString)
                    {
                        object valorConvertido = ConvertirValor(valorString, tipoColumnaSet);
                        var parametro = comando.Parameters.Add($"@{kvp.Key}", tipoColumnaSet.Value);
                        parametro.Value = valorConvertido;
                    }
                    else
                    {
                        comando.Parameters.AddWithValue($"@{kvp.Key}", kvp.Value);
                    }
                }

                if (tipoColumna.HasValue)
                {
                    var parametro = comando.Parameters.Add("@valorClave", tipoColumna.Value);
                    parametro.Value = valorClaveConvertido;
                }
                else
                {
                    comando.Parameters.AddWithValue("@valorClave", valorClave);
                }

                return await comando.ExecuteNonQueryAsync();
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException(
                    $"Error SQL al actualizar '{esquemaFinal}.{nombreTabla}': {ex.Message}", ex);
            }
        }

        public async Task<int> EliminarAsync(
            string nombreTabla,
            string? esquema,
            string nombreClave,
            string valorClave)
        {
            if (string.IsNullOrWhiteSpace(nombreTabla))
                throw new ArgumentException("El nombre de la tabla no puede estar vacío.", nameof(nombreTabla));
            if (string.IsNullOrWhiteSpace(nombreClave))
                throw new ArgumentException("El nombre de la clave no puede estar vacío.", nameof(nombreClave));
            if (string.IsNullOrWhiteSpace(valorClave))
                throw new ArgumentException("El valor de la clave no puede estar vacío.", nameof(valorClave));

            string esquemaFinal = string.IsNullOrWhiteSpace(esquema) ? "dbo" : esquema.Trim();

            try
            {
                var tipoColumna = await DetectarTipoColumnaAsync(nombreTabla, esquemaFinal, nombreClave);
                object valorConvertido = ConvertirValor(valorClave, tipoColumna);

                string sql = $"DELETE FROM [{esquemaFinal}].[{nombreTabla}] WHERE [{nombreClave}] = @valorClave";
                
                string cadena = _proveedorConexion.ObtenerCadenaConexion();

                using var conexion = new SqlConnection(cadena);
                await conexion.OpenAsync();

                using var comando = new SqlCommand(sql, conexion);

                if (tipoColumna.HasValue)
                {
                    var parametro = comando.Parameters.Add("@valorClave", tipoColumna.Value);
                    parametro.Value = valorConvertido;
                }
                else
                {
                    comando.Parameters.AddWithValue("@valorClave", valorClave);
                }

                return await comando.ExecuteNonQueryAsync();
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException(
                    $"Error SQL al eliminar de '{esquemaFinal}.{nombreTabla}': {ex.Message}", ex);
            }
        }

        public async Task<string?> ObtenerHashContrasenaAsync(
            string nombreTabla,
            string? esquema,
            string campoUsuario,
            string campoContrasena,
            string valorUsuario)
        {
            if (string.IsNullOrWhiteSpace(nombreTabla))
                throw new ArgumentException("El nombre de la tabla no puede estar vacío.", nameof(nombreTabla));
            if (string.IsNullOrWhiteSpace(campoUsuario))
                throw new ArgumentException("El campo de usuario no puede estar vacío.", nameof(campoUsuario));
            if (string.IsNullOrWhiteSpace(campoContrasena))
                throw new ArgumentException("El campo de contraseña no puede estar vacío.", nameof(campoContrasena));
            if (string.IsNullOrWhiteSpace(valorUsuario))
                throw new ArgumentException("El valor de usuario no puede estar vacío.", nameof(valorUsuario));

            string esquemaFinal = string.IsNullOrWhiteSpace(esquema) ? "dbo" : esquema.Trim();

            try
            {
                var tipoColumna = await DetectarTipoColumnaAsync(nombreTabla, esquemaFinal, campoUsuario);
                object valorConvertido = ConvertirValor(valorUsuario, tipoColumna);

                string sql = $"SELECT [{campoContrasena}] FROM [{esquemaFinal}].[{nombreTabla}] WHERE [{campoUsuario}] = @valorUsuario";
                
                string cadena = _proveedorConexion.ObtenerCadenaConexion();

                using var conexion = new SqlConnection(cadena);
                await conexion.OpenAsync();

                using var comando = new SqlCommand(sql, conexion);

                if (tipoColumna.HasValue)
                {
                    var parametro = comando.Parameters.Add("@valorUsuario", tipoColumna.Value);
                    parametro.Value = valorConvertido;
                }
                else
                {
                    comando.Parameters.AddWithValue("@valorUsuario", valorUsuario);
                }

                var resultado = await comando.ExecuteScalarAsync();
                return resultado?.ToString();
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException(
                    $"Error SQL al obtener hash de '{esquemaFinal}.{nombreTabla}': {ex.Message}", ex);
            }
        }

        public async Task<Dictionary<string, object?>> ObtenerDiagnosticoConexionAsync()
        {
            string sql = @"
                SELECT
                    DB_NAME() AS nombre_base_datos,
                    SCHEMA_NAME() AS esquema_actual,
                    @@VERSION AS version_servidor,
                    @@SERVERNAME AS nombre_servidor,
                    sqlserver_start_time AS hora_inicio_servidor,
                    SUSER_SNAME() AS usuario_actual,
                    @@SPID AS id_proceso_conexion,
                    SERVERPROPERTY('InstanceName') AS nombre_instancia,
                    SERVERPROPERTY('Edition') AS edicion_sqlserver,
                    SERVERPROPERTY('ProductVersion') AS version_producto
                FROM sys.dm_os_sys_info";

            try
            {
                string cadenaConexion = _proveedorConexion.ObtenerCadenaConexion();

                using var conexion = new SqlConnection(cadenaConexion);
                await conexion.OpenAsync();

                using var comando = new SqlCommand(sql, conexion);
                using var lector = await comando.ExecuteReaderAsync();

                if (!await lector.ReadAsync())
                    throw new InvalidOperationException("No se pudo obtener información de diagnóstico.");

                string nombreBaseDatos = lector.GetString(0);
                string? esquemaActual = lector.IsDBNull(1) ? "dbo" : lector.GetString(1);
                string versionCompleta = lector.GetString(2);
                string nombreServidor = lector.GetString(3);
                DateTime horaInicio = lector.GetDateTime(4);
                string usuarioActual = lector.GetString(5);
                int idProceso = lector.GetInt16(6);
                string? nombreInstancia = lector.IsDBNull(7) ? null : lector.GetString(7);
                string edicion = lector.GetString(8);
                string versionProducto = lector.GetString(9);

                TimeSpan tiempoEncendido = DateTime.Now - horaInicio;

                return new Dictionary<string, object?>
                {
                    ["proveedor"] = "SQL Server",
                    ["baseDatos"] = nombreBaseDatos,
                    ["esquema"] = esquemaActual,
                    ["version"] = versionCompleta,
                    ["versionProducto"] = versionProducto,
                    ["edicion"] = edicion,
                    ["servidor"] = nombreServidor,
                    ["instancia"] = nombreInstancia ?? "(Instancia predeterminada)",
                    ["horaInicio"] = horaInicio.ToString("yyyy-MM-ddTHH:mm:ss"),
                    ["usuarioConectado"] = usuarioActual,
                    ["idProcesoConexion"] = idProceso,
                    ["tiempoEncendido"] = $"{(int)tiempoEncendido.TotalDays} días, {tiempoEncendido.Hours} horas"
                };
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException($"Error SQL al obtener diagnóstico: {ex.Message}", ex);
            }
        }
    }
}

