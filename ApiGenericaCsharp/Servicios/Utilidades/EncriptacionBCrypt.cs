// EncriptacionBCrypt.cs — Clase de utilidad para manejo de encriptación BCrypt
// Ubicación: Servicios/Utilidades/EncriptacionBCrypt.cs
//
// Principios SOLID aplicados:
// - SRP: Esta clase solo se encarga de operaciones de encriptación/verificación
// - OCP: Abierta para extensión (nuevos métodos de hash) cerrada para modificación

using BCrypt.Net;
using System;

namespace ApiGenericaCsharp.Servicios.Utilidades
{
    /// <summary>
    /// Clase estática para operaciones de encriptación usando BCrypt.
    /// 
    /// BCrypt es específicamente diseñado para hashear contraseñas y datos sensibles
    /// que requieren verificación pero no recuperación del valor original.
    /// 
    /// Características de BCrypt implementadas:
    /// - Salt automático único por cada hash
    /// - Costo computacional ajustable para resistir ataques de fuerza bruta
    /// - Hashing irreversible para máxima seguridad
    /// - Verificación segura sin exponer el hash original
    /// </summary>
    public static class EncriptacionBCrypt
    {
        // Costo por defecto de BCrypt (12 es un balance entre seguridad y rendimiento)
        private const int CostoPorDefecto = 12;

        /// <summary>
        /// Encripta (hashea) un valor usando BCrypt con salt automático.
        /// 
        /// El resultado es un string de 60 caracteres que contiene:
        /// - Identificador del algoritmo
        /// - Costo utilizado
        /// - Salt generado
        /// - Hash resultante
        /// 
        /// Ejemplo: $2a$12$R9h/cIPz0gi.URNNX3kh2OPST9/PgBkqquzi.Ss7KIUgO2t0jWMUW
        /// </summary>
        /// <param name="valorOriginal">Valor a encriptar (contraseña, PIN, etc.)</param>
        /// <param name="costo">Costo computacional del hashing (por defecto 12)</param>
        /// <returns>Hash BCrypt de 60 caracteres</returns>
        public static string Encriptar(string valorOriginal, int costo = CostoPorDefecto)
        {
            if (string.IsNullOrWhiteSpace(valorOriginal))
                throw new ArgumentException("El valor a encriptar no puede estar vacío.", nameof(valorOriginal));

            if (costo < 4 || costo > 31)
                throw new ArgumentOutOfRangeException(nameof(costo), costo, 
                    "El costo de BCrypt debe estar entre 4 y 31. Recomendado: 10-15.");

            try
            {
                return BCrypt.Net.BCrypt.HashPassword(valorOriginal, costo);
            }
            catch (Exception excepcion)
            {
                throw new InvalidOperationException(
                    $"Error al generar hash BCrypt con costo {costo}: {excepcion.Message}",
                    excepcion
                );
            }
        }

        /// <summary>
        /// Verifica si un valor corresponde a un hash BCrypt específico.
        /// 
        /// Esta es la única forma de "verificar" datos hasheados con BCrypt, ya que
        /// el proceso es irreversible. No se puede obtener el valor original.
        /// </summary>
        /// <param name="valorOriginal">Valor a verificar (texto plano)</param>
        /// <param name="hashExistente">Hash BCrypt existente para comparar</param>
        /// <returns>true si el valor corresponde al hash, false si no</returns>
        public static bool Verificar(string valorOriginal, string hashExistente)
        {
            if (string.IsNullOrWhiteSpace(valorOriginal))
                throw new ArgumentException("El valor a verificar no puede estar vacío.", nameof(valorOriginal));

            if (string.IsNullOrWhiteSpace(hashExistente))
                throw new ArgumentException("El hash existente no puede estar vacío.", nameof(hashExistente));

            try
            {
                return BCrypt.Net.BCrypt.Verify(valorOriginal, hashExistente);
            }
            catch (Exception)
            {
                // Si ocurre cualquier error, retornar false
                return false;
            }
        }

        /// <summary>
        /// Verifica si un hash BCrypt necesita ser re-hasheado debido a cambio de costo.
        /// Útil para migraciones de seguridad.
        /// </summary>
        /// <param name="hashExistente">Hash BCrypt a verificar</param>
        /// <param name="costoDeseado">Nuevo costo deseado</param>
        /// <returns>true si el hash debe ser re-generado con el nuevo costo</returns>
        public static bool NecesitaReHasheo(string hashExistente, int costoDeseado = CostoPorDefecto)
        {
            if (string.IsNullOrWhiteSpace(hashExistente))
                return true;

            try
            {
                // Formato BCrypt: $2a$12$... donde 12 es el costo
                if (hashExistente.Length >= 7 && hashExistente.StartsWith("$2"))
                {
                    string costoParte = hashExistente.Substring(4, 2);
                    if (int.TryParse(costoParte, out int costoActual))
                    {
                        return costoActual < costoDeseado;
                    }
                }
                
                return true;
            }
            catch
            {
                return true;
            }
        }
    }
}

