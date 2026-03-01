// IRepositorioLecturaTabla.cs — Interface que define el contrato para lectura genérica de tablas
// Ubicación: Repositorios/Abstracciones/IRepositorioLecturaTabla.cs
//
// Principios SOLID aplicados:
// - SRP: Esta interface solo define operaciones de LECTURA, no mezcla responsabilidades
// - DIP: Permite que los servicios dependan de esta abstracción, no de implementaciones concretas
// - OCP: Abierta para extensión (nuevas implementaciones) pero cerrada para modificación
// - ISP: Interface específica y enfocada, solo contiene métodos de lectura

using System.Collections.Generic;   // Habilita List<> y Dictionary<> para colecciones
using System.Threading.Tasks;       // Habilita Task y async/await para operaciones asíncronas

namespace ApiGenericaCsharp.Repositorios.Abstracciones
{
    /// <summary>
    /// Contrato para repositorios que realizan operaciones de lectura sobre tablas de base de datos.
    /// 
    /// Esta interface define el "QUÉ" se puede hacer (leer datos), no el "CÓMO" se hace.
    /// Cada implementación concreta definirá cómo conectar y ejecutar las consultas
    /// (ADO.NET, Entity Framework, proveedores específicos, etc.).
    /// 
    /// Beneficios de esta abstracción:
    /// - Permite intercambiar proveedores de base de datos sin cambiar código cliente
    /// - Facilita testing mediante mocks
    /// - Desacopla la lógica de negocio del acceso a datos específico
    /// - Soporta múltiples implementaciones simultáneas
    /// </summary>
    public interface IRepositorioLecturaTabla
    {
        /// <summary>
        /// Obtiene filas de una tabla específica como lista de diccionarios.
        /// 
        /// Cada fila se representa como Dictionary<string, object?> donde:
        /// - La clave (string) es el nombre de la columna
        /// - El valor (object?) es el dato de esa columna (puede ser null)
        /// 
        /// Esta estructura genérica permite trabajar con CUALQUIER tabla sin necesidad
        /// de crear modelos/entidades específicas para cada una.
        /// 
        /// Comportamiento esperado:
        /// - Si nombreTabla no existe, debería lanzar excepción descriptiva
        /// - Si esquema es null, usar el esquema por defecto del proveedor
        /// - Si limite es null, aplicar límite por defecto (ej: 1000) o sin límite
        /// - Devolver lista vacía si no hay registros (no null)
        /// </summary>
        /// <param name="nombreTabla">
        /// Nombre de la tabla a consultar. Obligatorio y no puede estar vacío.
        /// Ejemplo: "usuarios", "productos", "pedidos"
        /// </param>
        /// <param name="esquema">
        /// Esquema/schema de la tabla. Opcional.
        /// - SQL Server: "dbo" (por defecto)
        /// - PostgreSQL: "public" (por defecto)  
        /// - MariaDB: null (no usa esquemas)
        /// Si es null, la implementación debe usar su valor por defecto.
        /// </param>
        /// <param name="limite">
        /// Máximo número de filas a devolver. Opcional.
        /// Si es null, la implementación puede:
        /// - No aplicar límite
        /// - Aplicar un límite por defecto (recomendado: 1000)
        /// Si es <= 0, debería lanzar excepción
        /// </param>
        /// <returns>
        /// Lista inmutable de filas donde cada fila es un diccionario:
        /// - Clave: nombre de columna (string)
        /// - Valor: dato de la columna (object?, puede ser null)
        /// 
        /// La lista es IReadOnlyList para evitar modificaciones accidentales.
        /// Si no hay registros, devuelve lista vacía (no null).
        /// 
        /// Ejemplos de estructura devuelta:
        /// [
        ///   { "id": 1, "nombre": "Juan", "email": "juan@test.com", "activo": true },
        ///   { "id": 2, "nombre": "María", "email": "maria@test.com", "activo": false }
        /// ]
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Se lanza cuando nombreTabla es null, vacío o contiene caracteres inválidos
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Se lanza cuando la tabla no existe o no se puede acceder a ella
        /// </exception>
        /// <exception cref="System.Data.Common.DbException">
        /// Se lanza cuando hay errores de conexión o consulta a la base de datos
        /// </exception>
        Task<IReadOnlyList<Dictionary<string, object?>>> ObtenerFilasAsync(
            string nombreTabla,
            string? esquema,
            int? limite
        );
        /// <summary>
        /// Obtiene filas filtradas por un valor específico en una columna.
        /// </summary>
        /// <param name="nombreTabla">Nombre de la tabla a consultar</param>
        /// <param name="esquema">Esquema de la tabla (opcional)</param>
        /// <param name="nombreClave">Nombre de la columna para filtrar</param>
        /// <param name="valor">Valor a buscar en la columna</param>
        /// <returns>Lista de filas que coinciden con el criterio</returns>
        Task<IReadOnlyList<Dictionary<string, object?>>> ObtenerPorClaveAsync(
            string nombreTabla,
            string? esquema,
            string nombreClave,
            string valor
        );
        /// <summary>
        /// Crea un nuevo registro en la tabla especificada.
        /// </summary>
        /// <param name="nombreTabla">Nombre de la tabla donde insertar</param>
        /// <param name="esquema">Esquema de la tabla (opcional)</param>
        /// <param name="datos">Datos a insertar como diccionario columna-valor</param>
        /// <param name="camposEncriptar">Lista de campos que deben ser encriptados, separados por coma (opcional)</param>
        /// <returns>True si se insertó correctamente</returns>
        Task<bool> CrearAsync(
            string nombreTabla,
            string? esquema,
            Dictionary<string, object?> datos,
            string? camposEncriptar = null
        );
        /// <summary>
        /// Actualiza un registro existente en la tabla especificada.
        /// </summary>
        /// <param name="nombreTabla">Nombre de la tabla donde actualizar</param>
        /// <param name="esquema">Esquema de la tabla (opcional)</param>
        /// <param name="nombreClave">Columna que identifica el registro a actualizar</param>
        /// <param name="valorClave">Valor de la clave para identificar el registro</param>
        /// <param name="datos">Nuevos datos a actualizar</param>
        /// <param name="camposEncriptar">Campos que deben ser encriptados, separados por coma (opcional)</param>
        /// <returns>Número de filas afectadas (0 si no se encontró el registro)</returns>
        Task<int> ActualizarAsync(
            string nombreTabla,
            string? esquema,
            string nombreClave,
            string valorClave,
            Dictionary<string, object?> datos,
            string? camposEncriptar = null
        );
        /// <summary>
        /// Elimina un registro existente de la tabla especificada.
        /// </summary>
        /// <param name="nombreTabla">Nombre de la tabla donde eliminar</param>
        /// <param name="esquema">Esquema de la tabla (opcional)</param>
        /// <param name="nombreClave">Columna que identifica el registro a eliminar</param>
        /// <param name="valorClave">Valor de la clave para identificar el registro</param>
        /// <returns>Número de filas eliminadas (0 si no se encontró el registro)</returns>
        Task<int> EliminarAsync(
            string nombreTabla,
            string? esquema,
            string nombreClave,
            string valorClave
        );
        /// <summary>
        /// Verifica credenciales de usuario obteniendo el hash almacenado para comparación.
        /// </summary>
        /// <param name="nombreTabla">Nombre de la tabla que contiene usuarios</param>
        /// <param name="esquema">Esquema de la tabla (opcional)</param>
        /// <param name="campoUsuario">Columna que identifica al usuario (ej: email, username)</param>
        /// <param name="campoContrasena">Columna que contiene el hash de la contraseña</param>
        /// <param name="valorUsuario">Valor del usuario a buscar</param>
        /// <returns>Hash de contraseña almacenado o null si no se encuentra el usuario</returns>
        Task<string?> ObtenerHashContrasenaAsync(
            string nombreTabla,
            string? esquema,
            string campoUsuario,
            string campoContrasena,
            string valorUsuario
        );

        /// <summary>
        /// Obtiene información de diagnóstico sobre la conexión actual a la base de datos.
        ///
        /// Este método consulta metadatos del sistema del motor de base de datos específico
        /// para obtener información útil para troubleshooting y diagnóstico:
        /// - Nombre de la base de datos conectada
        /// - Versión del motor de base de datos
        /// - Host/servidor conectado
        /// - Puerto de conexión
        /// - Hora de inicio del servidor (clave para distinguir instancias Docker vs locales)
        /// - Usuario de la conexión actual
        /// - ID del proceso/sesión
        ///
        /// IMPLEMENTACIÓN POR MOTOR:
        /// - PostgreSQL: consulta current_database(), version(), pg_postmaster_start_time(), etc.
        /// - SQL Server: consulta DB_NAME(), @@VERSION, sqlserver_start_time, etc.
        /// - MySQL/MariaDB: consulta DATABASE(), VERSION(), Uptime, etc.
        /// - Oracle: consulta SYS_CONTEXT, v$version, v$instance, etc.
        ///
        /// NOTA DE SEGURIDAD:
        /// Este método NO debe devolver credenciales ni la cadena de conexión completa.
        /// Solo metadatos públicos del servidor para propósitos de diagnóstico.
        /// </summary>
        /// <returns>
        /// Diccionario con información de diagnóstico. Claves comunes:
        /// - "proveedor": nombre del motor (ej: "PostgreSQL", "SQL Server")
        /// - "baseDatos": nombre de la base de datos actual
        /// - "version": versión completa del motor
        /// - "servidor": nombre/host del servidor
        /// - "puerto": puerto de conexión (si está disponible)
        /// - "horaInicio": timestamp de inicio del servidor
        /// - "usuarioConectado": usuario de la conexión actual
        /// - "idProcesoConexion": ID del proceso/sesión
        ///
        /// Las claves pueden variar según el motor de base de datos.
        /// </returns>
        /// <exception cref="NotImplementedException">
        /// Se lanza si la implementación actual no soporta diagnóstico de conexión
        /// </exception>
        /// <exception cref="System.Data.Common.DbException">
        /// Se lanza cuando hay errores de conexión o consulta a la base de datos
        /// </exception>
        Task<Dictionary<string, object?>> ObtenerDiagnosticoConexionAsync();

// NOTA: No se incluye método para obtener una sola fila por clave primaria
        // porque ObtenerPorClaveAsync puede devolver múltiples filas si la clave no es única.
        // Si se necesita garantizar unicidad, se puede implementar en la lógica de negocio.
    }
}

// NOTAS PEDAGÓGICAS para el tutorial:
//
// 1. ESTA ES UNA ABSTRACCIÓN PARA ACCESO A DATOS:
//    - Define QUÉ operaciones de lectura se pueden hacer
//    - NO define CÓMO se conecta a la base de datos específica
//    - Es el contrato que implementarán todos los repositorios concretos
//
// 2. ¿POR QUÉ USAR DICTIONARY<STRING, OBJECT?>?
//    - Flexibilidad: funciona con cualquier tabla sin crear modelos específicos
//    - Genérico: no necesitas saber las columnas de antemano
//    - Dinámico: las columnas se descubren en tiempo de ejecución
//
// 3. ¿POR QUÉ IREADONLYLIST?
//    - Evita modificaciones accidentales de los datos devueltos
//    - Indica claramente que es solo para lectura
//    - Mejor práctica para APIs que devuelven colecciones
//
// 4. ¿QUÉ VIENE DESPUÉS?
//    - Crear implementaciones concretas: RepositorioLecturaSqlServer, RepositorioLecturaPostgreSQL
//    - Cada implementación usará IProveedorConexion para obtener conexiones
//    - Registrar en Program.cs la implementación que se quiera usar
//
// 5. EJEMPLO DE IMPLEMENTACIONES FUTURAS:
//    public class RepositorioLecturaSqlServer : IRepositorioLecturaTabla
//    {
//        private readonly IProveedorConexion _proveedor;
//        
//        public RepositorioLecturaSqlServer(IProveedorConexion proveedor)
//        {
//            _proveedor = proveedor;
//        }
//        
//        public async Task<IReadOnlyList<Dictionary<string, object?>>> ObtenerFilasAsync(...)
//        {
//            var cadenaConexion = _proveedor.ObtenerCadenaConexion();
//            // ... lógica específica de SQL Server
//        }
//    }
//
// 6. PRÓXIMO PASO EN PROGRAM.CS:
//    Después de crear las implementaciones, se podrá descomentar:
//    builder.Services.AddScoped<IRepositorioLecturaTabla, RepositorioLecturaSqlServer>();

