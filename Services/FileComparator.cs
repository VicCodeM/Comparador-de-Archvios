using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ComparadorArchivos.Models;

namespace ComparadorArchivos.Services
{
    /// <summary>
    /// Servicio para comparar archivos entre dos carpetas
    /// </summary>
    public class FileComparator
    {
        public event EventHandler<ProgressEventArgs> ProgressChanged;
        public event EventHandler<ProgressEventArgs> FileProgressChanged;  // Progreso de archivo individual
        public event EventHandler<string> StatusChanged;

        /// <summary>
        /// Compara dos carpetas y retorna los resultados (archivos Y directorios, incluso vacíos)
        /// </summary>
        public async Task<List<FileComparisonResult>> CompareDirectoriesAsync(
            string sourcePath,
            string destinationPath,
            bool useHashComparison,
            CancellationToken cancellationToken = default)
        {
            var results = new List<FileComparisonResult>();

            // Normalizar las rutas (asegurar que no tengan barra final)
            sourcePath = sourcePath.TrimEnd('\\', '/');
            destinationPath = destinationPath.TrimEnd('\\', '/');

            OnStatusChanged("Habilitando privilegios administrativos para acceso completo...");
            
            // Habilitar privilegios administrativos para acceso sin restricciones
            if (!WindowsPermissionsHelper.EnableAdministrativePrivileges())
            {
                OnStatusChanged("⚠️ Advertencia: No se pudieron habilitar todos los privilegios administrativos.");
            }

            OnStatusChanged("Analizando carpeta origen y TODOS sus subdirectorios (incluyendo carpetas vacías y protegidas)...");
            
            // 1. Obtener TODAS las carpetas y archivos de forma recursiva con manejo robusto de permisos
            string[] sourceDirectories;
            string[] sourceFiles;
            
            WindowsPermissionsHelper.GetAllFilesAndDirectoriesRecursive(
                sourcePath,
                out sourceDirectories,
                out sourceFiles,
                status => OnStatusChanged(status));
            
            int totalItems = sourceDirectories.Length + sourceFiles.Length;
            int processedItems = 0;

            OnStatusChanged($"✓ Encontrados {totalItems} items: {sourceFiles.Length} archivos + {sourceDirectories.Length} carpetas (incluyendo carpetas protegidas del sistema)...");

            // PRIMERO: Procesar carpetas (incluyendo vacías)
            foreach (var sourceDir in sourceDirectories)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                string relativePath = GetRelativePath(sourcePath, sourceDir);
                string destinationDir = Path.Combine(destinationPath, relativePath);

                var dirInfo = new DirectoryInfo(sourceDir);
                int level = relativePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Length;

                var result = new FileComparisonResult
                {
                    FileName = dirInfo.Name,
                    RelativePath = relativePath,
                    SourceFullPath = sourceDir,
                    DestinationFullPath = destinationDir,
                    IsDirectory = true,
                    SubdirectoryLevel = level,
                    FileExtension = "",
                    SourceCreatedDate = dirInfo.CreationTime,
                    SourceModifiedDate = dirInfo.LastWriteTime,
                    Status = Directory.Exists(destinationDir) ? ComparisonStatus.Match : ComparisonStatus.Missing
                };

                if (Directory.Exists(destinationDir))
                {
                    var destDirInfo = new DirectoryInfo(destinationDir);
                    result.DestinationCreatedDate = destDirInfo.CreationTime;
                    result.DestinationModifiedDate = destDirInfo.LastWriteTime;
                }

                results.Add(result);

                processedItems++;
                int percentage = (int)((double)processedItems / totalItems * 100);
                OnProgressChanged(new ProgressEventArgs(percentage, processedItems, totalItems));
                OnStatusChanged($"Procesando carpeta: {relativePath} ({processedItems}/{totalItems})");
            }

            // SEGUNDO: Procesar archivos
            foreach (var sourceFile in sourceFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                // Calcular ruta relativa
                string relativePath = GetRelativePath(sourcePath, sourceFile);
                string destinationFile = Path.Combine(destinationPath, relativePath);

                var sourceInfo = new FileInfo(sourceFile);
                int level = relativePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Length - 1;

                var result = new FileComparisonResult
                {
                    FileName = Path.GetFileName(sourceFile),
                    RelativePath = relativePath,
                    SourceFullPath = sourceFile,
                    DestinationFullPath = destinationFile,
                    IsDirectory = false,
                    FileExtension = sourceInfo.Extension,
                    SubdirectoryLevel = level,
                    SourceSize = sourceInfo.Length,
                    SourceModifiedDate = sourceInfo.LastWriteTime,
                    SourceCreatedDate = sourceInfo.CreationTime
                };

                // Verificar si existe en destino
                if (!File.Exists(destinationFile))
                {
                    result.Status = ComparisonStatus.Missing;
                }
                else
                {
                    // Archivo existe, comparar
                    var destInfo = new FileInfo(destinationFile);
                    result.DestinationSize = destInfo.Length;
                    result.DestinationModifiedDate = destInfo.LastWriteTime;
                    result.DestinationCreatedDate = destInfo.CreationTime;

                    // Comparar por tamaño primero (más rápido)
                    if (result.SourceSize != result.DestinationSize)
                    {
                        result.Status = ComparisonStatus.Different;
                    }
                    else if (useHashComparison)
                    {
                        // Comparar por hash si está habilitado
                        OnStatusChanged($"Calculando hash: {result.FileName}");
                        
                        result.SourceHash = await CalculateFileHashAsync(sourceFile, cancellationToken);
                        result.DestinationHash = await CalculateFileHashAsync(destinationFile, cancellationToken);

                        result.Status = result.SourceHash == result.DestinationHash
                            ? ComparisonStatus.Match
                            : ComparisonStatus.Different;
                    }
                    else
                    {
                        // Si no se usa hash, comparar por fecha
                        result.Status = result.SourceModifiedDate == result.DestinationModifiedDate
                            ? ComparisonStatus.Match
                            : ComparisonStatus.Different;
                    }
                }

                results.Add(result);

                processedItems++;
                int percentage = (int)((double)processedItems / totalItems * 100);
                OnProgressChanged(new ProgressEventArgs(percentage, processedItems, totalItems));
                OnStatusChanged($"Procesando: {result.FileName} ({processedItems}/{totalItems})");
            }

            OnStatusChanged($"Análisis completado. Total: {results.Count} items ({sourceFiles.Length} archivos + {sourceDirectories.Length} carpetas)");
            return results;
        }

        /// <summary>
        /// Calcula el hash SHA-256 de un archivo de forma asíncrona con reporte de progreso
        /// Incluye manejo de permisos para archivos protegidos
        /// </summary>
        private async Task<string> CalculateFileHashAsync(string filePath, CancellationToken cancellationToken)
        {
            // Intentar tomar ownership si no tenemos acceso
            FileStream stream = null;
            int maxRetries = 3;
            
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    stream = File.OpenRead(filePath);
                    break;
                }
                catch (UnauthorizedAccessException) when (retry < maxRetries - 1)
                {
                    WindowsPermissionsHelper.TakeOwnership(filePath, isDirectory: false);
                    Thread.Sleep(100);
                }
                catch (IOException) when (retry < maxRetries - 1)
                {
                    Thread.Sleep(200);
                }
            }
            
            if (stream == null)
            {
                // Si después de los reintentos no podemos abrir el archivo, retornar hash vacío
                return "ERROR_ACCESS_DENIED";
            }
            
            using (var sha256 = SHA256.Create())
            using (stream)
            {
                var hash = await Task.Run(() =>
                {
                    long fileSize = stream.Length;
                    long totalBytesRead = 0;
                    byte[] buffer = new byte[8192];
                    int bytesRead;
                    
                    // Inicializar hash
                    sha256.Initialize();
                    
                    // Leer archivo en bloques y reportar progreso
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                        
                        totalBytesRead += bytesRead;
                        int percentage = fileSize > 0 ? (int)((double)totalBytesRead / fileSize * 100) : 100;
                        OnFileProgressChanged(new ProgressEventArgs(percentage, (int)totalBytesRead, (int)fileSize));
                    }
                    
                    // Finalizar hash
                    sha256.TransformFinalBlock(new byte[0], 0, 0);
                    return sha256.Hash;
                }, cancellationToken);
                
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Obtiene la ruta relativa de un archivo respecto a una carpeta base
        /// Maneja correctamente todos los niveles de subdirectorios
        /// Compatible con .NET Framework 4.8
        /// </summary>
        private string GetRelativePath(string basePath, string fullPath)
        {
            // Normalizar ambas rutas
            basePath = Path.GetFullPath(basePath).TrimEnd('\\', '/');
            fullPath = Path.GetFullPath(fullPath);

            // Convertir a URI para comparación correcta
            Uri baseUri = new Uri(basePath + Path.DirectorySeparatorChar);
            Uri fullUri = new Uri(fullPath);

            // Obtener la ruta relativa
            Uri relativeUri = baseUri.MakeRelativeUri(fullUri);
            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            // Reemplazar barras diagonales con separador del sistema
            return relativePath.Replace('/', Path.DirectorySeparatorChar);
        }

        protected virtual void OnProgressChanged(ProgressEventArgs e)
        {
            ProgressChanged?.Invoke(this, e);
        }

        protected virtual void OnFileProgressChanged(ProgressEventArgs e)
        {
            FileProgressChanged?.Invoke(this, e);
        }

        protected virtual void OnStatusChanged(string status)
        {
            StatusChanged?.Invoke(this, status);
        }
    }

    public class ProgressEventArgs : EventArgs
    {
        public int Percentage { get; set; }
        public int ProcessedItems { get; set; }
        public int TotalItems { get; set; }

        public ProgressEventArgs(int percentage, int processedItems, int totalItems)
        {
            Percentage = percentage;
            ProcessedItems = processedItems;
            TotalItems = totalItems;
        }
    }
}
