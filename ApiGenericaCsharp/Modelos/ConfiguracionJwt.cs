// ConfiguracionJwt.cs
// Clase de configuración que representa los parámetros necesarios para emitir tokens JWT.
//
// Se asocia directamente con la sección "Jwt" dentro del archivo appsettings.json.
//
// ---------------------------------------------------------
// PRINCIPIOS:
// ---------------------------------------------------------
// - SRP: Se encarga solo de representar datos de configuración JWT.
// - OCP: Puede ampliarse si se agregan más parámetros, sin afectar el resto del sistema.
// - DIP: Es inyectada en los controladores a través de IOptions<ConfiguracionJwt>.
//
// Ejemplo de sección en appsettings.json:
//
// "Jwt": {
//   "Key": "ClaveSuperSecreta123456",
//   "Issuer": "ApiGenericaCsharp",
//   "Audience": "clientes",
//   "DuracionMinutos": 60
// }

namespace ApiGenericaCsharp.Modelos  
{
    /// <summary>
    /// Representa los valores de configuración JWT.
    /// Estos valores son cargados desde appsettings.json y usados para firmar los tokens.
    /// </summary>
    public class ConfiguracionJwt
    {
        // Clave secreta usada para firmar los tokens JWT.
        // Debe ser larga y segura. Nunca se comparte públicamente.
        public string Key { get; set; } = string.Empty;

        // Emisor del token (Issuer). Normalmente el nombre o dominio de la API.
        public string Issuer { get; set; } = string.Empty;

        // Audiencia del token (Audience). Indica quién puede usar este token.
        public string Audience { get; set; } = string.Empty;

        // Duración del token en minutos.
        // Si no se establece en appsettings.json, se aplica el valor por defecto (30 minutos).
        public int DuracionMinutos { get; set; } = 30;
    }
}

