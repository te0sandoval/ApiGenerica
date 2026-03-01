// IPoliticaTablasProhibidas.cs — Interfaz para gestión de tablas permitidas/prohibidas
// Ubicación: Servicios/Abstracciones/IPoliticaTablasProhibidas.cs
//
// Principios SOLID aplicados:
// - SRP: Responsabilidad única = decidir si una tabla está permitida o prohibida
// - DIP: Los servicios dependen de esta abstracción, no de la configuración directa
// - OCP: Se puede extender con nuevas implementaciones (base de datos, archivos, API externa)
// - ISP: Interfaz pequeña y específica con un solo método

namespace ApiGenericaCsharp.Servicios.Abstracciones
{
    /// <summary>
    /// Interfaz que define el contrato para validar si una tabla está permitida o prohibida.
    ///
    /// PROPÓSITO:
    /// Esta interfaz permite que ServicioCrud no dependa directamente de IConfiguration.
    /// En lugar de leer la configuración directamente, delega esa responsabilidad a una
    /// implementación específica.
    ///
    /// BENEFICIOS:
    /// 1. ServicioCrud no necesita conocer de dónde vienen las reglas (JSON, BD, API)
    /// 2. Facilita testing: podemos crear un mock que siempre retorna true/false
    /// 3. Permite cambiar la fuente de configuración sin modificar ServicioCrud
    /// 4. Cumple con SRP: una clase, una responsabilidad (validar tablas)
    ///
    /// POSIBLES IMPLEMENTACIONES:
    /// - PoliticaTablasProhibidasDesdeJson: Lee de appsettings.json
    /// - PoliticaTablasProhibidasDesdeBaseDatos: Lee de tabla de configuración
    /// - PoliticaTablasProhibidasDesdeCache: Lee de Redis o similar
    /// </summary>
    public interface IPoliticaTablasProhibidas
    {
        /// <summary>
        /// Determina si una tabla específica está permitida para operaciones CRUD.
        ///
        /// CASO DE USO TÍPICO:
        /// Proteger tablas del sistema que no deben ser accesibles vía API genérica:
        /// - Tablas de usuarios y contraseñas
        /// - Tablas de configuración del sistema
        /// - Tablas de auditoría
        ///
        /// EJEMPLO:
        /// if (!_politicaTablas.EsTablaPermitida("usuarios"))
        /// {
        ///     throw new UnauthorizedAccessException("La tabla 'usuarios' está restringida.");
        /// }
        /// </summary>
        /// <param name="nombreTabla">
        /// Nombre de la tabla a validar.
        /// La validación debe ser case-insensitive para evitar bypasses.
        /// </param>
        /// <returns>
        /// - true: La tabla está permitida, se puede proceder con la operación
        /// - false: La tabla está prohibida, se debe bloquear la operación
        /// </returns>
        bool EsTablaPermitida(string nombreTabla);
    }
}

