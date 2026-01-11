using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Threading;

namespace ComparadorArchivos.Services
{
    /// <summary>
    /// Clase para manejar permisos de Windows y acceso a carpetas protegidas del sistema
    /// Permite acceso sin restricciones a todas las carpetas de Windows
    /// </summary>
    public static class WindowsPermissionsHelper
    {
        // Windows API para habilitar privilegios
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState, int BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        // APIs para deshabilitar redirección de sistema de archivos (WOW64)
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Wow64DisableWow64FsRedirection(out IntPtr oldValue);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Wow64RevertWow64FsRedirection(IntPtr oldValue);

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

        // Estado de redirección de sistema de archivos
        private static IntPtr _fsRedirectionOldValue = IntPtr.Zero;
        private static bool _fsRedirectionDisabled = false;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID Luid;
            public uint Attributes;
        }

        /// <summary>
        /// Habilita privilegios administrativos necesarios para acceso completo
        /// </summary>
        public static bool EnableAdministrativePrivileges()
        {
            try
            {
                // Habilitar privilegios necesarios
                EnablePrivilege("SeBackupPrivilege");      // Privilegio de respaldo (leer cualquier archivo)
                EnablePrivilege("SeRestorePrivilege");     // Privilegio de restauración (escribir en cualquier archivo)
                EnablePrivilege("SeTakeOwnershipPrivilege"); // Tomar ownership de archivos
                EnablePrivilege("SeSecurityPrivilege");    // Modificar seguridad
                
                // Deshabilitar redirección de sistema de archivos para acceso completo a System32
                DisableFileSystemRedirection();
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error habilitando privilegios: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Deshabilita la redirección del sistema de archivos WOW64
        /// Permite acceder a System32 real en lugar de SysWOW64
        /// </summary>
        public static bool DisableFileSystemRedirection()
        {
            try
            {
                if (!_fsRedirectionDisabled && Environment.Is64BitOperatingSystem)
                {
                    if (Wow64DisableWow64FsRedirection(out _fsRedirectionOldValue))
                    {
                        _fsRedirectionDisabled = true;
                        return true;
                    }
                }
                return _fsRedirectionDisabled;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deshabilitando redirección FS: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Restaura la redirección del sistema de archivos WOW64
        /// </summary>
        public static bool RestoreFileSystemRedirection()
        {
            try
            {
                if (_fsRedirectionDisabled && _fsRedirectionOldValue != IntPtr.Zero)
                {
                    if (Wow64RevertWow64FsRedirection(_fsRedirectionOldValue))
                    {
                        _fsRedirectionDisabled = false;
                        _fsRedirectionOldValue = IntPtr.Zero;
                        return true;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restaurando redirección FS: {ex.Message}");
                return false;
            }
        }

        private static void EnablePrivilege(string privilegeName)
        {
            IntPtr hToken = IntPtr.Zero;
            try
            {
                // Abrir el token del proceso
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out hToken))
                {
                    throw new InvalidOperationException($"No se pudo abrir el token del proceso. Error: {Marshal.GetLastWin32Error()}");
                }

                // Buscar el privilegio
                LUID luid;
                if (!LookupPrivilegeValue(null, privilegeName, out luid))
                {
                    throw new InvalidOperationException($"No se pudo encontrar el privilegio {privilegeName}. Error: {Marshal.GetLastWin32Error()}");
                }

                // Configurar el privilegio
                TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED
                };

                // Ajustar el privilegio
                if (!AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                {
                    throw new InvalidOperationException($"No se pudo habilitar el privilegio {privilegeName}. Error: {Marshal.GetLastWin32Error()}");
                }
            }
            finally
            {
                if (hToken != IntPtr.Zero)
                {
                    CloseHandle(hToken);
                }
            }
        }

        /// <summary>
        /// Toma ownership de un archivo o carpeta y otorga permisos completos
        /// </summary>
        public static bool TakeOwnership(string path, bool isDirectory = false)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Intentando tomar ownership de: {path}");
                
                var currentUser = WindowsIdentity.GetCurrent();
                var currentUserSid = currentUser.User;

                if (isDirectory)
                {
                    var dirInfo = new DirectoryInfo(path);
                    
                    try
                    {
                        var dirSecurity = dirInfo.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
                        
                        // Cambiar el owner a nosotros
                        dirSecurity.SetOwner(currentUserSid);
                        dirInfo.SetAccessControl(dirSecurity);
                        System.Diagnostics.Debug.WriteLine($"✓ Owner cambiado para directorio: {path}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error cambiando owner de directorio: {ex.Message}");
                    }

                    // Intentar agregar permisos completos
                    try
                    {
                        var dirSecurity = dirInfo.GetAccessControl();
                        dirSecurity.AddAccessRule(new FileSystemAccessRule(
                            currentUserSid,
                            FileSystemRights.FullControl,
                            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                            PropagationFlags.None,
                            AccessControlType.Allow));
                        dirInfo.SetAccessControl(dirSecurity);
                        System.Diagnostics.Debug.WriteLine($"✓ Permisos agregados para directorio: {path}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error agregando permisos a directorio: {ex.Message}");
                    }
                }
                else
                {
                    var fileInfo = new FileInfo(path);
                    
                    try
                    {
                        var fileSecurity = fileInfo.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
                        
                        // Cambiar el owner a nosotros
                        fileSecurity.SetOwner(currentUserSid);
                        fileInfo.SetAccessControl(fileSecurity);
                        System.Diagnostics.Debug.WriteLine($"✓ Owner cambiado para archivo: {path}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error cambiando owner de archivo: {ex.Message}");
                    }

                    // Intentar agregar permisos completos
                    try
                    {
                        var fileSecurity = fileInfo.GetAccessControl();
                        fileSecurity.AddAccessRule(new FileSystemAccessRule(
                            currentUserSid,
                            FileSystemRights.FullControl,
                            AccessControlType.Allow));
                        fileInfo.SetAccessControl(fileSecurity);
                        System.Diagnostics.Debug.WriteLine($"✓ Permisos agregados para archivo: {path}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error agregando permisos a archivo: {ex.Message}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ Error general en TakeOwnership para {path}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Intenta acceder a un directorio con reintentos y manejo de permisos
        /// </summary>
        public static string[] GetDirectoriesSafe(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, int maxRetries = 5)
        {
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"Intento {retry + 1}/{maxRetries} de leer directorios: {path}");
                    return Directory.GetDirectories(path, searchPattern, searchOption);
                }
                catch (UnauthorizedAccessException uae)
                {
                    System.Diagnostics.Debug.WriteLine($"Acceso denegado a {path}: {uae.Message}");
                    
                    // Intentar tomar ownership y reintentar
                    if (retry < maxRetries - 1)
                    {
                        System.Diagnostics.Debug.WriteLine($"Tomando ownership de {path}...");
                        TakeOwnership(path, isDirectory: true);
                        Thread.Sleep(200); // Pausa más larga para que los permisos se apliquen
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"✗ No se pudo acceder a {path} después de {maxRetries} intentos");
                        return new string[0];
                    }
                }
                catch (DirectoryNotFoundException)
                {
                    System.Diagnostics.Debug.WriteLine($"Directorio no encontrado: {path}");
                    return new string[0];
                }
                catch (IOException ioe)
                {
                    System.Diagnostics.Debug.WriteLine($"Error de IO en {path}: {ioe.Message}");
                    if (retry < maxRetries - 1)
                    {
                        Thread.Sleep(200);
                    }
                    else
                    {
                        return new string[0];
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error inesperado en {path}: {ex.Message}");
                    return new string[0];
                }
            }
            return new string[0];
        }

        /// <summary>
        /// Intenta acceder a archivos con reintentos y manejo de permisos
        /// </summary>
        public static string[] GetFilesSafe(string path, string searchPattern = "*.*", SearchOption searchOption = SearchOption.TopDirectoryOnly, int maxRetries = 5)
        {
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"Intento {retry + 1}/{maxRetries} de leer archivos: {path}");
                    return Directory.GetFiles(path, searchPattern, searchOption);
                }
                catch (UnauthorizedAccessException uae)
                {
                    System.Diagnostics.Debug.WriteLine($"Acceso denegado a archivos en {path}: {uae.Message}");
                    
                    // Intentar tomar ownership y reintentar
                    if (retry < maxRetries - 1)
                    {
                        System.Diagnostics.Debug.WriteLine($"Tomando ownership de directorio {path} para acceder a archivos...");
                        TakeOwnership(path, isDirectory: true);
                        Thread.Sleep(200);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"✗ No se pudo acceder a archivos en {path} después de {maxRetries} intentos");
                        return new string[0];
                    }
                }
                catch (DirectoryNotFoundException)
                {
                    System.Diagnostics.Debug.WriteLine($"Directorio no encontrado: {path}");
                    return new string[0];
                }
                catch (IOException ioe)
                {
                    System.Diagnostics.Debug.WriteLine($"Error de IO leyendo archivos en {path}: {ioe.Message}");
                    if (retry < maxRetries - 1)
                    {
                        Thread.Sleep(200);
                    }
                    else
                    {
                        return new string[0];
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error inesperado leyendo archivos en {path}: {ex.Message}");
                    return new string[0];
                }
            }
            return new string[0];
        }

        /// <summary>
        /// Obtiene directorios y archivos de forma recursiva con manejo robusto de permisos
        /// No falla en carpetas protegidas, las omite y continúa
        /// </summary>
        public static void GetAllFilesAndDirectoriesRecursive(
            string rootPath, 
            out string[] allDirectories, 
            out string[] allFiles,
            Action<string> statusCallback = null)
        {
            var directories = new System.Collections.Generic.List<string>();
            var files = new System.Collections.Generic.List<string>();
            var dirsToProcess = new System.Collections.Generic.Queue<string>();

            dirsToProcess.Enqueue(rootPath);

            while (dirsToProcess.Count > 0)
            {
                string currentDir = dirsToProcess.Dequeue();
                
                try
                {
                    statusCallback?.Invoke($"Escaneando: {currentDir}");

                    // Obtener subdirectorios de forma segura
                    string[] subDirs = GetDirectoriesSafe(currentDir, "*", SearchOption.TopDirectoryOnly);
                    foreach (string subDir in subDirs)
                    {
                        directories.Add(subDir);
                        dirsToProcess.Enqueue(subDir);
                    }

                    // Obtener archivos de forma segura
                    string[] currentFiles = GetFilesSafe(currentDir, "*.*", SearchOption.TopDirectoryOnly);
                    files.AddRange(currentFiles);
                }
                catch
                {
                    // Si hay cualquier error, simplemente continuar con el siguiente directorio
                    // No queremos que una carpeta problemática detenga todo el proceso
                    statusCallback?.Invoke($"⚠️ Carpeta omitida (sin acceso): {currentDir}");
                }
            }

            allDirectories = directories.ToArray();
            allFiles = files.ToArray();
        }

        /// <summary>
        /// Copia un archivo con manejo de permisos
        /// </summary>
        public static void CopyFileSafe(string sourceFile, string destinationFile, bool overwrite = true, int maxRetries = 3)
        {
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    // Asegurar que el directorio de destino existe
                    string destDir = Path.GetDirectoryName(destinationFile);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    // Si el archivo de destino existe y está protegido, tomar ownership
                    if (File.Exists(destinationFile))
                    {
                        try
                        {
                            // Intentar remover atributos de solo lectura
                            File.SetAttributes(destinationFile, FileAttributes.Normal);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            TakeOwnership(destinationFile, isDirectory: false);
                            File.SetAttributes(destinationFile, FileAttributes.Normal);
                        }
                    }

                    File.Copy(sourceFile, destinationFile, overwrite);
                    return;
                }
                catch (UnauthorizedAccessException) when (retry < maxRetries - 1)
                {
                    // Intentar tomar ownership del archivo origen
                    TakeOwnership(sourceFile, isDirectory: false);
                    
                    // Si el destino existe, tomar ownership también
                    if (File.Exists(destinationFile))
                    {
                        TakeOwnership(destinationFile, isDirectory: false);
                    }
                    
                    Thread.Sleep(100);
                }
                catch (IOException) when (retry < maxRetries - 1)
                {
                    Thread.Sleep(200);
                }
            }
        }

        /// <summary>
        /// Crea un directorio con manejo de permisos
        /// </summary>
        public static void CreateDirectorySafe(string path, int maxRetries = 3)
        {
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                    }
                    return;
                }
                catch (UnauthorizedAccessException) when (retry < maxRetries - 1)
                {
                    // Intentar tomar ownership del directorio padre
                    string parentDir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                    {
                        TakeOwnership(parentDir, isDirectory: true);
                    }
                    Thread.Sleep(100);
                }
                catch (IOException) when (retry < maxRetries - 1)
                {
                    Thread.Sleep(200);
                }
            }
        }

        /// <summary>
        /// Verifica si la aplicación se está ejecutando con privilegios de administrador
        /// </summary>
        public static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
