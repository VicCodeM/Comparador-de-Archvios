using System;
using System.Collections.Concurrent;
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
    /// Servicio para sincronizar archivos entre carpetas con colas de procesamiento paralelo
    /// </summary>
    public class FileSynchronizer
    {
        public event EventHandler<ProgressEventArgs> ProgressChanged;
        public event EventHandler<ProgressEventArgs> FileProgressChanged;  // Progreso de archivo individual
        public event EventHandler<string> StatusChanged;
        public event EventHandler<LogEventArgs> LogMessage;
        public event EventHandler<FileComparisonResult> FileCopied;  // NUEVO: Para actualizar el grid en tiempo real
        
        private const int BufferSize = 16 * 1024 * 1024; // 16MB buffer para copia rápida (como TeraCopy)
        private const int MaxParallelTasks = 4; // Máximo de tareas paralelas para copiar directorios
        private readonly ConcurrentQueue<FileComparisonResult> _copyQueue = new ConcurrentQueue<FileComparisonResult>();

        /// <summary>
        /// Copia archivos y carpetas faltantes al destino
        /// </summary>
        public async Task<SyncResult> CopyMissingFilesAsync(
            List<FileComparisonResult> files,
            CancellationToken cancellationToken = default)
        {
            // Habilitar privilegios administrativos para acceso sin restricciones
            WindowsPermissionsHelper.EnableAdministrativePrivileges();
            
            var missingItems = files.Where(f => f.Status == ComparisonStatus.Missing).ToList();
            return await CopyFilesAsync(missingItems, "Copiando archivos y carpetas faltantes", cancellationToken);
        }

        /// <summary>
        /// Reemplaza archivos diferentes en el destino
        /// </summary>
        public async Task<SyncResult> ReplaceDifferentFilesAsync(
            List<FileComparisonResult> files,
            CancellationToken cancellationToken = default)
        {
            // Habilitar privilegios administrativos para acceso sin restricciones
            WindowsPermissionsHelper.EnableAdministrativePrivileges();
            
            var differentFiles = files.Where(f => f.Status == ComparisonStatus.Different).ToList();
            return await CopyFilesAsync(differentFiles, "Reemplazando archivos diferentes", cancellationToken);
        }

        /// <summary>
        /// Copia una lista de archivos usando colas y procesamiento paralelo
        /// </summary>
        private async Task<SyncResult> CopyFilesAsync(
            List<FileComparisonResult> files,
            string operationName,
            CancellationToken cancellationToken)
        {
            var result = new SyncResult();
            int totalFiles = files.Count;
            int processedFiles = 0;
            var lockObj = new object();

            OnLogMessage($"=== {operationName} ===", LogLevel.Info);
            OnLogMessage($"Total de archivos a procesar: {totalFiles}", LogLevel.Info);
            OnLogMessage($"Procesamiento paralelo: {MaxParallelTasks} tareas simultáneas", LogLevel.Info);

            // Separar carpetas y archivos para procesamiento optimizado
            var directories = files.Where(f => f.IsDirectory).ToList();
            var filesList = files.Where(f => !f.IsDirectory).ToList();

            // 1. Procesar CARPETAS primero (secuencial, rápido)
            OnLogMessage($"Creando {directories.Count} carpetas...", LogLevel.Info);
            foreach (var dir in directories)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    OnLogMessage("Operación cancelada por el usuario.", LogLevel.Warning);
                    break;
                }

                try
                {
                    OnStatusChanged($"Creando carpeta: {dir.RelativePath}");
                    
                    if (!Directory.Exists(dir.DestinationFullPath))
                    {
                        WindowsPermissionsHelper.CreateDirectorySafe(dir.DestinationFullPath);
                        result.CopiedFiles++;
                        OnLogMessage($"✓ Creada carpeta: {dir.RelativePath}", LogLevel.Success);
                    }
                    else
                    {
                        result.Skipped++;
                        OnLogMessage($"○ Carpeta ya existe: {dir.RelativePath}", LogLevel.Info);
                    }
                    
                    dir.Status = ComparisonStatus.Match;
                    OnFileCopied(dir);
                }
                catch (UnauthorizedAccessException ex)
                {
                    result.Errors++;
                    OnLogMessage($"✗ Error de permisos en carpeta {dir.RelativePath}: {ex.Message}", LogLevel.Error);
                    OnLogMessage($"  ℹ️ Intente ejecutar como Administrador", LogLevel.Warning);
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    OnLogMessage($"✗ Error creando carpeta {dir.RelativePath}: {ex.Message}", LogLevel.Error);
                }

                lock (lockObj)
                {
                    processedFiles++;
                    int percentage = (int)((double)processedFiles / totalFiles * 100);
                    OnProgressChanged(new ProgressEventArgs(percentage, processedFiles, totalFiles));
                }
            }

            // 2. Procesar ARCHIVOS en paralelo con cola
            OnLogMessage($"Copiando {filesList.Count} archivos en paralelo...", LogLevel.Info);
            
            var progressCounter = new ProgressCounter { Count = processedFiles };
            var copyTasks = new List<Task>();
            for (int i = 0; i < MaxParallelTasks; i++)
            {
                copyTasks.Add(ProcessCopyQueueAsync(filesList, result, totalFiles, progressCounter, lockObj, cancellationToken));
            }

            // Procesar archivos en paralelo
            foreach (var file in filesList)
            {
                _copyQueue.Enqueue(file);
            }

            // Esperar a que se completen todas las tareas
            await Task.WhenAll(copyTasks);
            processedFiles = progressCounter.Count;

            result.TotalProcessed = processedFiles;
            result.Skipped = totalFiles - processedFiles;

            OnLogMessage($"\n=== Resumen de {operationName} ===", LogLevel.Info);
            OnLogMessage($"Total procesados: {result.TotalProcessed}", LogLevel.Info);
            OnLogMessage($"Copiados exitosamente: {result.CopiedFiles}", LogLevel.Success);
            OnLogMessage($"Omitidos: {result.Skipped}", LogLevel.Warning);
            OnLogMessage($"Errores: {result.Errors}", LogLevel.Error);

            return result;
        }

        /// <summary>
        /// Procesa archivos de la cola de copia en paralelo
        /// </summary>
        private async Task ProcessCopyQueueAsync(
            List<FileComparisonResult> filesList,
            SyncResult result,
            int totalFiles,
            ProgressCounter progressCounter,
            object lockObj,
            CancellationToken cancellationToken)
        {
            while (_copyQueue.TryDequeue(out var file))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _copyQueue.Enqueue(file); // Devolver a la cola si se cancela
                    break;
                }

                try
                {
                    OnStatusChanged($"Copiando: {file.RelativePath}");

                    // Crear directorio padre si no existe
                    string destinationDir = Path.GetDirectoryName(file.DestinationFullPath);
                    if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                    {
                        WindowsPermissionsHelper.CreateDirectorySafe(destinationDir);
                    }

                    // Copiar archivo con verificación de integridad
                    await FastCopyFileAsync(file.SourceFullPath, file.DestinationFullPath, cancellationToken);

                    lock (lockObj)
                    {
                        result.CopiedFiles++;
                    }
                    OnLogMessage($"✓ Copiado: {file.RelativePath} ({file.SizeText})", LogLevel.Success);

                    // Actualizar información del archivo
                    file.DestinationSize = file.SourceSize;
                    file.DestinationModifiedDate = File.GetLastWriteTime(file.DestinationFullPath);
                    file.DestinationCreatedDate = File.GetCreationTime(file.DestinationFullPath);
                    
                    if (!string.IsNullOrEmpty(file.SourceHash))
                    {
                        file.DestinationHash = file.SourceHash;
                    }

                    file.Status = ComparisonStatus.Match;
                    OnFileCopied(file);
                }
                catch (UnauthorizedAccessException)
                {
                    lock (lockObj)
                    {
                        result.Errors++;
                    }
                    OnLogMessage($"✗ Permiso denegado: {file.RelativePath}", LogLevel.Error);
                    OnLogMessage($"  ℹ️ Ejecute como Administrador", LogLevel.Warning);
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        result.Errors++;
                    }
                    OnLogMessage($"✗ Error: {file.RelativePath} - {ex.Message}", LogLevel.Error);
                }

                lock (lockObj)
                {
                    progressCounter.Count++;
                    int percentage = (int)((double)progressCounter.Count / totalFiles * 100);
                    OnProgressChanged(new ProgressEventArgs(percentage, progressCounter.Count, totalFiles));
                }
            }
        }

        private class ProgressCounter
        {
            public int Count { get; set; }
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

        protected virtual void OnLogMessage(string message, LogLevel level)
        {
            LogMessage?.Invoke(this, new LogEventArgs(message, level));
        }

        protected virtual void OnFileCopied(FileComparisonResult file)
        {
            FileCopied?.Invoke(this, file);
        }

        /// <summary>
        /// Copia un archivo usando un buffer grande para máxima velocidad (como TeraCopy)
        /// y verifica la integridad con SHA-256
        /// Incluye manejo robusto de permisos para archivos protegidos
        /// </summary>
        private async Task FastCopyFileAsync(string sourceFile, string destinationFile, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                try
                {
                    FileStream sourceStream = null;
                    FileStream destStream = null;
                    int maxRetries = 3;
                    
                    // Intentar abrir el archivo origen con reintentos y manejo de permisos
                    for (int retry = 0; retry < maxRetries; retry++)
                    {
                        try
                        {
                            sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
                            break;
                        }
                        catch (UnauthorizedAccessException) when (retry < maxRetries - 1)
                        {
                            WindowsPermissionsHelper.TakeOwnership(sourceFile, isDirectory: false);
                            Thread.Sleep(100);
                        }
                    }
                    
                    if (sourceStream == null)
                    {
                        throw new UnauthorizedAccessException($"No se pudo acceder al archivo origen: {sourceFile}");
                    }
                    
                    // Intentar crear el archivo destino con reintentos y manejo de permisos
                    for (int retry = 0; retry < maxRetries; retry++)
                    {
                        try
                        {
                            // Si el archivo existe y está protegido, remover protección
                            if (File.Exists(destinationFile))
                            {
                                try
                                {
                                    File.SetAttributes(destinationFile, FileAttributes.Normal);
                                }
                                catch
                                {
                                    WindowsPermissionsHelper.TakeOwnership(destinationFile, isDirectory: false);
                                    File.SetAttributes(destinationFile, FileAttributes.Normal);
                                }
                            }
                            
                            destStream = new FileStream(destinationFile, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.SequentialScan);
                            break;
                        }
                        catch (UnauthorizedAccessException) when (retry < maxRetries - 1)
                        {
                            // Tomar ownership del directorio padre si es necesario
                            string parentDir = Path.GetDirectoryName(destinationFile);
                            if (!string.IsNullOrEmpty(parentDir))
                            {
                                WindowsPermissionsHelper.TakeOwnership(parentDir, isDirectory: true);
                            }
                            Thread.Sleep(100);
                        }
                    }
                    
                    if (destStream == null)
                    {
                        sourceStream?.Dispose();
                        throw new UnauthorizedAccessException($"No se pudo crear el archivo destino: {destinationFile}");
                    }
                    
                    // 1. COPIAR archivo con buffer grande y reportar progreso
                    using (sourceStream)
                    using (destStream)
                    {
                        byte[] buffer = new byte[BufferSize];
                        int bytesRead;
                        long totalBytesRead = 0;
                        long fileSize = sourceStream.Length;
                        
                        while ((bytesRead = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            destStream.Write(buffer, 0, bytesRead);
                            
                            // Reportar progreso de este archivo
                            totalBytesRead += bytesRead;
                            int filePercentage = fileSize > 0 ? (int)((double)totalBytesRead / fileSize * 100) : 100;
                            OnFileProgressChanged(new ProgressEventArgs(filePercentage, (int)totalBytesRead, (int)fileSize));
                        }
                    }
                    
                    // 2. VERIFICAR integridad con hash SHA-256
                    string sourceHash = CalculateSHA256(sourceFile);
                    string destHash = CalculateSHA256(destinationFile);
                    
                    if (sourceHash != destHash)
                    {
                        // Si el hash no coincide, eliminar el archivo corrupto y lanzar error
                        File.Delete(destinationFile);
                        throw new IOException($"❌ Error de integridad: El archivo copiado no coincide con el original. Hash origen: {sourceHash}, Hash destino: {destHash}");
                    }
                    
                    // 3. PRESERVAR fechas y atributos solo si la verificación fue exitosa
                    try
                    {
                        File.SetCreationTime(destinationFile, File.GetCreationTime(sourceFile));
                        File.SetLastWriteTime(destinationFile, File.GetLastWriteTime(sourceFile));
                        File.SetAttributes(destinationFile, File.GetAttributes(sourceFile));
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Si no podemos establecer atributos, al menos intentamos con ownership
                        WindowsPermissionsHelper.TakeOwnership(destinationFile, isDirectory: false);
                        try
                        {
                            File.SetCreationTime(destinationFile, File.GetCreationTime(sourceFile));
                            File.SetLastWriteTime(destinationFile, File.GetLastWriteTime(sourceFile));
                            File.SetAttributes(destinationFile, File.GetAttributes(sourceFile));
                        }
                        catch
                        {
                            // Si aún así falla, continuar sin preservar atributos
                            OnLogMessage($"  ⚠️ No se pudieron preservar todos los atributos del archivo", LogLevel.Warning);
                        }
                    }
                    
                    OnLogMessage($"  ✓ Integridad verificada (SHA-256: {sourceHash.Substring(0, 8)}...)", LogLevel.Success);
                }
                catch (IOException ex) when (ex.Message.Contains("being used by another process") || ex.HResult == -2147024864)
                {
                    // Archivo en uso por otro proceso
                    string fileName = Path.GetFileName(sourceFile);
                    OnLogMessage($"  ⚠️ DOCUMENTO EN USO: {fileName}", LogLevel.Error);
                    throw new IOException($"⚠️ DOCUMENTO EN USO: {fileName}\n\nEl archivo está siendo usado por otra aplicación.\nCierre el archivo e intente nuevamente.", ex);
                }
            }, cancellationToken);
        }
        
        /// <summary>
        /// Calcula el hash SHA-256 de un archivo con reporte de progreso
        /// Incluye manejo de permisos para archivos protegidos
        /// </summary>
        private string CalculateSHA256(string filePath, string label = "Verificando")
        {
            try
            {
                FileStream stream = null;
                int maxRetries = 3;
                
                // Intentar abrir el archivo con reintentos y manejo de permisos
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
                }
                
                if (stream == null)
                {
                    throw new UnauthorizedAccessException($"No se pudo acceder al archivo: {filePath}");
                }
                
                using (var sha256 = SHA256.Create())
                using (stream)
                {
                    long fileSize = stream.Length;
                    long totalBytesRead = 0;
                    byte[] buffer = new byte[8192];
                    int bytesRead;
                    
                    sha256.Initialize();
                    
                    // Leer archivo en bloques y reportar progreso
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                        
                        totalBytesRead += bytesRead;
                        int percentage = fileSize > 0 ? (int)((double)totalBytesRead / fileSize * 100) : 100;
                        OnFileProgressChanged(new ProgressEventArgs(percentage, (int)totalBytesRead, (int)fileSize));
                    }
                    
                    // Finalizar hash
                    sha256.TransformFinalBlock(new byte[0], 0, 0);
                    return BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch (IOException ex) when (ex.Message.Contains("being used by another process") || ex.HResult == -2147024864)
            {
                // Archivo en uso por otro proceso durante verificación
                string fileName = Path.GetFileName(filePath);
                OnLogMessage($"  ⚠️ DOCUMENTO EN USO (verificando): {fileName}", LogLevel.Error);
                throw new IOException($"⚠️ DOCUMENTO EN USO: {fileName}\n\nEl archivo está siendo usado por otra aplicación durante la verificación.\nCierre el archivo e intente nuevamente.", ex);
            }
        }
    }

    public class SyncResult
    {
        public int TotalProcessed { get; set; }
        public int CopiedFiles { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }
    }

    public class LogEventArgs : EventArgs
    {
        public string Message { get; set; }
        public LogLevel Level { get; set; }
        public DateTime Timestamp { get; set; }

        public LogEventArgs(string message, LogLevel level)
        {
            Message = message;
            Level = level;
            Timestamp = DateTime.Now;
        }
    }

    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }
}
