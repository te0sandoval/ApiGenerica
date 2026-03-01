// ServicioCrud.cs — Implementación de la lógica de negocio que coordina operaciones CRUD
// Ubicación: Servicios/ServicioCrud.cs
//
// Principios SOLID aplicados:
// - SRP: Esta clase solo se encarga de lógica de negocio, delega el acceso a datos al repositorio
// - DIP: Depende de IRepositorioLecturaTabla (abstracción), no de implementaciones concretas
// - OCP: Se puede cambiar el repositorio sin modificar este servicio
// - LSP: Implementa completamente IServicioCrud, es intercambiable con otras implementaciones

using System;                                             // Para ArgumentException y ArgumentNullException
using System.Collections.Generic;                        // Para List<> y Dictionary<>
using System.Threading.Tasks;                            // Para async/await
using ApiGenericaCsharp.Servicios.Abstracciones;             // Para IServicioCrud e IPoliticaTablasProhibidas
using ApiGenericaCsharp.Repositorios.Abstracciones;          // Para IRepositorioLecturaTabla

namespace ApiGenericaCsharp.Servicios
{
    /// <summary>
    /// Implementación concreta del servicio CRUD que aplica reglas de negocio.
    ///
    /// Esta clase actúa como coordinadora entre la capa de presentación (Controllers)
    /// y la capa de acceso a datos (Repositorios). Sus responsabilidades incluyen:
    ///
    /// - Validar parámetros de entrada según reglas de negocio
    /// - Normalizar y limpiar datos antes de enviarlos al repositorio
    /// - Aplicar límites y políticas de seguridad
    /// - Transformar datos según necesidades del dominio
    /// - Manejar la coordinación entre múltiples repositorios (en el futuro)
    /// - Proporcionar una interfaz estable para los controladores
    ///
    /// La implementación actual es básica pero está preparada para crecer:
    /// - Validaciones básicas de entrada
    /// - Normalización de parámetros
    /// - Delegación directa al repositorio
    /// - Preparada para agregar transformaciones futuras
    /// </summary>
    public class ServicioCrud : IServicioCrud
    {
        // ====================================================================================
        // DEPENDENCIAS INYECTADAS (aplicando DIP - Dependency Inversion Principle)
        // ====================================================================================

        // Campo privado que mantiene la referencia al repositorio inyectado
        // Aplica DIP: depende de abstracción, no de implementación concreta
        private readonly IRepositorioLecturaTabla _repositorioLectura;

        // Campo privado que mantiene la referencia a la política de tablas prohibidas
        // MEJORA ARQUITECTÓNICA: Antes dependía de IConfiguration directamente (violaba DIP)
        // Ahora depende de IPoliticaTablasProhibidas (abstracción específica del dominio)
        // Beneficios:
        // - Separación de responsabilidades: ServicioCrud no conoce de dónde vienen las reglas
        // - Más testeable: fácil crear mocks de IPoliticaTablasProhibidas
        // - Más mantenible: cambios en cómo se leen las reglas no afectan ServicioCrud
        // - Cumple DIP: módulo de alto nivel no depende de módulo de bajo nivel (IConfiguration)
        private readonly IPoliticaTablasProhibidas _politicaTablasProhibidas;

        /// <summary>
        /// Constructor que recibe las dependencias mediante inyección de dependencias (DI).
        ///
        /// MEJORA ARQUITECTÓNICA - COMPARACIÓN ANTES vs DESPUÉS:
        ///
        /// ANTES (VIOLABA DIP):
        /// public ServicioCrud(IRepositorioLecturaTabla repo, IConfiguration config)
        /// Problema: ServicioCrud dependía de IConfiguration (infraestructura de bajo nivel)
        /// - Mezclaba lógica de negocio con lectura de configuración
        /// - Difícil de testear (necesitas configurar IConfiguration mock)
        /// - Poco expresivo (¿para qué necesita IConfiguration? no es claro)
        ///
        /// DESPUÉS (CUMPLE DIP):
        /// public ServicioCrud(IRepositorioLecturaTabla repo, IPoliticaTablasProhibidas politica)
        /// Beneficio: ServicioCrud depende de abstracción del dominio específica
        /// - Separación clara de responsabilidades
        /// - Fácil de testear (mock simple con una línea)
        /// - Expresivo (está claro que valida tablas prohibidas)
        /// - Cumple DIP: no depende de detalles de implementación
        ///
        /// FLUJO COMPLETO DE INYECCIÓN DE DEPENDENCIAS:
        /// 1. Program.cs registra: AddScoped<IServicioCrud, ServicioCrud>
        /// 2. Program.cs registra: AddScoped<IRepositorioLecturaTabla, RepositorioLecturaSqlServer>
        /// 3. Program.cs registra: AddSingleton<IPoliticaTablasProhibidas, PoliticaTablasProhibidasDesdeJson>
        /// 4. Cuando se solicita IServicioCrud (por ejemplo, desde EntidadesController):
        ///    - El contenedor DI crea ServicioCrud
        ///    - Ve que necesita IRepositorioLecturaTabla → inyecta RepositorioLecturaSqlServer
        ///    - Ve que necesita IPoliticaTablasProhibidas → inyecta PoliticaTablasProhibidasDesdeJson
        ///    - RepositorioLecturaSqlServer necesita IProveedorConexion → se inyecta automáticamente
        ///    - PoliticaTablasProhibidasDesdeJson necesita IConfiguration → se inyecta automáticamente
        ///    - Toda la cadena de dependencias se resuelve automáticamente
        ///    - ServicioCrud queda completamente inicializado y listo para usar
        ///
        /// VENTAJAS DE ESTA ARQUITECTURA:
        /// - ServicioCrud NO conoce IConfiguration (separación de capas)
        /// - ServicioCrud NO sabe de dónde vienen las reglas de tablas prohibidas
        /// - Fácil cambiar la implementación (JSON → BD → API) sin tocar ServicioCrud
        /// - Testing simplificado con mocks
        /// - Código más expresivo y mantenible
        /// </summary>
        /// <param name="repositorioLectura">
        /// Repositorio que implementa las operaciones de acceso a datos.
        /// Se inyecta automáticamente según la configuración en Program.cs.
        ///
        /// El servicio no necesita saber:
        /// - Qué proveedor de base de datos se usa (SQL Server, PostgreSQL, MariaDB, etc.)
        /// - Cómo se obtienen las cadenas de conexión
        /// - Qué sintaxis SQL específica se ejecuta
        /// - Cómo se mapean los tipos de datos
        ///
        /// Solo sabe que implementa IRepositorioLecturaTabla y puede pedirle que:
        /// - Lea filas de una tabla
        /// - Filtre por clave
        /// - Inserte, actualice o elimine registros
        /// </param>
        /// <param name="politicaTablasProhibidas">
        /// Política que determina qué tablas están permitidas o prohibidas.
        /// Se inyecta automáticamente según la configuración en Program.cs.
        ///
        /// NUEVA DEPENDENCIA - MEJORA ARQUITECTÓNICA:
        /// Esta es la abstracción que reemplaza la dependencia directa de IConfiguration.
        ///
        /// El servicio no necesita saber:
        /// - De dónde viene la lista de tablas prohibidas (JSON, BD, API, hardcoded, etc.)
        /// - Cómo se almacenan las reglas
        /// - Si usa lista negra (blacklist) o lista blanca (whitelist)
        ///
        /// Solo sabe que puede preguntar: ¿Esta tabla está permitida?
        /// Y recibe una respuesta clara: true o false
        ///
        /// Esto cumple perfectamente con:
        /// - SRP: Cada clase tiene una responsabilidad
        /// - DIP: Dependemos de abstracciones, no de implementaciones
        /// - OCP: Abierto a extensión, cerrado a modificación
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Se lanza si alguna dependencia es null, indicando problema en la configuración
        /// de inyección de dependencias en Program.cs.
        ///
        /// Este tipo de excepción solo debería ocurrir:
        /// - Durante desarrollo si olvidaste registrar un servicio
        /// - Durante configuración si hay un error en Program.cs
        ///
        /// En producción con configuración correcta, nunca debería lanzarse.
        /// </exception>
        public ServicioCrud(
            IRepositorioLecturaTabla repositorioLectura,
            IPoliticaTablasProhibidas politicaTablasProhibidas)
        {
            // Validación defensiva: asegurar que las dependencias fueron inyectadas correctamente
            // Si alguna es null, hay un problema en Program.cs que debe corregirse

            _repositorioLectura = repositorioLectura ?? throw new ArgumentNullException(
                nameof(repositorioLectura),
                "IRepositorioLecturaTabla no puede ser null. " +
                "Verificar que esté registrado en Program.cs con AddScoped<IRepositorioLecturaTabla, ...>()"
            );

            _politicaTablasProhibidas = politicaTablasProhibidas ?? throw new ArgumentNullException(
                nameof(politicaTablasProhibidas),
                "IPoliticaTablasProhibidas no puede ser null. " +
                "Verificar que esté registrado en Program.cs con AddSingleton<IPoliticaTablasProhibidas, PoliticaTablasProhibidasDesdeJson>()"
            );

            // NOTA PARA ESTUDIANTES:
            // Observa cómo el constructor es limpio y expresivo:
            // - Solo recibe abstracciones (interfaces), no implementaciones concretas
            // - Los nombres de los parámetros expresan claramente qué hace cada dependencia
            // - Las validaciones proporcionan mensajes de error útiles
            // - No hay lógica de negocio aquí, solo asignación de dependencias
        }

        /// <summary>
        /// Implementa IServicioCrud.ListarAsync aplicando reglas de negocio.
        ///
        /// Proceso de la lógica de negocio:
        /// 1. VALIDACIÓN: Verificar que los parámetros cumplan reglas de negocio
        /// 2. NORMALIZACIÓN: Limpiar y estandarizar parámetros de entrada
        /// 3. DELEGACIÓN: Llamar al repositorio con parámetros validados
        /// 4. TRANSFORMACIÓN: Procesar datos devueltos según reglas de dominio
        /// 5. RETORNO: Devolver datos listos para la capa de presentación
        ///
        /// La implementación actual es básica pero extensible:
        /// - Validaciones fundamentales de entrada
        /// - Normalización de parámetros opcionales
        /// - Delegación directa al repositorio
        /// - Preparada para agregar reglas de negocio complejas
        ///
        /// Reglas de negocio que se podrían agregar en el futuro:
        /// - Validación de permisos por tabla/esquema
        /// - Aplicación de límites por tipo de usuario
        /// - Filtrado de columnas sensibles
        /// - Auditoría de accesos
        /// - Transformación de datos según contexto
        /// - Cache de resultados frecuentes
        /// </summary>
        /// <param name="nombreTabla">
        /// Nombre de la tabla a consultar. Se valida que no esté vacío.
        /// En el futuro se podría validar contra una lista de tablas permitidas.
        /// </param>
        /// <param name="esquema">
        /// Esquema de la tabla. Se normaliza: null/vacío se convierte a null,
        /// valores válidos se limpian con Trim().
        /// En el futuro se podría mapear esquemas virtuales a físicos.
        /// </param>
        /// <param name="limite">
        /// Límite de registros. Se normaliza: null/cero/negativo se convierte a null.
        /// En el futuro se podrían aplicar límites máximos por tipo de usuario.
        /// </param>
        /// <returns>
        /// Lista inmutable de diccionarios con los datos obtenidos.
        /// Los datos vienen directamente del repositorio, pero en el futuro
        /// se podrían aplicar transformaciones de negocio antes del retorno.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Se lanza cuando nombreTabla es null, vacío o solo espacios en blanco.
        /// Esta es una validación de regla de negocio fundamental.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Se propagan las excepciones del repositorio, que pueden incluir:
        /// - Tabla no encontrada
        /// - Problemas de conexión
        /// - Errores de permisos
        /// El servicio actúa como intermediario transparente para estos errores.
        /// </exception>
        public async Task<IReadOnlyList<Dictionary<string, object?>>> ListarAsync(
            string nombreTabla,
            string? esquema,
            int? limite
        )
        {
            // ====================================================================================
            // FASE 1: VALIDACIONES DE REGLAS DE NEGOCIO
            // ====================================================================================

            // VALIDACIÓN 1: Nombre de tabla no puede estar vacío
            // Esta es una validación básica de entrada que previene errores en capas inferiores
            if (string.IsNullOrWhiteSpace(nombreTabla))
                throw new ArgumentException(
                    "El nombre de la tabla no puede estar vacío.",
                    nameof(nombreTabla)
                );

            // VALIDACIÓN 2: Verificar que la tabla esté permitida según política de seguridad
            // MEJORA ARQUITECTÓNICA - COMPARACIÓN ANTES vs DESPUÉS:
            //
            // ANTES (acoplado a IConfiguration):
            // var tablasProhibidas = _configuration.GetSection("TablasProhibidas").Get<string[]>();
            // if (tablasProhibidas.Contains(nombreTabla, StringComparer.OrdinalIgnoreCase))
            //     throw new UnauthorizedAccessException(...);
            //
            // Problemas:
            // - ServicioCrud conocía detalles de implementación (JSON, sección "TablasProhibidas")
            // - Difícil de testear (necesitas configurar IConfiguration completo)
            // - Violaba DIP (dependía de infraestructura)
            // - Poco expresivo (mucha lógica para algo simple)
            //
            // DESPUÉS (desacoplado usando abstracción):
            if (!_politicaTablasProhibidas.EsTablaPermitida(nombreTabla))
            {
                throw new UnauthorizedAccessException(
                    $"Acceso denegado: La tabla '{nombreTabla}' está restringida y no puede ser consultada. " +
                    $"Verifique los permisos de acceso o contacte al administrador del sistema."
                );
            }
            //
            // Ventajas:
            // - Una sola línea clara y expresiva
            // - ServicioCrud no sabe de dónde vienen las reglas
            // - Fácil de testear con mock simple
            // - Cumple DIP perfectamente
            // - Si cambia cómo se leen las reglas, ServicioCrud NO cambia
            //
            // NOTA PEDAGÓGICA PARA ESTUDIANTES:
            // Este es un ejemplo perfecto de refactoring que mejora el diseño:
            // - El código hace exactamente lo mismo funcionalmente
            // - Pero la arquitectura es mucho mejor
            // - Más fácil de entender, testear y mantener
            // - Demuestra los beneficios reales de aplicar SOLID

            // VALIDACIONES FUTURAS QUE SE PODRÍAN AGREGAR:
            // - Verificar permisos del usuario autenticado para acceder a esa tabla específica
            // - Validar caracteres permitidos en el nombre de tabla (prevenir SQL injection)
            // - Aplicar reglas de negocio específicas por tipo de tabla
            // - Auditar accesos a tablas sensibles
            // - Aplicar límites de consulta según perfil del usuario

            // FASE 2: NORMALIZACIÓN DE PARÁMETROS
            // Limpiar y estandarizar parámetros opcionales según reglas de negocio

            // Normalizar esquema: null/vacío se mantiene como null para que el repositorio aplique su defecto
            string? esquemaNormalizado = string.IsNullOrWhiteSpace(esquema) ? null : esquema.Trim();

            // Normalizar límite: valores inválidos se convierten a null para aplicar defecto del repositorio
            int? limiteNormalizado = (limite is null || limite <= 0) ? null : limite;

            // APLICACIONES FUTURAS DE NORMALIZACIÓN:
            // - Aplicar límite máximo según tipo de usuario (ej: usuarios normales máximo 1000)
            // - Mapear esquemas virtuales a esquemas físicos
            // - Aplicar configuraciones específicas por entorno (desarrollo vs producción)

            // FASE 3: DELEGACIÓN AL REPOSITORIO (DIP EN ACCIÓN)
            // El servicio no sabe cómo se accede a los datos, solo delega al repositorio
            // Esta delegación es la aplicación práctica del principio DIP:
            // - El servicio depende de IRepositorioLecturaTabla (abstracción)
            // - No sabe si es SQL Server, PostgreSQL, MariaDB, etc.
            // - No sabe cómo se obtienen las cadenas de conexión
            // - No sabe qué sintaxis SQL específica se usa
            var filas = await _repositorioLectura.ObtenerFilasAsync(nombreTabla, esquemaNormalizado, limiteNormalizado);

            // FASE 4: TRANSFORMACIONES POST-REPOSITORIO (LÓGICA DE NEGOCIO)
            // En la implementación actual, devolvemos los datos tal como vienen del repositorio
            // Pero aquí es donde se aplicarían transformaciones de negocio en el futuro:

            // TRANSFORMACIONES FUTURAS QUE SE PODRÍAN AGREGAR:
            // - Filtrar columnas sensibles según perfil del usuario
            // - Agregar campos calculados específicos del dominio
            // - Aplicar formatos de datos según reglas de presentación
            // - Enriquecer datos con información de otras fuentes
            // - Aplicar reglas de privacy/GDPR
            // - Convertir tipos de datos según necesidades del dominio

            // Ejemplo de transformación futura:
            // if (nombreTabla.ToLower() == "usuarios")
            // {
            //     // Filtrar campo password y datos sensibles
            //     filas = filas.Select(fila => fila.Where(kvp => kvp.Key != "password").ToDictionary()).ToList();
            // }

            // FASE 5: RETORNO DE RESULTADOS
            // Devolver datos procesados y listos para la capa de presentación
            return filas;
        }
        /// <summary>
        /// Implementa la consulta filtrada aplicando reglas de negocio y validaciones.
        ///
        /// Proceso: Validación → Normalización → Delegación al repositorio → Transformación
        /// Similar a ListarAsync pero con validaciones adicionales para filtrado por clave.
        /// </summary>
        public async Task<IReadOnlyList<Dictionary<string, object?>>> ObtenerPorClaveAsync(
            string nombreTabla,
            string? esquema,
            string nombreClave,
            string valor
        )
        {
            // FASE 1: VALIDACIONES DE REGLAS DE NEGOCIO
            if (string.IsNullOrWhiteSpace(nombreTabla))
                throw new ArgumentException("El nombre de la tabla no puede estar vacío.", nameof(nombreTabla));

            if (string.IsNullOrWhiteSpace(nombreClave))
                throw new ArgumentException("El nombre de la clave no puede estar vacío.", nameof(nombreClave));

            if (string.IsNullOrWhiteSpace(valor))
                throw new ArgumentException("El valor no puede estar vacío.", nameof(valor));

            // VALIDACIÓN DE TABLAS PROHIBIDAS (usando la política inyectada)
            if (!_politicaTablasProhibidas.EsTablaPermitida(nombreTabla))
                throw new UnauthorizedAccessException(
                    $"Acceso denegado: La tabla '{nombreTabla}' está restringida y no puede ser consultada."
                );

            // FASE 2: NORMALIZACIÓN DE PARÁMETROS
            string? esquemaNormalizado = string.IsNullOrWhiteSpace(esquema) ? null : esquema.Trim();
            string nombreClaveNormalizado = nombreClave.Trim();
            string valorNormalizado = valor.Trim();

            // FASE 3: DELEGACIÓN AL REPOSITORIO (aplicando DIP)
            var filas = await _repositorioLectura.ObtenerPorClaveAsync(
                nombreTabla,
                esquemaNormalizado,
                nombreClaveNormalizado,
                valorNormalizado
            );

            // FASE 4: TRANSFORMACIONES POST-CONSULTA (futuras)
            // Aquí se pueden aplicar filtros adicionales, cálculos, etc.

            return filas;
        }

        /// <summary>
        /// Implementa la creación de registro aplicando reglas de negocio y validaciones.
        ///
        /// Proceso: Validación → Normalización → Delegación al repositorio
        /// Aplica las mismas validaciones de seguridad que los métodos de consulta.
        /// </summary>
        public async Task<bool> CrearAsync(
            string nombreTabla,
            string? esquema,
            Dictionary<string, object?> datos,
            string? camposEncriptar = null
        )
        {
            // FASE 1: VALIDACIONES DE REGLAS DE NEGOCIO
            if (string.IsNullOrWhiteSpace(nombreTabla))
                throw new ArgumentException("El nombre de la tabla no puede estar vacío.", nameof(nombreTabla));

            if (datos == null || !datos.Any())
                throw new ArgumentException("Los datos no pueden estar vacíos.", nameof(datos));

            // VALIDACIÓN DE TABLAS PROHIBIDAS (usando la política inyectada)
            if (!_politicaTablasProhibidas.EsTablaPermitida(nombreTabla))
                throw new UnauthorizedAccessException(
                    $"Acceso denegado: La tabla '{nombreTabla}' está restringida y no puede ser modificada."
                );

            // FASE 2: NORMALIZACIÓN DE PARÁMETROS
            string? esquemaNormalizado = string.IsNullOrWhiteSpace(esquema) ? null : esquema.Trim();
            string? camposEncriptarNormalizados = string.IsNullOrWhiteSpace(camposEncriptar) ? null : camposEncriptar.Trim();

            // VALIDACIONES ADICIONALES DE NEGOCIO (futuras expansiones)
            // Aquí se pueden agregar:
            // - Validación de tipos de datos
            // - Reglas de negocio específicas por tabla
            // - Límites de tamaño de campos
            // - Validaciones de integridad referencial

            // FASE 3: DELEGACIÓN AL REPOSITORIO (aplicando DIP)
            return await _repositorioLectura.CrearAsync(
                nombreTabla,
                esquemaNormalizado,
                datos,
                camposEncriptarNormalizados
            );
        }

        /// <summary>
        /// Implementa la actualización de registro aplicando reglas de negocio y validaciones.
        ///
        /// Proceso: Validación → Normalización → Delegación al repositorio
        /// Aplica las mismas validaciones de seguridad que los métodos de consulta y creación.
        /// </summary>
        public async Task<int> ActualizarAsync(
            string nombreTabla,
            string? esquema,
            string nombreClave,
            string valorClave,
            Dictionary<string, object?> datos,
            string? camposEncriptar = null
        )
        {
            // FASE 1: VALIDACIONES DE REGLAS DE NEGOCIO
            if (string.IsNullOrWhiteSpace(nombreTabla))
                throw new ArgumentException("El nombre de la tabla no puede estar vacío.", nameof(nombreTabla));

            if (string.IsNullOrWhiteSpace(nombreClave))
                throw new ArgumentException("El nombre de la clave no puede estar vacío.", nameof(nombreClave));

            if (string.IsNullOrWhiteSpace(valorClave))
                throw new ArgumentException("El valor de la clave no puede estar vacío.", nameof(valorClave));

            if (datos == null || !datos.Any())
                throw new ArgumentException("Los datos a actualizar no pueden estar vacíos.", nameof(datos));

            // VALIDACIÓN DE TABLAS PROHIBIDAS (usando la política inyectada)
            if (!_politicaTablasProhibidas.EsTablaPermitida(nombreTabla))
                throw new UnauthorizedAccessException(
                    $"Acceso denegado: La tabla '{nombreTabla}' está restringida y no puede ser modificada."
                );

            // FASE 2: NORMALIZACIÓN DE PARÁMETROS
            string? esquemaNormalizado = string.IsNullOrWhiteSpace(esquema) ? null : esquema.Trim();
            string nombreClaveNormalizado = nombreClave.Trim();
            string valorClaveNormalizado = valorClave.Trim();
            string? camposEncriptarNormalizados = string.IsNullOrWhiteSpace(camposEncriptar) ? null : camposEncriptar.Trim();

            // VALIDACIONES ADICIONALES DE NEGOCIO PARA ACTUALIZACIÓN (futuras expansiones)
            // Aquí se pueden agregar:
            // - Validación de permisos específicos para actualización
            // - Reglas de negocio sobre qué campos se pueden actualizar
            // - Validaciones de integridad de datos
            // - Auditoría de cambios sensibles

            // FASE 3: DELEGACIÓN AL REPOSITORIO (aplicando DIP)
            return await _repositorioLectura.ActualizarAsync(
                nombreTabla,
                esquemaNormalizado,
                nombreClaveNormalizado,
                valorClaveNormalizado,
                datos,
                camposEncriptarNormalizados
            );
        }

        /// <summary>
        /// Implementa la eliminación de registro aplicando reglas de negocio y validaciones.
        ///
        /// Proceso: Validación → Normalización → Delegación al repositorio
        /// Aplica las mismas validaciones de seguridad que los otros métodos CRUD.
        /// </summary>
        public async Task<int> EliminarAsync(
            string nombreTabla,
            string? esquema,
            string nombreClave,
            string valorClave
        )
        {
            // FASE 1: VALIDACIONES DE REGLAS DE NEGOCIO
            if (string.IsNullOrWhiteSpace(nombreTabla))
                throw new ArgumentException("El nombre de la tabla no puede estar vacío.", nameof(nombreTabla));

            if (string.IsNullOrWhiteSpace(nombreClave))
                throw new ArgumentException("El nombre de la clave no puede estar vacío.", nameof(nombreClave));

            if (string.IsNullOrWhiteSpace(valorClave))
                throw new ArgumentException("El valor de la clave no puede estar vacío.", nameof(valorClave));

            // VALIDACIÓN DE TABLAS PROHIBIDAS (usando la política inyectada)
            if (!_politicaTablasProhibidas.EsTablaPermitida(nombreTabla))
                throw new UnauthorizedAccessException(
                    $"Acceso denegado: La tabla '{nombreTabla}' está restringida y no puede ser modificada."
                );

            // FASE 2: NORMALIZACIÓN DE PARÁMETROS
            string? esquemaNormalizado = string.IsNullOrWhiteSpace(esquema) ? null : esquema.Trim();
            string nombreClaveNormalizado = nombreClave.Trim();
            string valorClaveNormalizado = valorClave.Trim();

            // VALIDACIONES ADICIONALES DE NEGOCIO PARA ELIMINACIÓN (futuras expansiones)
            // Aquí se pueden agregar:
            // - Validación de permisos específicos para eliminación (más restrictivos)
            // - Reglas de negocio sobre eliminación en cascada
            // - Validaciones de integridad referencial antes de eliminar
            // - Auditoría de eliminaciones sensibles
            // - Soft delete vs hard delete según reglas del negocio

            // FASE 3: DELEGACIÓN AL REPOSITORIO (aplicando DIP)
            return await _repositorioLectura.EliminarAsync(
                nombreTabla,
                esquemaNormalizado,
                nombreClaveNormalizado,
                valorClaveNormalizado
            );
        }
        /// <summary>
        /// Implementa la verificación de credenciales aplicando reglas de negocio y seguridad.
        ///
        /// Proceso: Validación → Obtener hash almacenado → Verificar con BCrypt → Evaluar resultado
        /// Utiliza BCrypt para comparación segura de contraseñas hasheadas.
        /// </summary>
        public async Task<(int codigo, string mensaje)> VerificarContrasenaAsync(
            string nombreTabla,
            string? esquema,
            string campoUsuario,
            string campoContrasena,
            string valorUsuario,
            string valorContrasena
        )
        {
            // FASE 1: VALIDACIONES DE REGLAS DE NEGOCIO
            if (string.IsNullOrWhiteSpace(nombreTabla))
                throw new ArgumentException("El nombre de la tabla no puede estar vacío.", nameof(nombreTabla));

            if (string.IsNullOrWhiteSpace(campoUsuario))
                throw new ArgumentException("El campo de usuario no puede estar vacío.", nameof(campoUsuario));

            if (string.IsNullOrWhiteSpace(campoContrasena))
                throw new ArgumentException("El campo de contraseña no puede estar vacío.", nameof(campoContrasena));

            if (string.IsNullOrWhiteSpace(valorUsuario))
                throw new ArgumentException("El valor de usuario no puede estar vacío.", nameof(valorUsuario));

            if (string.IsNullOrWhiteSpace(valorContrasena))
                throw new ArgumentException("La contraseña no puede estar vacía.", nameof(valorContrasena));

            // VALIDACIÓN DE TABLAS PROHIBIDAS (usando la política inyectada)
            if (!_politicaTablasProhibidas.EsTablaPermitida(nombreTabla))
                throw new UnauthorizedAccessException(
                    $"Acceso denegado: La tabla '{nombreTabla}' está restringida y no puede ser consultada."
                );

            // FASE 2: NORMALIZACIÓN DE PARÁMETROS
            string? esquemaNormalizado = string.IsNullOrWhiteSpace(esquema) ? null : esquema.Trim();
            string campoUsuarioNormalizado = campoUsuario.Trim();
            string campoContrasenaNormalizado = campoContrasena.Trim();
            string valorUsuarioNormalizado = valorUsuario.Trim();

            try
            {
                // FASE 3: OBTENER HASH ALMACENADO DEL REPOSITORIO
                string? hashAlmacenado = await _repositorioLectura.ObtenerHashContrasenaAsync(
                    nombreTabla,
                    esquemaNormalizado,
                    campoUsuarioNormalizado,
                    campoContrasenaNormalizado,
                    valorUsuarioNormalizado
                );

                // FASE 4: EVALUAR RESULTADO DE LA BÚSQUEDA
                if (hashAlmacenado == null)
                {
                    // Usuario no encontrado en la base de datos
                    return (404, "Usuario no encontrado");
                }

                // FASE 5: VERIFICACIÓN DE CONTRASEÑA CON BCRYPT
                // Usar nuestra clase de utilidad para verificación segura
                bool contrasenaCorrecta = ApiGenericaCsharp.Servicios.Utilidades.EncriptacionBCrypt.Verificar(
                    valorContrasena,  // Contraseña en texto plano proporcionada
                    hashAlmacenado    // Hash BCrypt almacenado en base de datos
                );

                if (contrasenaCorrecta)
                {
                    return (200, "Credenciales válidas");
                }
                else
                {
                    return (401, "Contraseña incorrecta");
                }
            }
            catch (Exception excepcion)
            {
                // Re-lanzar excepción para que el controlador la maneje apropiadamente
                throw new InvalidOperationException(
                    $"Error durante la verificación de credenciales: {excepcion.Message}",
                    excepcion
                );
            }
        }
//aquí se puede agregar más métodos siguiendo el mismo patrón
    }
}

// NOTAS PEDAGÓGICAS para el tutorial:
//
// 1. ESTA ES LA CAPA DE LÓGICA DE NEGOCIO:
//    - No accede directamente a datos (eso es responsabilidad del repositorio)
//    - No maneja HTTP (eso es responsabilidad del controlador)
//    - Se enfoca únicamente en reglas de negocio, validaciones y coordinación
//
// 2. APLICACIÓN PRÁCTICA DE DIP:
//    - Depende de IRepositorioLecturaTabla, no de RepositorioLecturaSqlServer
//    - Puede funcionar con cualquier implementación del repositorio
//    - El cambio de proveedor de BD no afecta este código
//    - Facilita testing con repositorios mock
//
// 3. SEPARACIÓN DE RESPONSABILIDADES:
//    - Controlador: maneja HTTP y coordina con servicios
//    - Servicio: aplica reglas de negocio y coordina con repositorios
//    - Repositorio: accede a datos sin lógica de negocio
//    - Cada capa tiene su propósito específico
//
// 4. EXTENSIBILIDAD:
//    - La estructura permite agregar reglas de negocio sin afectar otras capas
//    - Se pueden agregar validaciones, transformaciones, auditoría
//    - Preparado para crecer en complejidad sin romper el diseño
//
// 5. FLUJO COMPLETO DE UNA PETICIÓN:
//    EntidadesController.ListarAsync()
//    ↓ (HTTP → Business Logic)
//    ServicioCrud.ListarAsync()
//    ↓ (Business Logic → Data Access)
//    RepositorioLecturaSqlServer.ObtenerFilasAsync()
//    ↓ (Data Access → Database)
//    SQL Server Database
//
// 6. PRÓXIMO PASO EN PROGRAM.CS:
//    Descomentar la línea:
//    builder.Services.AddScoped<IServicioCrud, ServicioCrud>();
//
// 7. TESTING:
//    Esta clase es fácil de testear porque:
//    - Recibe IRepositorioLecturaTabla que se puede mockear
//    - Las validaciones son independientes del acceso a datos
//    - Se pueden probar las reglas de negocio aisladamente
//
// 8. EVOLUCIÓN FUTURA:
//    Se pueden agregar más métodos siguiendo el mismo patrón:
//    - CrearAsync: validar datos, delegar al repositorio, auditar
//    - ActualizarAsync: validar permisos, normalizar, delegar
//    - EliminarAsync: validar reglas de negocio, delegar, auditar

