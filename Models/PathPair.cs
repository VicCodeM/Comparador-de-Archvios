using System;

namespace ComparadorArchivos.Models
{
    /// <summary>
    /// Representa un par de rutas (origen y destino) para comparación y sincronización
    /// </summary>
    public class PathPair
    {
        /// <summary>
        /// Ruta de origen
        /// </summary>
        public string SourcePath { get; set; }

        /// <summary>
        /// Ruta de destino
        /// </summary>
        public string DestinationPath { get; set; }

        /// <summary>
        /// Identificador único del par de rutas
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Indica si este par de rutas es válido (ambas rutas existen)
        /// </summary>
        public bool IsValid
        {
            get
            {
                return !string.IsNullOrWhiteSpace(SourcePath) &&
                       !string.IsNullOrWhiteSpace(DestinationPath) &&
                       System.IO.Directory.Exists(SourcePath) &&
                       System.IO.Directory.Exists(DestinationPath);
            }
        }

        public PathPair()
        {
            Id = Guid.NewGuid();
            SourcePath = string.Empty;
            DestinationPath = string.Empty;
        }

        public PathPair(string sourcePath, string destinationPath)
        {
            Id = Guid.NewGuid();
            SourcePath = sourcePath ?? string.Empty;
            DestinationPath = destinationPath ?? string.Empty;
        }

        public override string ToString()
        {
            return $"{SourcePath} → {DestinationPath}";
        }
    }
}
