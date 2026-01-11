using System;
using System.ComponentModel;

namespace ComparadorArchivos.Models
{
    /// <summary>
    /// Representa el resultado de la comparación de un archivo o directorio
    /// </summary>
    public class FileComparisonResult : INotifyPropertyChanged
    {
        private ComparisonStatus _status;
        private bool _selected;
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        public string FileName { get; set; }
        public string RelativePath { get; set; }
        public long SourceSize { get; set; }
        public long DestinationSize { get; set; }
        public DateTime? SourceModifiedDate { get; set; }
        public DateTime? DestinationModifiedDate { get; set; }
        public DateTime? SourceCreatedDate { get; set; }
        public DateTime? DestinationCreatedDate { get; set; }
        
        public ComparisonStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(SizeText));
                    OnPropertyChanged(nameof(FechaModificacion));
                    OnPropertyChanged(nameof(FechaCreacion));
                }
            }
        }
        public string SourceFullPath { get; set; }
        public string DestinationFullPath { get; set; }
        public string SourceHash { get; set; }
        public string DestinationHash { get; set; }
        public bool IsDirectory { get; set; }
        public string FileExtension { get; set; }
        public int SubdirectoryLevel { get; set; }

        /// <summary>
        /// Indica si el item está seleccionado para sincronización
        /// </summary>
        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected != value)
                {
                    _selected = value;
                    OnPropertyChanged(nameof(Selected));
                }
            }
        }

        public string StatusText
        {
            get
            {
                return Status switch
                {
                    ComparisonStatus.Missing => "Falta",
                    ComparisonStatus.Different => "Diferente",
                    ComparisonStatus.Match => "Coincide",
                    _ => "Desconocido"
                };
            }
        }

        public string TipoItem
        {
            get
            {
                return IsDirectory ? "📁 Carpeta" : "📄 Archivo";
            }
        }

        public string SizeText
        {
            get
            {
                if (IsDirectory) return "---";
                long size = Status == ComparisonStatus.Missing ? SourceSize : DestinationSize;
                return FormatSize(size);
            }
        }

        public string FechaModificacion
        {
            get
            {
                var fecha = Status == ComparisonStatus.Missing ? SourceModifiedDate : DestinationModifiedDate;
                return fecha?.ToString("dd/MM/yyyy HH:mm:ss") ?? "---";
            }
        }

        public string FechaCreacion
        {
            get
            {
                var fecha = Status == ComparisonStatus.Missing ? SourceCreatedDate : DestinationCreatedDate;
                return fecha?.ToString("dd/MM/yyyy HH:mm:ss") ?? "---";
            }
        }

        public string Extension
        {
            get
            {
                return IsDirectory ? "Carpeta" : (string.IsNullOrEmpty(FileExtension) ? "Sin extensión" : FileExtension);
            }
        }

        public string NivelProfundidad
        {
            get
            {
                return $"Nivel {SubdirectoryLevel}";
            }
        }

        /// <summary>
        /// Hash SHA-256 del origen (primeros 16 caracteres para visualización)
        /// </summary>
        public string HashOrigen
        {
            get
            {
                if (IsDirectory) return "---";
                if (string.IsNullOrEmpty(SourceHash)) return "No calculado";
                return SourceHash.Length >= 16 ? SourceHash.Substring(0, 16) + "..." : SourceHash;
            }
        }

        /// <summary>
        /// Hash SHA-256 del destino (primeros 16 caracteres para visualización)
        /// </summary>
        public string HashDestino
        {
            get
            {
                if (IsDirectory) return "---";
                if (string.IsNullOrEmpty(DestinationHash)) return "No existe";
                return DestinationHash.Length >= 16 ? DestinationHash.Substring(0, 16) + "..." : DestinationHash;
            }
        }

        /// <summary>
        /// Indica si los hashes coinciden
        /// </summary>
        public string IntegridadHash
        {
            get
            {
                if (IsDirectory) return "---";
                if (string.IsNullOrEmpty(SourceHash)) return "Sin verificar";
                if (string.IsNullOrEmpty(DestinationHash)) return "N/A";
                if (SourceHash == DestinationHash) return "✓ Coincide";
                return "✗ Difiere";
            }
        }

        private string FormatSize(long bytes)
        {
            if (bytes == 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum ComparisonStatus
    {
        Missing,    // No existe en destino
        Different,  // Existe pero es diferente
        Match       // Coincide completamente
    }
}
