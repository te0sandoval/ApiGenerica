// IServicioCrud.cs — Interface que define el contrato para operaciones de lógica de negocio
// Ubicación: Servicios/Abstracciones/IServicioCrud.cs
//
// Principios SOLID aplicados:
// - SRP: Esta interface solo define operaciones de lógica de negocio, no acceso a datos ni HTTP
// - DIP: Permite que el controlador dependa de esta abstracción, no de implementaciones concretas
// - ISP: Interface específica y pequeña, empezamos solo con ListarAsync para no forzar implementaciones innecesarias
// - OCP: Abierta para extensión (agregar más métodos CRUD) pero cerrada para modificación

using System.Collections.Generic;   // Para usar List<> y Dictionary<> genéricos
using System.Threading.Tasks;       // Para programación asíncrona con async/await

namespace ApiGenericaCsharp.Servicios.Abstracciones
{
    /// <summary>
    /// Interface que define el contrato para un servicio CRUD genérico.
    /// 
    /// Esta capa de servicios se encarga de aplicar la lógica de negocio:
    /// - Aplicar reglas de negocio y validaciones específicas del dominio
    /// - Coordinar operaciones entre múltiples repositorios si es necesario
    /// - Transformar datos según necesidades específicas del negocio
    /// - Aislar la lógica de negocio de los detalles técnicos de acceso a datos
    /// - Proporcionar una API de alto nivel que entienda conceptos del dominio
    /// 
    /// ¿Por qué necesitamos una capa de servicios separada?
    /// 
    /// Analogía del restaurante expandida:
    /// - Repositorio = Bodega: "Dame todos los tomates que tengo"
    /// - Servicio = Chef: "Dame tomates, pero solo los maduros, máximo 10, y si no hay suficientes, sustitúyelos por pimientos"
    /// - Controlador = Mesero: "El cliente pidió ensalada, prepárala según las reglas de la casa"
    /// 
    /// El controlador (mesero) no debe saber sobre reglas de cocina (negocio)
    /// El repositorio (bodega) no debe saber sobre recetas (negocio)
    /// El servicio (chef) es quien conoce las reglas de cocina y coordina todo
    /// 
    /// Responsabilidades específicas de esta capa:
    /// - Validaciones de negocio (no técnicas): "¿Esta tabla es consultable por este usuario?"
    /// - Reglas de dominio: "Máximo 1000 registros para usuarios normales, 5000 para admins"
    /// - Transformaciones de negocio: "Ocultar campos sensibles según el perfil"
    /// - Auditoría y logging: "Registrar quién consulta qué tablas cuándo"
    /// - Coordinación: "Si consulta 'usuarios', también traer datos de 'perfiles'"
    /// 
    /// ¿Por qué no hacer esto en el controlador?
    /// - El controlador debe ser "tonto" y solo manejar HTTP (SRP)
    /// - Las reglas de negocio deben ser reutilizables desde diferentes interfaces
    /// - Facilita testing: se pueden probar reglas de negocio independientemente
    /// - Permite cambiar la interfaz (REST, GraphQL, gRPC) sin tocar lógica de negocio
    /// 
    /// Por ahora solo incluimos ListarAsync, siguiendo ISP (Interface Segregation).
    /// Conforme el tutorial avance, se irán agregando más métodos CRUD según se necesiten.
    /// </summary>
    public interface IServicioCrud
    {
        /// <summary>
        /// Lista registros de una tabla aplicando reglas de negocio específicas del dominio.
        /// 
        /// Este método es similar a IRepositorioLecturaTabla.ObtenerFilasAsync en signatura,
        /// pero completamente diferente en semántica y responsabilidades:
        /// 
        /// DIFERENCIAS CLAVE CON EL REPOSITORIO:
        /// 
        /// Repositorio (capa técnica):
        /// - "Dame todos los registros de la tabla X que existan"
        /// - No aplica reglas de negocio, solo ejecuta SQL
        /// - No sabe nada sobre permisos, límites de seguridad, etc.
        /// - Su único criterio es: "¿La tabla existe? ¿La consulta es válida?"
        /// 
        /// Servicio (capa de negocio):
        /// - "Dame registros de la tabla X, pero aplicando las reglas de la empresa"
        /// - Valida permisos: "¿Este usuario puede ver esta tabla?"
        /// - Aplica límites de seguridad: "Máximo 1000 registros para usuarios normales"
        /// - Filtra datos sensibles: "Ocultar columna 'password' si no eres admin"
        /// - Registra auditoría: "Log: Usuario Juan consultó tabla 'empleados' a las 14:30"
        /// 
        /// FLUJO TÍPICO DE ESTE MÉTODO:
        /// 1. Validar parámetros de entrada (no null, caracteres válidos)
        /// 2. Aplicar reglas de autorización ("¿Puede este usuario ver esta tabla?")
        /// 3. Aplicar reglas de límites ("¿Está pidiendo demasiados registros?")
        /// 4. Llamar al repositorio para obtener datos técnicos
        /// 5. Aplicar transformaciones de negocio (filtrar columnas, calcular campos)
        /// 6. Registrar auditoría de la operación
        /// 7. Devolver resultado procesado según reglas del dominio
        /// 
        /// EJEMPLOS DE REGLAS DE NEGOCIO QUE PODRÍA APLICAR:
        /// - Seguridad: "La tabla 'sueldos' solo es visible para el departamento de RRHH"
        /// - Performance: "Máximo 1000 registros para evitar timeouts en el navegador"
        /// - Auditoría: "Registrar todas las consultas a tablas con datos personales"
        /// - Transformación: "Convertir fechas a formato local del usuario"
        /// - Enriquecimiento: "Agregar campo calculado 'tiempo_desde_creacion'"
        /// - Filtrado: "Ocultar registros marcados como 'eliminado_logico = true'"
        /// </summary>
        /// <param name="nombreTabla">
        /// Nombre de la tabla a consultar. Obligatorio y debe cumplir reglas del dominio.
        /// 
        /// A diferencia del repositorio que solo valida sintaxis, el servicio puede validar:
        /// - Que la tabla esté en la lista de tablas permitidas para consulta externa
        /// - Que el usuario actual tenga permisos para acceder a esa tabla específica
        /// - Que el nombre no contenga caracteres que violen políticas de seguridad
        /// - Que no sea una tabla del sistema o de configuración interna
        /// - Que cumpla con reglas específicas del dominio empresarial
        /// 
        /// Ejemplos de validaciones de negocio:
        /// -  "usuarios" → Permitida para la mayoría de roles
        /// -  "productos" → Permitida para roles comerciales
        /// -  "sueldos" → Solo para RRHH
        /// -  "logs_sistema" → Solo para administradores técnicos
        /// -  "configuracion_interna" → Prohibida para consultas externas
        /// 
        /// El servicio debe proporcionar mensajes de error específicos del dominio:
        /// - No solo "Argumento inválido"
        /// - Sino "La tabla 'sueldos' requiere permisos de RRHH"
        /// </param>
        /// <param name="esquema">
        /// Esquema de la base de datos. Opcional y sujeto a reglas de negocio.
        /// 
        /// El servicio puede aplicar reglas de esquemas más sofisticadas que el repositorio:
        /// - Aplicar esquema por defecto según el rol del usuario o departamento
        /// - Validar que el usuario tenga acceso al esquema especificado
        /// - Mapear esquemas virtuales a esquemas físicos (abstracción del dominio)
        /// - Aplicar reglas de multi-tenancy si cada cliente tiene su esquema
        /// 
        /// Ejemplos de lógica de esquemas:
        /// - Si usuario es de "Ventas" → esquema por defecto "ventas"
        /// - Si usuario es de "RRHH" → esquema por defecto "recursos_humanos"
        /// - Si esquema es null → aplicar esquema según contexto del usuario
        /// - Si esquema es "publico" → permitir para todos
        /// - Si esquema es "confidencial" → validar permisos especiales
        /// 
        /// Si es null, se aplican reglas inteligentes por defecto del servicio.
        /// </param>
        /// <param name="limite">
        /// Máximo número de registros a devolver. Opcional pero importante para reglas de negocio.
        /// 
        /// El servicio puede aplicar lógica de límites mucho más sofisticada que el repositorio:
        /// - Límite máximo global basado en políticas empresariales
        /// - Límites diferentes según el tipo de usuario (básico: 100, premium: 1000, admin: sin límite)
        /// - Límites diferentes según el tipo de tabla (tablas grandes: máximo 500, tablas pequeñas: máximo 5000)
        /// - Límites dinámicos basados en la carga del servidor o horario
        /// - Registro de auditoría para consultas que piden límites altos
        /// 
        /// Ejemplos de reglas de límites:
        /// - Usuario básico + tabla "productos" → máximo 100 registros
        /// - Usuario premium + tabla "productos" → máximo 1000 registros  
        /// - Usuario admin + cualquier tabla → sin límite (pero con auditoría)
        /// - Cualquier usuario + tabla "logs" → máximo 50 (tabla muy grande)
        /// - Horario pico (9am-11am) → límites reducidos por performance
        /// 
        /// Si es null, se aplica límite inteligente por defecto según contexto.
        /// </param>
        /// <returns>
        /// Lista inmutable de diccionarios representando las filas obtenidas después de aplicar reglas de negocio.
        /// 
        /// Estructura similar a la del repositorio pero con procesamiento de negocio aplicado:
        /// - Cada elemento es un Dictionary<string, object?>
        /// - Clave: nombre de columna (posiblemente transformado según reglas de negocio)
        /// - Valor: dato de la columna (posiblemente procesado según reglas de dominio)
        /// 
        /// TRANSFORMACIONES DE NEGOCIO QUE PUEDE APLICAR EL SERVICIO:
        /// - Filtrado de columnas sensibles según permisos del usuario
        /// - Formateo de datos según preferencias del usuario (fechas, números)
        /// - Agregación de campos calculados específicos del dominio
        /// - Enmascaramiento de datos sensibles (mostrar solo últimos 4 dígitos de tarjetas)
        /// - Traducción de códigos internos a descripciones amigables
        /// - Aplicación de reglas de presentación específicas del negocio
        /// 
        /// Ejemplos de procesamiento de datos:
        /// - Campo "password" → removido si no eres admin
        /// - Campo "fecha_creacion" → formateado según zona horaria del usuario
        /// - Campo "estado_codigo" (1,2,3) → convertido a ("Activo","Inactivo","Pendiente")
        /// - Campo "salario" → enmascarado como "***" si no tienes permisos de RRHH
        /// - Agregar campo "dias_desde_registro" calculado en tiempo real
        /// 
        /// METADATOS ADICIONALES:
        /// El servicio puede enriquecer la respuesta con información del dominio:
        /// - Información de permisos aplicados
        /// - Razón por la cual ciertos campos fueron filtrados
        /// - Advertencias sobre límites aplicados
        /// - Información de auditoría (para logs)
        /// 
        /// Ejemplo de resultado procesado:
        /// [
        ///   { 
        ///     "id": 1, 
        ///     "nombre": "Juan Pérez", 
        ///     "email": "juan@empresa.com", 
        ///     "activo": true,
        ///     "dias_desde_registro": 45,  // Campo calculado por el servicio
        ///     "departamento_desc": "Ventas"  // Transformado de codigo "V01" a descripción
        ///   }
        /// ]
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Se lanza cuando hay problemas con los parámetros de entrada que violan reglas de negocio:
        /// - nombreTabla es null, vacío o contiene caracteres inválidos según políticas empresariales
        /// - La tabla no está en la lista de tablas permitidas para consulta externa
        /// - El esquema no es válido según reglas de negocio específicas del dominio
        /// - El límite excede el máximo permitido para el tipo de usuario actual
        /// - La combinación de parámetros viola alguna regla específica del dominio
        /// 
        /// Los mensajes deben ser específicos del dominio, no técnicos:
        /// -  "Argumento inválido en parámetro nombreTabla"
        /// -  "La tabla 'configuracion_sistema' no está disponible para consultas externas"
        /// -  "Su rol 'Usuario Básico' tiene un límite máximo de 500 registros"
        /// </exception>
        /// <exception cref="UnauthorizedAccessException">
        /// Se lanza cuando hay problemas de autorización específicos del dominio:
        /// - El usuario no tiene permisos para acceder a la tabla especificada
        /// - El usuario no tiene permisos para acceder al esquema especificado  
        /// - La operación viola políticas de seguridad empresariales
        /// - El usuario ha excedido su cuota de consultas diarias/horarias
        /// 
        /// Los mensajes deben explicar claramente las reglas de negocio:
        /// -  "Acceso a la tabla 'sueldos' requiere rol de Recursos Humanos"
        /// -  "Ha excedido su límite de 100 consultas por hora"
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Se lanza cuando hay problemas en la operación que no son culpa directa del usuario:
        /// - Errores de configuración del servicio (reglas mal configuradas)
        /// - Problemas de conectividad subyacentes (repositorio no disponible)
        /// - Violaciones de reglas de negocio complejas que indican problemas sistémicos
        /// - Estados inconsistentes en el sistema que impiden la operación
        /// 
        /// Estos errores típicamente requieren intervención técnica:
        /// -  "Servicio de permisos no disponible, contacte al administrador"
        /// -  "Reglas de negocio mal configuradas para la tabla solicitada"
        /// </exception>
        Task<IReadOnlyList<Dictionary<string, object?>>> ListarAsync(
            string nombreTabla,    // Tabla a consultar (con validaciones de dominio)
            string? esquema,       // Esquema (con reglas de negocio aplicadas)
            int? limite           // Límite (con políticas empresariales aplicadas)
        );
        /// <summary>
        /// Obtiene registros filtrados por una clave específica aplicando reglas de negocio.
        /// </summary>
        /// <param name="nombreTabla">Nombre de la tabla a consultar</param>
        /// <param name="esquema">Esquema de la tabla (opcional)</param>
        /// <param name="nombreClave">Nombre de la columna para filtrar</param>
        /// <param name="valor">Valor a buscar en la columna</param>
        /// <returns>Lista de registros que coinciden con el criterio</returns>
        Task<IReadOnlyList<Dictionary<string, object?>>> ObtenerPorClaveAsync(
            string nombreTabla,
            string? esquema,
            string nombreClave,
            string valor
        );
        /// <summary>
        /// Crea un nuevo registro aplicando reglas de negocio y validaciones.
        /// </summary>
        /// <param name="nombreTabla">Nombre de la tabla donde crear el registro</param>
        /// <param name="esquema">Esquema de la tabla (opcional)</param>
        /// <param name="datos">Datos a insertar como diccionario columna-valor</param>
        /// <param name="camposEncriptar">Campos que deben ser encriptados, separados por coma (opcional)</param>
        /// <returns>True si se creó el registro correctamente</returns>
        Task<bool> CrearAsync(
            string nombreTabla,
            string? esquema,
            Dictionary<string, object?> datos,
            string? camposEncriptar = null
        );

        /// <summary>
        /// Actualiza un registro existente aplicando reglas de negocio y validaciones.
        /// </summary>
        /// <param name="nombreTabla">Nombre de la tabla donde actualizar</param>
        /// <param name="esquema">Esquema de la tabla (opcional)</param>
        /// <param name="nombreClave">Columna que identifica el registro a actualizar</param>
        /// <param name="valorClave">Valor de la clave para identificar el registro</param>
        /// <param name="datos">Nuevos datos a actualizar</param>
        /// <param name="camposEncriptar">Campos que deben ser encriptados, separados por coma (opcional)</param>
        /// <returns>Número de filas afectadas (0 si no se encontró, >0 si se actualizó)</returns>
        Task<int> ActualizarAsync(
            string nombreTabla,
            string? esquema,
            string nombreClave,
            string valorClave,
            Dictionary<string, object?> datos,
            string? camposEncriptar = null
        );

        /// <summary>
        /// Elimina un registro existente aplicando reglas de negocio y validaciones.
        /// </summary>
        /// <param name="nombreTabla">Nombre de la tabla donde eliminar</param>
        /// <param name="esquema">Esquema de la tabla (opcional)</param>
        /// <param name="nombreClave">Columna que identifica el registro a eliminar</param>
        /// <param name="valorClave">Valor de la clave para identificar el registro</param>
        /// <returns>Número de filas eliminadas (0 si no se encontró, >0 si se eliminó)</returns>
        Task<int> EliminarAsync(
            string nombreTabla,
            string? esquema,
            string nombreClave,
            string valorClave
        );

        /// <summary>
        /// Verifica las credenciales de un usuario comparando contraseña con hash BCrypt almacenado.
        /// </summary>
        /// <param name="nombreTabla">Nombre de la tabla que contiene usuarios</param>
        /// <param name="esquema">Esquema de la tabla (opcional)</param>
        /// <param name="campoUsuario">Columna que identifica al usuario</param>
        /// <param name="campoContrasena">Columna que contiene el hash de contraseña</param>
        /// <param name="valorUsuario">Valor del usuario a verificar</param>
        /// <param name="valorContrasena">Contraseña en texto plano a verificar</param>
        /// <returns>Tupla con código de resultado (200=éxito, 404=usuario no encontrado, 401=contraseña incorrecta) y mensaje</returns>
        Task<(int codigo, string mensaje)> VerificarContrasenaAsync(
            string nombreTabla,
            string? esquema,
            string campoUsuario,
            string campoContrasena,
            string valorUsuario,
            string valorContrasena
        );

        //aquí se pude agregar más métodos según se necesiten
    }
}

// NOTAS PEDAGÓGICAS para el tutorial:
//
// 1. DIFERENCIA FUNDAMENTAL ENTRE SERVICIO Y REPOSITORIO:
//    
//    REPOSITORIO (Capa Técnica - "Cómo"):
//    - "Dame todos los registros de esta tabla SQL"
//    - Preocupación: ¿La consulta es sintácticamente correcta?
//    - Validaciones: ¿La tabla existe? ¿La conexión funciona?
//    - Responsabilidad: Acceso puro a datos sin interpretación
//    - Errores típicos: SQLException, tabla no encontrada, timeout
//    
//    SERVICIO (Capa de Negocio - "Qué y Por qué"):
//    - "Dame registros que este usuario puede ver según las reglas del negocio"
//    - Preocupación: ¿Esta operación tiene sentido para el dominio?
//    - Validaciones: ¿Tiene permisos? ¿Cumple políticas? ¿Es seguro?
//    - Responsabilidad: Aplicar conocimiento del dominio empresarial
//    - Errores típicos: Acceso denegado, límite excedido, regla de negocio violada
//
// 2. ¿POR QUÉ SEPARAR EN CAPAS? (Separación de Responsabilidades):
//    
//    SIN CAPAS (Todo en un lugar - Problemático):
//    public class ControladorUsuarios 
//    {
//        public async Task<List<User>> GetUsers() 
//        {
//            //  Mezclando HTTP, negocio y datos en un solo lugar
//            if (string.IsNullOrEmpty(Request.Headers["Authorization"])) 
//                return BadRequest();  // ← HTTP
//            
//            if (currentUser.Role != "Admin") 
//                throw new UnauthorizedException();  // ← Negocio
//            
//            var sql = "SELECT * FROM users";
//            var connection = new SqlConnection("...");  // ← Datos
//            // ... más código mezclado
//        }
//    }
//    
//    CON CAPAS (Responsabilidades Separadas - Correcto):
//    - Controlador: "Recibí petición HTTP, la paso al servicio, convierto respuesta a HTTP"
//    - Servicio: "Aplico reglas de negocio, uso repositorio, proceso resultados"
//    - Repositorio: "Ejecuto consulta SQL, devuelvo datos crudos"
//
// 3. APLICACIÓN PRÁCTICA DE SOLID:
//    
//    SRP (Single Responsibility Principle):
//    - Controlador: Solo coordinación HTTP ↔ Servicio
//    - Servicio: Solo reglas de negocio y coordinación de repositorios  
//    - Repositorio: Solo acceso técnico a datos
//    
//    DIP (Dependency Inversion Principle):
//    - Controlador depende de IServicioCrud (abstracción), no de ServicioCrud
//    - Servicio depende de IRepositorioLecturaTabla (abstracción), no de RepositorioLecturaSqlServer
//    - Permite intercambiar implementaciones sin romper código cliente
//    
//    ISP (Interface Segregation Principle):
//    - Interface pequeña y enfocada: solo métodos de consulta por ahora
//    - No forzamos a implementar métodos que no necesitamos aún
//    - Se pueden agregar más interfaces específicas (IServicioEscritura, etc.)
//    
//    OCP (Open/Closed Principle):
//    - Abierta para extensión: se pueden agregar más métodos CRUD
//    - Cerrada para modificación: cambios nuevos no rompen código existente
//    - Se pueden crear nuevas implementaciones sin tocar las existentes
//
// 4. FLUJO TÍPICO DE UNA PETICIÓN HTTP COMPLETA:
//    
//    HTTP Request: GET /api/usuarios?limite=100
//         ↓
//    EntidadesController.ListarAsync("usuarios", null, 100)
//         ↓ (Llama a abstracción)
//    IServicioCrud.ListarAsync("usuarios", null, 100)  ← TÚ ESTÁS AQUÍ
//         ↓ (Implementación aplica reglas de negocio)
//    ServicioCrud.ListarAsync() → valida permisos, aplica límites, etc.
//         ↓ (Llama a abstracción)
//    IRepositorioLecturaTabla.ObtenerFilasAsync("usuarios", "dbo", 100)
//         ↓ (Implementación ejecuta SQL)
//    RepositorioLecturaSqlServer.ObtenerFilasAsync() → ejecuta "SELECT TOP 100 * FROM [dbo].[usuarios]"
//         ↓ (Datos crudos)
//    List<Dictionary<string, object?>> datos = [{"id": 1, "nombre": "Juan"}, ...]
//         ↓ (Servicio procesa)
//    ServicioCrud aplica filtros, transformaciones, auditoría
//         ↓ (Datos procesados)
//    Controlador convierte a JSON y devuelve HTTP 200 OK
//         ↓
//    HTTP Response: {"datos": [{"id": 1, "nombre": "Juan"}], "total": 1}
//
// 5. EJEMPLOS DE REGLAS DE NEGOCIO QUE ESTA INTERFACE PERMITIRÁ IMPLEMENTAR:
//    
//    Seguridad por roles:
//    - "Usuarios básicos: máximo 100 registros"
//    - "Administradores: sin límite pero con auditoría"
//    - "Tabla 'sueldos' solo para RRHH"
//    
//    Performance y UX:
//    - "Máximo 1000 registros para evitar que el navegador se cuelgue"
//    - "En horario pico, límites más restrictivos"
//    
//    Auditoría y compliance:
//    - "Registrar todas las consultas a datos personales"
//    - "Alertar si alguien consulta más de 10,000 registros en una hora"
//    
//    Transformaciones de negocio:
//    - "Ocultar campo 'password' siempre"
//    - "Convertir códigos internos a descripciones legibles"
//    - "Agregar campos calculados relevantes para el dominio"
//
// 6. ¿QUÉ VIENE DESPUÉS EN EL TUTORIAL?
//    - Crear ServicioCrud.cs que implemente esta interface con reglas de negocio reales
//    - El servicio usará IRepositorioLecturaTabla para obtener datos técnicos
//    - Registrar en Program.cs: AddScoped<IServicioCrud, ServicioCrud>
//    - Crear el controlador que use esta interface para manejar peticiones HTTP
//
// 7. EXTENSIBILIDAD FUTURA (Siguiendo ISP):
//    Conforme el tutorial crezca, se pueden agregar métodos siguiendo el mismo patrón:
//    Task<Dictionary<string, object?>> CrearAsync(string tabla, Dictionary<string, object?> datos);
//    Task<bool> ActualizarAsync(string tabla, object id, Dictionary<string, object?> datos);
//    Task<bool> EliminarAsync(string tabla, object id);
//    
//    O incluso interfaces más específicas:
//    interface IServicioConsultas : IServicioCrud { /* solo lectura */ }
//    interface IServicioModificaciones { /* solo escritura */ }
//
// 8. TESTING DE ESTA INTERFACE:
//    Esta interface facilita testing a múltiples niveles:
//    
//    Unit Testing (Servicios):
//    - Mockear IRepositorioLecturaTabla para probar reglas de negocio aisladamente
//    - Verificar que se apliquen correctamente límites, filtros, transformaciones
//    - Probar diferentes roles de usuario y sus permisos correspondientes
//    
//    Integration Testing (Controladores):
//    - Mockear IServicioCrud para probar solo la lógica de coordinación HTTP
//    - Verificar que se manejen correctamente los códigos de estado HTTP
//    - Probar serialización JSON y manejo de errores
//    
//    Ejemplo de test:
//    var mockRepo = new Mock<IRepositorioLecturaTabla>();
//    mockRepo.Setup(r => r.ObtenerFilasAsync("usuarios", "dbo", 100))
//           .ReturnsAsync(new List<Dictionary<string, object?>> { /* datos test */ });
//    
//    var servicio = new ServicioCrud(mockRepo.Object);
//    var resultado = await servicio.ListarAsync("usuarios", null, 1000);
//    
//    // Verificar que se aplicó el límite de negocio (no el técnico)
//    Assert.True(resultado.Count <= limite_definido_por_reglas_de_negocio);

