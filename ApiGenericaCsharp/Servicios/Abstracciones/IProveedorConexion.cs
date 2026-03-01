// IProveedorConexion.cs — Interface que define el contrato para obtener conexiones a base de datos
// Ubicación: Servicios/Abstracciones/IProveedorConexion.cs
//
// Principios SOLID aplicados:
// - SRP: Esta interface solo se encarga de definir operaciones relacionadas con conexiones
// - DIP: Permite que otras clases dependan de esta abstracción, no de implementaciones concretas
// - ISP: Interface específica y pequeña, solo métodos relacionados con conexiones
// - OCP: Abierta para extensión (nuevas implementaciones) pero cerrada para modificación

using System;

namespace ApiGenericaCsharp.Servicios.Abstracciones
{
    /// <summary>
    /// Contrato que define cómo obtener información de conexión a base de datos.
    /// 
    /// Esta interface es el "qué" (contrato), no el "cómo" (implementación).
    /// Permite que cualquier clase que necesite conexiones dependa de esta abstracción
    /// en lugar de depender de una clase concreta específica.
    /// 
    /// Beneficios:
    /// - Facilita testing (se pueden crear mocks de esta interface)
    /// - Permite intercambiar implementaciones sin cambiar código cliente
    /// - Desacopla la lógica de obtener conexiones de quienes las usan
    /// </summary>
    public interface IProveedorConexion
    {
        /// <summary>
        /// Obtiene el nombre del proveedor de base de datos actualmente configurado.
        /// 
        /// Valores esperados:
        /// - "SqlServer" para Microsoft SQL Server
        /// - "Postgres" para PostgreSQL
        /// - "MariaDB" para MariaDB
        /// - "MySQL" para MySQL
        /// </summary>
        string ProveedorActual { get; }

        /// <summary>
        /// Obtiene la cadena de conexión correspondiente al proveedor configurado.
        /// </summary>
        /// <returns>Cadena de conexión lista para usar</returns>
        /// <exception cref="InvalidOperationException">
        /// Se lanza cuando no existe configuración para el proveedor actual
        /// </exception>
        string ObtenerCadenaConexion();
    }
}

