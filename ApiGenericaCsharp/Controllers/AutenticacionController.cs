// AutenticacionController.cs
// Controlador genérico que autentica usuarios en cualquier tabla de base de datos
// y genera tokens JWT válidos si las credenciales son correctas.
//
// ---------------------------------------------------------
// CARACTERÍSTICAS
// ---------------------------------------------------------
// - Compatible con cualquier tabla y campos personalizados
// - Usa BCrypt para comparar contraseñas encriptadas
// - Genera tokens JWT configurables desde appsettings.json
// - No depende del tipo de base de datos (SQL Server, PostgreSQL, etc.)
// - Sigue principios SOLID: SRP, DIP y OCP
//
// ---------------------------------------------------------
// IMPORTACIONES NECESARIAS
// ---------------------------------------------------------
using Microsoft.AspNetCore.Mvc;                      // Para el controlador y las acciones
using Microsoft.Extensions.Options;                  // Para inyectar configuraciones (IOptions)
using Microsoft.IdentityModel.Tokens;                // Para firmar y generar el token JWT
using System.IdentityModel.Tokens.Jwt;               // Para manipular JWT
using System.Security.Claims;                        // Para definir los claims dentro del token
using System.Text;                                   // Para codificar la clave secreta
using ApiGenericaCsharp.Modelos;                          // Para la clase ConfiguracionJwt
using ApiGenericaCsharp.Servicios.Abstracciones;           // Para la interfaz IServicioCrud

namespace ApiGenericaCsharp.Controllers
{
    /// <summary>
    /// Controlador que permite autenticar un usuario contra cualquier tabla
    /// y devuelve un token JWT si las credenciales son válidas.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AutenticacionController : ControllerBase
    {
        private readonly ConfiguracionJwt _configuracionJwt;   // Configuración JWT cargada desde appsettings
        private readonly IServicioCrud _servicioCrud;          // Servicio genérico para verificar contraseñas

        // ---------------------------------------------------------
        // CONSTRUCTOR
        // ---------------------------------------------------------
        public AutenticacionController(
            IOptions<ConfiguracionJwt> opcionesJwt, 
            IServicioCrud servicioCrud)
        {
            _configuracionJwt = opcionesJwt.Value;
            _servicioCrud = servicioCrud;
        }

        // ---------------------------------------------------------
        // POST: /api/autenticacion/token
        // Descripción:
        //   - Verifica credenciales en la tabla indicada (con hash BCrypt)
        //   - Si son válidas, genera un token JWT con los datos básicos.
        // ---------------------------------------------------------
        [HttpPost("token")]
        public async Task<IActionResult> GenerarToken([FromBody] CredencialesGenericas credenciales)
        {
            // -----------------------------------------------------
            // VALIDACIONES BÁSICAS DEL BODY
            // -----------------------------------------------------
            if (string.IsNullOrWhiteSpace(credenciales.Tabla) ||
                string.IsNullOrWhiteSpace(credenciales.CampoUsuario) ||
                string.IsNullOrWhiteSpace(credenciales.CampoContrasena) ||
                string.IsNullOrWhiteSpace(credenciales.Usuario) ||
                string.IsNullOrWhiteSpace(credenciales.Contrasena))
            {
                return BadRequest(new
                {
                    estado = 400,
                    mensaje = "Debe enviar tabla, campos y credenciales completas.",
                    ejemplo = new
                    {
                        tabla = "TablaDeUsuarios",
                        campoUsuario = "ejemploCampoUsuario",
                        campoContrasena = "ejemploCampoContrasena",
                        usuario = "ejemplo@correo.com",
                        contrasena = "admin123"
                    }
                });
            }

            // -----------------------------------------------------
            // FASE 1: VERIFICACIÓN DE CREDENCIALES ENCRIPTADAS
            // -----------------------------------------------------
            // Se delega la comparación de contraseñas al ServicioCrud,
            // el cual implementa la lógica de verificación usando BCrypt.
            var (codigo, mensaje) = await _servicioCrud.VerificarContrasenaAsync(
                credenciales.Tabla,
                null, // Esquema opcional
                credenciales.CampoUsuario,
                credenciales.CampoContrasena,
                credenciales.Usuario,
                credenciales.Contrasena
            );

            // -----------------------------------------------------
            // FASE 2: EVALUACIÓN DEL RESULTADO DE VERIFICACIÓN
            // -----------------------------------------------------
            if (codigo == 404)
                return NotFound(new { estado = 404, mensaje = "Usuario no encontrado." });

            if (codigo == 401)
                return Unauthorized(new { estado = 401, mensaje = "Contraseña incorrecta." });

            if (codigo != 200)
                return StatusCode(500, new { estado = 500, mensaje = "Error interno durante la verificación.", detalle = mensaje });

            // -----------------------------------------------------
            // FASE 3: GENERACIÓN DEL TOKEN JWT
            // -----------------------------------------------------
            // Si la verificación fue exitosa, se crea un token JWT con los datos básicos del usuario.
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, credenciales.Usuario),       // Nombre de usuario
                new Claim("tabla", credenciales.Tabla),                 // Tabla usada para autenticación
                new Claim("campoUsuario", credenciales.CampoUsuario)    // Campo de usuario utilizado
            };

            // Clave secreta para firmar el token (obtenida desde appsettings.json)
            var clave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuracionJwt.Key));

            // Se especifica el algoritmo de firma HMAC-SHA256
            var credencialesFirma = new SigningCredentials(clave, SecurityAlgorithms.HmacSha256);

            // Duración configurable del token (en minutos)
            var duracion = _configuracionJwt.DuracionMinutos > 0 ? _configuracionJwt.DuracionMinutos : 60;

            // Construcción del token con sus parámetros principales
            var token = new JwtSecurityToken(
                issuer: _configuracionJwt.Issuer,       // Emisor del token
                audience: _configuracionJwt.Audience,   // Público autorizado
                claims: claims,                         // Datos del usuario dentro del token
                expires: DateTime.UtcNow.AddMinutes(duracion), // Fecha de expiración
                signingCredentials: credencialesFirma   // Firma digital
            );

            // Serializa el token a formato string para enviarlo al cliente
            string tokenGenerado = new JwtSecurityTokenHandler().WriteToken(token);

            // -----------------------------------------------------
            // FASE 4: RESPUESTA FINAL
            // -----------------------------------------------------
            return Ok(new
            {
                estado = 200,
                mensaje = "Autenticación exitosa.",
                usuario = credenciales.Usuario,
                token = tokenGenerado,
                expiracion = token.ValidTo
            });
        }
    }

    // ---------------------------------------------------------
    // CLASE AUXILIAR: CredencialesGenericas
    // ---------------------------------------------------------
    // Representa el cuerpo del POST enviado por el cliente.
    // Incluye toda la información necesaria para validar un usuario
    // contra cualquier tabla de la base de datos.
    public class CredencialesGenericas
    {
        // Nombre de la tabla que contiene los usuarios (por ejemplo: "usuario", "vendedor", "cliente")
        public string Tabla { get; set; } = string.Empty;

        // Nombre del campo que almacena el identificador de usuario (por ejemplo: "email", "nombre", "login")
        public string CampoUsuario { get; set; } = string.Empty;

        // Nombre del campo que almacena la contraseña (por ejemplo: "clave", "password", "contrasena")
        public string CampoContrasena { get; set; } = string.Empty;

        // Valor del usuario que intenta autenticarse
        public string Usuario { get; set; } = string.Empty;

        // Contraseña enviada por el usuario (texto plano para comparar con hash en BD)
        public string Contrasena { get; set; } = string.Empty;
    }
}

