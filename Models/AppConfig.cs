using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ComparadorArchivos.Models
{
    /// <summary>
    /// Configuración persistente de la aplicación
    /// </summary>
    public class AppConfig
    {
        // ============================================
        // RUTAS Y HISTORIAL
        // ============================================
        public string LastSourcePath { get; set; }
        public string LastDestinationPath { get; set; }
        public List<string> RecentSourcePaths { get; set; } = new List<string>();
        public List<string> RecentDestinationPaths { get; set; } = new List<string>();
        public int MaxRecentPaths { get; set; } = 10;
        public bool RememberLastPaths { get; set; } = true;
        
        // ============================================
        // TEMA Y APARIENCIA
        // ============================================
        public string LastTheme { get; set; } = "Office 2019 Colorful";
        public bool UseDarkTheme { get; set; } = false;
        public bool UseSystemTheme { get; set; } = false;
        public int FontSize { get; set; } = 9;
        public bool ShowIconsInGrid { get; set; } = true;
        public bool UseAlternateRowColors { get; set; } = true;
        public bool ShowGridLines { get; set; } = true;
        
        // ============================================
        // COMPARACIÓN
        // ============================================
        public bool UseHashComparison { get; set; } = true;
        public bool CompareBySize { get; set; } = true;
        public bool CompareByDate { get; set; } = true;
        public bool CompareByAttributes { get; set; } = false;
        public bool IgnoreHiddenFiles { get; set; } = false;
        public bool IgnoreSystemFiles { get; set; } = false;
        public bool IgnoreEmptyFolders { get; set; } = false;
        public bool CaseSensitiveComparison { get; set; } = false;
        
        // ============================================
        // FILTROS
        // ============================================
        public string FileExtensionFilter { get; set; } = "*.*";
        public string ExcludeExtensions { get; set; } = ""; // Separado por comas
        public string ExcludeFolders { get; set; } = ""; // Separado por comas
        public long MinFileSizeBytes { get; set; } = 0;
        public long MaxFileSizeBytes { get; set; } = 0; // 0 = sin límite
        public bool FilterByDateModified { get; set; } = false;
        public DateTime? MinDateModified { get; set; }
        public DateTime? MaxDateModified { get; set; }
        
        // ============================================
        // RENDIMIENTO
        // ============================================
        public int BufferSizeMB { get; set; } = 16;
        public bool UseMaximumBuffer { get; set; } = true;
        public bool PreventSystemSleep { get; set; } = true;
        public int MaxParallelOperations { get; set; } = 1;
        public bool UseMultiThreading { get; set; } = false;
        public int ThreadPoolSize { get; set; } = 4;
        public bool EnableDiskCache { get; set; } = true;
        public bool PrioritizeLatestFiles { get; set; } = false;
        
        // ============================================
        // VERIFICACIÓN E INTEGRIDAD
        // ============================================
        public bool VerifyIntegrityAfterCopy { get; set; } = true;
        public bool VerifyBeforeDelete { get; set; } = true;
        public bool CreateBackupBeforeOverwrite { get; set; } = false;
        public string BackupFolder { get; set; } = "";
        public bool CheckDiskSpaceBeforeCopy { get; set; } = true;
        public bool ValidateFilePermissions { get; set; } = true;
        
        // ============================================
        // COPIA Y SINCRONIZACIÓN
        // ============================================
        public bool PreserveDateTimeStamps { get; set; } = true;
        public bool PreserveFileAttributes { get; set; } = true;
        public bool PreserveFilePermissions { get; set; } = false;
        public bool OverwriteReadOnly { get; set; } = true;
        public bool SkipIdenticalFiles { get; set; } = true;
        public bool DeleteSourceAfterCopy { get; set; } = false;
        public bool CreateDestinationFolders { get; set; } = true;
        public bool UseRecycleBinForDeletes { get; set; } = true;
        
        // ============================================
        // LOGS Y REPORTES
        // ============================================
        public bool ShowDetailedLogs { get; set; } = true;
        public bool SaveLogsToFile { get; set; } = false;
        public string LogFilePath { get; set; } = "";
        public bool AutoSaveReportAfterOperation { get; set; } = false;
        public string ReportFormat { get; set; } = "HTML"; // HTML, CSV, TXT, JSON
        public bool IncludeTimestampsInLog { get; set; } = true;
        public bool LogOnlyErrors { get; set; } = false;
        public int MaxLogSizeMB { get; set; } = 10;
        
        // ============================================
        // NOTIFICACIONES
        // ============================================
        public bool ShowNotifications { get; set; } = true;
        public bool PlaySoundOnComplete { get; set; } = false;
        public bool ShowPopupOnComplete { get; set; } = true;
        public bool ShowPopupOnError { get; set; } = true;
        public bool MinimizeToTrayDuringOperation { get; set; } = false;
        public bool ShowProgressInTaskbar { get; set; } = true;
        public bool EmailNotificationOnComplete { get; set; } = false;
        public string EmailAddress { get; set; } = "";
        
        // ============================================
        // SEGURIDAD
        // ============================================
        public bool EnableAdminPrivileges { get; set; } = true;
        public bool TakeOwnershipAutomatically { get; set; } = true;
        public bool ConfirmBeforeDelete { get; set; } = true;
        public bool ConfirmBeforeOverwrite { get; set; } = false;
        public bool RequirePasswordForSensitiveOps { get; set; } = false;
        public bool AuditOperations { get; set; } = false;
        
        // ============================================
        // INTERFAZ Y COMPORTAMIENTO
        // ============================================
        public bool ShowFilePreview { get; set; } = true;
        public bool AutoStartComparison { get; set; } = false;
        public bool RememberWindowSize { get; set; } = true;
        public int WindowWidth { get; set; } = 1024;
        public int WindowHeight { get; set; } = 768;
        public bool AlwaysOnTop { get; set; } = false;
        public bool MinimizeToTray { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public bool DoubleClickToOpen { get; set; } = true;
        public bool ShowToolTips { get; set; } = true;
        
        // ============================================
        // AVANZADO
        // ============================================
        public bool EnableDebugMode { get; set; } = false;
        public bool CheckForUpdates { get; set; } = true;
        public bool SendAnonymousStatistics { get; set; } = false;
        public string CustomHashAlgorithm { get; set; } = "SHA256"; // SHA256, MD5, SHA1
        public int RetryAttempts { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 100;
        public bool FollowSymlinks { get; set; } = false;
        public bool ScanNetworkDrives { get; set; } = true;
        public bool UseVSSCopies { get; set; } = false;

        private static string ConfigFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SyncCompareFiles",
            "config.json"
        );

        /// <summary>
        /// Guarda la configuración en disco
        /// </summary>
        public void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(ConfigFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                // Log error but don't throw - config save is not critical
                System.Diagnostics.Debug.WriteLine($"Error saving config: {ex.Message}");
            }
        }

        /// <summary>
        /// Carga la configuración desde disco
        /// </summary>
        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    return JsonSerializer.Deserialize<AppConfig>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading config: {ex.Message}");
            }

            return new AppConfig();
        }
    }
}
