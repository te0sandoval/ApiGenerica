// ProveedorConexion.cs — Implementación que lee configuración desde appsettings.json
// Ubicación: Servicios/Conexion/ProveedorConexion.cs
//
// Principios SOLID aplicados:
// - SRP: Esta clase solo sabe "leer configuración y entregar una cadena de conexión"
// - DIP: Implementa IProveedorConexion, permitiendo que otras clases dependan de la abstracción
// - OCP: Si mañana se agrega MySQL/Oracle, se extiende sin tocar el resto del sistema

using Microsoft.Extensions.Configuration;     // Permite leer appsettings.*.json
using System;                                  // Para InvalidOperationException
using ApiGenericaCsharp.Servicios.Abstracciones;   // Para IProveedorConexion

namespace ApiGenericaCsharp.Servicios.Conexion
{
   /// <summary>
   /// Implementación concreta que lee "DatabaseProvider" y "ConnectionStrings" desde IConfiguration.
   /// 
   /// Esta clase encapsula toda la lógica específica de cómo obtener configuraciones:
   /// - Lee desde archivos appsettings.json
   /// - Maneja valores por defecto
   /// - Proporciona mensajes de error claros
   /// - Valida que las configuraciones existan
   /// 
   /// Responsabilidad única: conectar la configuración JSON con los repositorios que necesitan
   /// cadenas de conexión, sin que estos sepan de dónde vienen.
   /// </summary>
   public class ProveedorConexion : IProveedorConexion
   {
       private readonly IConfiguration _configuracion;

       /// <summary>
       /// Constructor que recibe la configuración por inyección de dependencias.
       /// 
       /// El patrón de inyección de dependencias funciona así:
       /// 1. Program.cs registra: AddSingleton&lt;IProveedorConexion, ProveedorConexion&gt;()
       /// 2. Cuando alguien solicita IProveedorConexion, el contenedor crea ProveedorConexion
       /// 3. El contenedor ve que ProveedorConexion necesita IConfiguration
       /// 4. El contenedor inyecta automáticamente IConfiguration en este constructor
       /// </summary>
       public ProveedorConexion(IConfiguration configuracion)
       {
           _configuracion = configuracion ?? throw new ArgumentNullException(
               nameof(configuracion), 
               "IConfiguration no puede ser null. Verificar configuración en Program.cs."
           );
       }

       /// <summary>
       /// Lee el valor de "DatabaseProvider" desde appsettings.json. 
       /// Si no existe o está vacío, por defecto usa "SqlServer".
       /// </summary>
       public string ProveedorActual
       {
           get
           {
               var valor = _configuracion.GetValue<string>("DatabaseProvider");
               return string.IsNullOrWhiteSpace(valor) ? "SqlServer" : valor.Trim();
           }
       }

       /// <summary>
       /// Entrega la cadena de conexión correspondiente al proveedor actual.
       /// </summary>
       public string ObtenerCadenaConexion()
       {
           string? cadena = _configuracion.GetConnectionString(ProveedorActual);

           if (string.IsNullOrWhiteSpace(cadena))
           {
               throw new InvalidOperationException(
                   $"No se encontró la cadena de conexión para el proveedor '{ProveedorActual}'. " +
                   $"Verificar que existe 'ConnectionStrings:{ProveedorActual}' en appsettings.json " +
                   $"y que 'DatabaseProvider' esté configurado correctamente."
               );
           }

           return cadena;
       }
   }
}

