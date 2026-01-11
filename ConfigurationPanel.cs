using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using ComparadorArchivos.Models;

namespace ComparadorArchivos
{
    /// <summary>
    /// Panel de configuración para la aplicación
    /// </summary>
    public partial class ConfigurationPanel : XtraUserControl
    {
        private AppConfig _config;
        public event EventHandler<ThemeChangedEventArgs> ThemeChanged;

        // Controles de Tema
        private GroupControl grpTheme;
        private RadioGroup radioTheme;
        private LabelControl lblThemeInfo;

        // Controles de Rendimiento
        private GroupControl grpPerformance;
        private CheckEdit chkPreventSleep;
        private CheckEdit chkMaxBuffer;
        private CheckEdit chkVerifyIntegrity;
        private SpinEdit spinBufferSize;
        private LabelControl lblBufferSize;

        // Controles de Interfaz
        private GroupControl grpInterface;
        private CheckEdit chkShowDetailedLogs;
        private CheckEdit chkShowFilePreview;

        // Botones
        private SimpleButton btnApply;
        private SimpleButton btnRestoreDefaults;
        private SimpleButton btnOpenAdvanced;

        public ConfigurationPanel()
        {
            _config = AppConfig.Load();
            InitializeComponent();
            LoadConfiguration();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // Panel principal
            this.Name = "ConfigurationPanel";
            this.Size = new System.Drawing.Size(600, 550);

            // Header informativo
            var lblHeader = new LabelControl();
            lblHeader.Text = "Configuración rápida\nAvanzada: %AppData%\\SyncCompareFiles\\config.json";
            lblHeader.Location = new System.Drawing.Point(10, 10);
            lblHeader.AutoSizeMode = LabelAutoSizeMode.None;
            lblHeader.Size = new System.Drawing.Size(580, 40);
            lblHeader.Appearance.Font = new System.Drawing.Font("Tahoma", 8F, System.Drawing.FontStyle.Bold);
            lblHeader.Appearance.ForeColor = System.Drawing.Color.DarkBlue;
            lblHeader.Appearance.Options.UseFont = true;
            lblHeader.Appearance.Options.UseForeColor = true;
            this.Controls.Add(lblHeader);

            int yPos = 70;

            // ===========================================
            // GRUPO: TEMA Y APARIENCIA
            // ===========================================
            grpTheme = new GroupControl();
            grpTheme.Text = "🎨 Tema y Apariencia";
            grpTheme.Location = new System.Drawing.Point(10, yPos);
            grpTheme.Size = new System.Drawing.Size(580, 120);

            radioTheme = new RadioGroup();
            radioTheme.Properties.Items.Add(new DevExpress.XtraEditors.Controls.RadioGroupItem("light", "☀️ Tema Claro (Office 2019 Colorful)"));
            radioTheme.Properties.Items.Add(new DevExpress.XtraEditors.Controls.RadioGroupItem("dark", "🌙 Tema Oscuro (Office 2019 Black)"));
            radioTheme.Location = new System.Drawing.Point(10, 30);
            radioTheme.Size = new System.Drawing.Size(560, 50);
            radioTheme.SelectedIndexChanged += RadioTheme_SelectedIndexChanged;

            lblThemeInfo = new LabelControl();
            lblThemeInfo.Text = "ℹ️ El cambio de tema se aplicará inmediatamente";
            lblThemeInfo.Location = new System.Drawing.Point(10, 90);
            lblThemeInfo.AutoSizeMode = LabelAutoSizeMode.None;
            lblThemeInfo.Size = new System.Drawing.Size(560, 20);
            lblThemeInfo.Appearance.ForeColor = System.Drawing.Color.Gray;

            grpTheme.Controls.Add(radioTheme);
            grpTheme.Controls.Add(lblThemeInfo);
            this.Controls.Add(grpTheme);

            yPos += 130;

            // ===========================================
            // GRUPO: RENDIMIENTO
            // ===========================================
            grpPerformance = new GroupControl();
            grpPerformance.Text = "⚡ Opciones de Rendimiento";
            grpPerformance.Location = new System.Drawing.Point(10, yPos);
            grpPerformance.Size = new System.Drawing.Size(580, 150);

            chkPreventSleep = new CheckEdit();
            chkPreventSleep.Text = "Prevenir suspensión del sistema durante operaciones";
            chkPreventSleep.Location = new System.Drawing.Point(10, 30);
            chkPreventSleep.Size = new System.Drawing.Size(560, 20);

            chkMaxBuffer = new CheckEdit();
            chkMaxBuffer.Text = "Usar buffer máximo (128 MB) para máxima velocidad";
            chkMaxBuffer.Location = new System.Drawing.Point(10, 55);
            chkMaxBuffer.Size = new System.Drawing.Size(560, 20);

            lblBufferSize = new LabelControl();
            lblBufferSize.Text = "Tamaño de buffer personalizado (MB):";
            lblBufferSize.Location = new System.Drawing.Point(10, 85);

            spinBufferSize = new SpinEdit();
            spinBufferSize.Properties.MinValue = 1;
            spinBufferSize.Properties.MaxValue = 128;
            spinBufferSize.Location = new System.Drawing.Point(250, 82);
            spinBufferSize.Size = new System.Drawing.Size(80, 20);
            spinBufferSize.Enabled = false;
            chkMaxBuffer.CheckedChanged += (s, e) => spinBufferSize.Enabled = !chkMaxBuffer.Checked;

            chkVerifyIntegrity = new CheckEdit();
            chkVerifyIntegrity.Text = "Verificar integridad (SHA-256) después de copiar";
            chkVerifyIntegrity.Location = new System.Drawing.Point(10, 115);
            chkVerifyIntegrity.Size = new System.Drawing.Size(560, 20);

            grpPerformance.Controls.Add(chkPreventSleep);
            grpPerformance.Controls.Add(chkMaxBuffer);
            grpPerformance.Controls.Add(lblBufferSize);
            grpPerformance.Controls.Add(spinBufferSize);
            grpPerformance.Controls.Add(chkVerifyIntegrity);
            this.Controls.Add(grpPerformance);

            yPos += 160;

            // ===========================================
            // GRUPO: INTERFAZ
            // ===========================================
            grpInterface = new GroupControl();
            grpInterface.Text = "🖥️ Opciones de Interfaz";
            grpInterface.Location = new System.Drawing.Point(10, yPos);
            grpInterface.Size = new System.Drawing.Size(580, 90);

            chkShowDetailedLogs = new CheckEdit();
            chkShowDetailedLogs.Text = "Mostrar logs detallados durante operaciones";
            chkShowDetailedLogs.Location = new System.Drawing.Point(10, 30);
            chkShowDetailedLogs.Size = new System.Drawing.Size(560, 20);

            chkShowFilePreview = new CheckEdit();
            chkShowFilePreview.Text = "Mostrar vista previa de archivos";
            chkShowFilePreview.Location = new System.Drawing.Point(10, 55);
            chkShowFilePreview.Size = new System.Drawing.Size(560, 20);

            grpInterface.Controls.Add(chkShowDetailedLogs);
            grpInterface.Controls.Add(chkShowFilePreview);
            this.Controls.Add(grpInterface);

            yPos += 100;

            // ===========================================
            // BOTONES
            // ===========================================
            btnRestoreDefaults = new SimpleButton();
            btnRestoreDefaults.Text = "🔄 Restaurar Valores Predeterminados";
            btnRestoreDefaults.Location = new System.Drawing.Point(10, yPos);
            btnRestoreDefaults.Size = new System.Drawing.Size(280, 30);
            btnRestoreDefaults.Click += BtnRestoreDefaults_Click;

            btnApply = new SimpleButton();
            btnApply.Text = "✓ Aplicar Configuración";
            btnApply.Location = new System.Drawing.Point(300, yPos);
            btnApply.Size = new System.Drawing.Size(290, 30);
            btnApply.Appearance.BackColor = System.Drawing.Color.FromArgb(0, 120, 215);
            btnApply.Appearance.ForeColor = System.Drawing.Color.White;
            btnApply.Appearance.Options.UseBackColor = true;
            btnApply.Appearance.Options.UseForeColor = true;
            btnApply.Click += BtnApply_Click;

            yPos += 40;

            btnOpenAdvanced = new SimpleButton();
            btnOpenAdvanced.Text = "⚙️ Configuración avanzada (todas)";
            btnOpenAdvanced.Location = new System.Drawing.Point(10, yPos);
            btnOpenAdvanced.Size = new System.Drawing.Size(580, 30);
            btnOpenAdvanced.Click += BtnOpenAdvanced_Click;

            this.Controls.Add(btnRestoreDefaults);
            this.Controls.Add(btnApply);
            this.Controls.Add(btnOpenAdvanced);

            this.ResumeLayout(false);
        }

        private void LoadConfiguration()
        {
            // Cargar tema
            radioTheme.SelectedIndex = _config.UseDarkTheme ? 1 : 0;

            // Cargar opciones de rendimiento
            chkPreventSleep.Checked = _config.PreventSystemSleep;
            chkMaxBuffer.Checked = _config.UseMaximumBuffer;
            spinBufferSize.Value = _config.BufferSizeMB;
            chkVerifyIntegrity.Checked = _config.VerifyIntegrityAfterCopy;

            // Cargar opciones de interfaz
            chkShowDetailedLogs.Checked = _config.ShowDetailedLogs;
            chkShowFilePreview.Checked = _config.ShowFilePreview;
        }

        private void RadioTheme_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Aplicar tema inmediatamente
            bool isDark = radioTheme.SelectedIndex == 1;
            string themeName = isDark ? "Office 2019 Black" : "Office 2019 Colorful";
            
            try
            {
                DevExpress.LookAndFeel.UserLookAndFeel.Default.SetSkinStyle(themeName);
                
                // Notificar cambio de tema
                ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(themeName, isDark));
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"No se pudo cambiar el tema:\n\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            // Guardar todas las configuraciones
            _config.UseDarkTheme = radioTheme.SelectedIndex == 1;
            _config.LastTheme = _config.UseDarkTheme ? "Office 2019 Black" : "Office 2019 Colorful";
            _config.PreventSystemSleep = chkPreventSleep.Checked;
            _config.UseMaximumBuffer = chkMaxBuffer.Checked;
            _config.BufferSizeMB = (int)spinBufferSize.Value;
            _config.VerifyIntegrityAfterCopy = chkVerifyIntegrity.Checked;
            _config.ShowDetailedLogs = chkShowDetailedLogs.Checked;
            _config.ShowFilePreview = chkShowFilePreview.Checked;
            _config.Save();

            XtraMessageBox.Show(
                "Listo, se guardó la configuración.",
                "Guardado",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void BtnRestoreDefaults_Click(object sender, EventArgs e)
        {
            var result = XtraMessageBox.Show(
                "¿Restaurar todos los valores por defecto?",
                "Confirmar",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                _config = new AppConfig();
                _config.Save();
                LoadConfiguration();

                XtraMessageBox.Show(
                    "Listo, se restauraron los valores por defecto.",
                    "Restaurado",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void BtnOpenAdvanced_Click(object sender, EventArgs e)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SyncCompareFiles");
                string configPath = Path.Combine(folder, "config.json");

                // Asegurar que exista la carpeta y el archivo (se crea al guardar si no existe)
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                if (!File.Exists(configPath))
                {
                    // Guardar el estado actual para generar el archivo
                    _config.Save();
                }

                // Abrir carpeta y archivo
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });

                Process.Start(new ProcessStartInfo
                {
                    FileName = configPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"No se pudo abrir la configuración avanzada:\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public AppConfig GetConfiguration()
        {
            return _config;
        }
    }

    public class ThemeChangedEventArgs : EventArgs
    {
        public string ThemeName { get; set; }
        public bool IsDarkTheme { get; set; }

        public ThemeChangedEventArgs(string themeName, bool isDarkTheme)
        {
            ThemeName = themeName;
            IsDarkTheme = isDarkTheme;
        }
    }
}
