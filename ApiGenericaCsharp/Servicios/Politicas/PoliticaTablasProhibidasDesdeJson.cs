// PoliticaTablasProhibidasDesdeJson.cs — Implementación que lee tablas prohibidas desde appsettings.json
// Ubicación: Servicios/Politicas/PoliticaTablasProhibidasDesdeJson.cs
//
// Principios SOLID aplicados:
// - SRP: Solo se encarga de leer configuración JSON y validar tablas
// - DIP: Implementa IPoliticaTablasProhibidas, permitiendo intercambiarla

using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using ApiGenericaCsharp.Servicios.Abstracciones;

namespace ApiGenericaCsharp.Servicios.Politicas
{
    /// <summary>
    /// Implementación concreta que lee la lista de tablas prohibidas desde appsettings.json.
    ///
    /// CONFIGURACIÓN ESPERADA EN appsettings.json:
    /// {
    ///   "TablasProhibidas": ["usuarios", "contrasenas", "sys_config", "auditoria"]
    /// }
    ///
    /// ESTRATEGIA DE VALIDACIÓN:
    /// - Si la lista está vacía o no existe: TODAS las tablas están permitidas
    /// - Si la lista contiene tablas: Solo se PROHÍBEN las tablas listadas
    /// - La comparación es case-insensitive: "Usuarios" = "usuarios" = "USUARIOS"
    /// </summary>
    public class PoliticaTablasProhibidasDesdeJson : IPoliticaTablasProhibidas
    {
        // HashSet proporciona búsqueda O(1) vs List que sería O(n)
        private readonly HashSet<string> _tablasProhibidas;

        /// <summary>
        /// Constructor que lee la configuración y prepara el HashSet de tablas prohibidas.
        /// </summary>
        public PoliticaTablasProhibidasDesdeJson(IConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(
                    nameof(configuration),
                    "IConfiguration no puede ser null."
                );

            // Leer la sección "TablasProhibidas" del JSON
            var tablasProhibidasArray = configuration.GetSection("TablasProhibidas")
                .Get<string[]>() ?? Array.Empty<string>();

            // Convertir a HashSet con comparación case-insensitive
            _tablasProhibidas = new HashSet<string>(
                tablasProhibidasArray.Where(t => !string.IsNullOrWhiteSpace(t)),
                StringComparer.OrdinalIgnoreCase
            );
        }

        /// <summary>
        /// Determina si una tabla está permitida verificando si NO está en la lista de prohibidas.
        /// </summary>
        public bool EsTablaPermitida(string nombreTabla)
        {
            // Nombres vacíos no están permitidos
            if (string.IsNullOrWhiteSpace(nombreTabla))
                return false;

            // Retorna true si NO está en la lista prohibida
            return !_tablasProhibidas.Contains(nombreTabla);
        }

        /// <summary>
        /// Método auxiliar para obtener la lista de tablas prohibidas (útil para debugging).
        /// </summary>
        public IReadOnlyCollection<string> ObtenerTablasProhibidas()
        {
            return _tablasProhibidas;
        }

        /// <summary>
        /// Indica si hay restricciones configuradas.
        /// </summary>
        public bool TieneRestricciones()
        {
            return _tablasProhibidas.Count > 0;
        }
    }
}

