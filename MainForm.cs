using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraSplashScreen;
using ComparadorArchivos.Models;
using ComparadorArchivos.Services;

namespace ComparadorArchivos
{
    public partial class MainForm : DevExpress.XtraEditors.XtraForm
    {
        // Windows API para prevenir suspensión
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;

        private FileComparator _comparator;
        private FileSynchronizer _synchronizer;
        private AppConfig _config;
        private bool _isWorking = false;
        private List<FileComparisonResult> _comparisonResults;
        private CancellationTokenSource _cancellationTokenSource;
        private BindingList<FileComparisonResult> _syncGridDataSource;  // DataSource para grid de sincronización
        private List<PathPair> _pathPairs;  // Lista de pares de rutas
        private int _currentPathPairY = 120;  // Posición Y para el siguiente par de rutas
        // NUEVO: listas de orígenes/destinos y pares generados
        private List<string> _origins = new List<string>();
        private List<string> _destinations = new List<string>();
        private List<PathPair> _generatedPairs = new List<PathPair>();

        // Controles de la página de emparejamiento (creados en tiempo de ejecución)
        private DevExpress.XtraWizard.WizardPage _pairingPage;
        private System.Windows.Forms.ListBox _lstOrigins;
        private System.Windows.Forms.ListBox _lstDestinations;
        private System.Windows.Forms.ListView _lvPairs;
        private System.Windows.Forms.Button _btnAddOrigin;
        private System.Windows.Forms.Button _btnAddDestination;
        private System.Windows.Forms.Button _btnAddPair;
        private System.Windows.Forms.Button _btnRemovePair;
        private System.Windows.Forms.Button _btnGenOneToOne;
        private System.Windows.Forms.Button _btnGenCartesian;

        public MainForm()
        {
            InitializeComponent();
            InitializeServices();
            LoadConfiguration();

            // Configurar evento para mostrar/ocultar label según página
            wizardControl1.SelectedPageChanged += WizardControl1_SelectedPageChanged;
            UpdateHeaderInfoVisibility();
            
            // Agregar botón de configuración en la página de bienvenida
            AddConfigurationButton();
            chkUseHash.Checked = true;
            
            // Inicializar lista de pares de rutas
            _pathPairs = new List<PathPair>();

            // Recalcular límites de scroll cuando el panel cambie de tamaño (ej. al maximizar)
            panelPaths.Resize += (s, e) =>
            {
                UpdateScrollBounds();
            };

            // Configurar página de emparejamiento (dinámica)
            SetupPairingPage();
        }
        
        /// <summary>
        /// Agrega un botón de configuración en la página de bienvenida
        /// </summary>
        private void AddConfigurationButton()
        {
            var btnConfig = new SimpleButton();
            btnConfig.Text = "⚙️ Configuración";
            btnConfig.Location = new System.Drawing.Point(20, 210);
            btnConfig.Size = new System.Drawing.Size(200, 35);
            btnConfig.Appearance.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            btnConfig.Appearance.Options.UseFont = true;
            btnConfig.Click += (s, e) => OpenConfigurationPanel();
            
            // Agregar a la página de bienvenida
            welcomeWizardPage1.Controls.Add(btnConfig);
            
            // Agregar un label informativo sobre el tema actual
            var lblCurrentTheme = new LabelControl();
            lblCurrentTheme.Text = $"🎨 Tema actual: {(_config.UseDarkTheme ? "Oscuro" : "Claro")}";
            lblCurrentTheme.Location = new System.Drawing.Point(20, 255);
            lblCurrentTheme.AutoSizeMode = LabelAutoSizeMode.None;
            lblCurrentTheme.Size = new System.Drawing.Size(400, 20);
            lblCurrentTheme.Appearance.Font = new System.Drawing.Font("Tahoma", 9F);
            lblCurrentTheme.Appearance.ForeColor = System.Drawing.Color.Gray;
            lblCurrentTheme.Appearance.Options.UseFont = true;
            lblCurrentTheme.Appearance.Options.UseForeColor = true;
            
            welcomeWizardPage1.Controls.Add(lblCurrentTheme);
        }

        private void WizardControl1_SelectedPageChanged(object sender, DevExpress.XtraWizard.WizardPageChangedEventArgs e)
        {
            UpdateHeaderInfoVisibility();
        }

        private void UpdateHeaderInfoVisibility()
        {
            // Mostrar lblHeaderInfo en todas las páginas excepto la de bienvenida (página 0)
            lblHeaderInfo.Visible = wizardControl1.SelectedPageIndex != 0;
        }

        private void InitializeServices()
        {
            _comparator = new FileComparator();
            _comparator.ProgressChanged += Comparator_ProgressChanged;
            _comparator.FileProgressChanged += Comparator_FileProgressChanged;  // Progreso de archivo individual
            _comparator.StatusChanged += Comparator_StatusChanged;

            _synchronizer = new FileSynchronizer();
            _synchronizer.ProgressChanged += Synchronizer_ProgressChanged;
            _synchronizer.FileProgressChanged += Synchronizer_FileProgressChanged;  // Progreso de archivo individual
            _synchronizer.StatusChanged += Synchronizer_StatusChanged;
            _synchronizer.LogMessage += Synchronizer_LogMessage;
            _synchronizer.FileCopied += Synchronizer_FileCopied;  // NUEVO: Para actualizar el grid en tiempo real

            _comparisonResults = new List<FileComparisonResult>();
        }

        private void LoadConfiguration()
        {
            _config = AppConfig.Load();

            if (!string.IsNullOrEmpty(_config.LastSourcePath))
                txtSourcePath.Text = _config.LastSourcePath;

            if (!string.IsNullOrEmpty(_config.LastDestinationPath))
                txtDestinationPath.Text = _config.LastDestinationPath;

            // Forzar siempre activados los básicos
            _config.UseHashComparison = true;
            _config.PreventSystemSleep = true;
            _config.UseMaximumBuffer = true;
            _config.Save();

            chkUseHash.Checked = true;
            chkPreventSleep.Checked = true;
            chkMaxBuffer.Checked = true;
        }

        private void SaveConfiguration()
        {
            _config.LastSourcePath = txtSourcePath.Text;
            _config.LastDestinationPath = txtDestinationPath.Text;
            _config.UseHashComparison = chkUseHash.Checked;
            _config.Save();
        }

        // ===========================================
        // PASO 1: Selección de carpetas
        // ===========================================

        private void btnSelectSource_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Seleccione la carpeta de origen";
                dialog.SelectedPath = txtSourcePath.Text;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtSourcePath.Text = dialog.SelectedPath;
                    ValidateFastCopyButton();
                }
            }
        }

        private void btnSelectDestination_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Seleccione la carpeta de destino";
                if (!string.IsNullOrEmpty(txtDestinationPath.Text))
                    dialog.SelectedPath = txtDestinationPath.Text;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtDestinationPath.Text = dialog.SelectedPath;
                    ValidateFastCopyButton();
                }
            }
        }

        private void ValidateFastCopyButton()
        {
            // Habilitar botón si al menos un par de rutas es válido
            bool hasValidMainPair = !string.IsNullOrEmpty(txtSourcePath.Text) &&
                                   !string.IsNullOrEmpty(txtDestinationPath.Text) &&
                                   Directory.Exists(txtSourcePath.Text) &&
                                   Directory.Exists(txtDestinationPath.Text);
            
            bool hasValidAdditionalPairs = _pathPairs.Any(p => p.IsValid);
            
            btnFastCopy.Enabled = hasValidMainPair || hasValidAdditionalPairs;
        }

        private void btnSwapPaths_Click(object sender, EventArgs e)
        {
            // Intercambiar las rutas de origen y destino
            string tempPath = txtSourcePath.Text;
            txtSourcePath.Text = txtDestinationPath.Text;
            txtDestinationPath.Text = tempPath;

            // Intercambiar también los textos de las etiquetas
            string tempLabel = labelControl1.Text;
            labelControl1.Text = labelControl2.Text;
            labelControl2.Text = tempLabel;

            XtraMessageBox.Show(
                $"Listo, las rutas se intercambiaron:\n\n{labelControl1.Text} {txtSourcePath.Text}\n{labelControl2.Text} {txtDestinationPath.Text}",
                "Rutas intercambiadas",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            ValidateFastCopyButton();
        }

        private void btnAddPath_Click(object sender, EventArgs e)
        {
            // Crear un nuevo par de rutas
            var newPathPair = new PathPair();
            _pathPairs.Add(newPathPair);
            
            // Crear controles para el nuevo par de rutas
            AddPathPairControls(newPathPair);
            
            // Actualizar el tamaño del panel
            UpdatePanelSize();
            
            // Actualizar contador de rutas
            UpdatePathCountLabel();
            
            // Forzar el redibujado del panel
            panelPaths.Refresh();

            // Asegurar que el scroll tenga los límites correctos
            UpdateScrollBounds();
        }
        
        private void UpdatePathCountLabel()
        {
            int totalPairs = _pathPairs.Count;
            int validPairs = _pathPairs.Count(p => p.IsValid);
            
            if (totalPairs == 0)
            {
                lblPathCount.Text = "Rutas adicionales: 0";
                lblPathCount.Appearance.ForeColor = System.Drawing.Color.Gray;
            }
            else if (validPairs == totalPairs)
            {
                lblPathCount.Text = $"Rutas adicionales: {totalPairs} (todas válidas)";
                lblPathCount.Appearance.ForeColor = System.Drawing.Color.Green;
            }
            else
            {
                lblPathCount.Text = $"Rutas adicionales: {totalPairs} ({validPairs} válidas)";
                lblPathCount.Appearance.ForeColor = System.Drawing.Color.Orange;
            }
        }
        
        private int GetPanelContentBottom()
        {
            int maxBottom = 0;
            foreach (System.Windows.Forms.Control c in panelPaths.Controls)
            {
                if (c.Bottom > maxBottom)
                    maxBottom = c.Bottom;
            }
            return maxBottom;
        }

        private int GetStaticContentBottom()
        {
            int maxBottom = 0;
            foreach (System.Windows.Forms.Control c in panelPaths.Controls)
            {
                // Controles iniciales no tienen Tag
                if (c.Tag == null && c.Bottom > maxBottom)
                    maxBottom = c.Bottom;
            }
            // Fallback si no hay estáticos
            if (maxBottom == 0) maxBottom = 120;
            return maxBottom;
        }

        // Ajusta los límites del scroll según el contenido actual del panel
        private void UpdateScrollBounds()
        {
            // Altura real del contenido
            int contentBottom = 0;
            foreach (System.Windows.Forms.Control c in panelPaths.Controls)
            {
                if (c.Bottom > contentBottom)
                    contentBottom = c.Bottom;
            }
            int contentHeight = Math.Max(0, contentBottom + 6);

            // Fijar área mínima de scroll EXACTA al contenido
            panelPaths.AutoScrollMinSize = new System.Drawing.Size(0, contentHeight);

            // Clampear la posición actual del scroll dentro del nuevo rango
            int viewHeight = Math.Max(0, panelPaths.ClientSize.Height);
            int maxOffset = Math.Max(0, contentHeight - viewHeight);
            int currentOffset = Math.Max(0, -panelPaths.AutoScrollPosition.Y); // AutoScrollPosition es negativo
            if (currentOffset > maxOffset)
            {
                panelPaths.AutoScrollPosition = new System.Drawing.Point(0, maxOffset);
            }
        }

        // ================================
        // Emparejamiento Orígenes/Destinos
        // ================================
        private void SetupPairingPage()
        {
            _pairingPage = new DevExpress.XtraWizard.WizardPage();
            _pairingPage.Text = "Emparejar Rutas";
            _pairingPage.DescriptionText = "Crea pares Origen → Destino (uno-a-muchos, muchos-a-uno o cartesiano)";

            // Insertar la página después de la Selección de Rutas
            int insertIndex = Math.Max(1, wizardControl1.Pages.IndexOf(wizardPage1) + 1);
            wizardControl1.Pages.Insert(insertIndex, _pairingPage);

            // Listas
            _lstOrigins = new System.Windows.Forms.ListBox(){ Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left, Location = new System.Drawing.Point(20, 40), Size = new System.Drawing.Size(260, 320) };
            _lstDestinations = new System.Windows.Forms.ListBox(){ Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right, Location = new System.Drawing.Point(488, 40), Size = new System.Drawing.Size(260, 320) };

            var lblO = new LabelControl(){ Text = "Orígenes", Location = new System.Drawing.Point(20, 20) };
            var lblD = new LabelControl(){ Text = "Destinos", Location = new System.Drawing.Point(488, 20) };

            // Pairs list
            _lvPairs = new System.Windows.Forms.ListView(){ Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, Location = new System.Drawing.Point(20, 370), Size = new System.Drawing.Size(728, 80), View = System.Windows.Forms.View.Details, FullRowSelect = true };
            _lvPairs.Columns.Add("Origen", 360);
            _lvPairs.Columns.Add("Destino", 360);

            // Botones
            _btnAddOrigin = new System.Windows.Forms.Button(){ Text = "+ Origen", Location = new System.Drawing.Point(20, 460), Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Size = new System.Drawing.Size(100, 25) };
            _btnAddDestination = new System.Windows.Forms.Button(){ Text = "+ Destino", Location = new System.Drawing.Point(488, 460), Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Size = new System.Drawing.Size(100, 25) };
            _btnAddPair = new System.Windows.Forms.Button(){ Text = "Agregar Par →", Location = new System.Drawing.Point(320, 100), Size = new System.Drawing.Size(140, 30) };
            _btnRemovePair = new System.Windows.Forms.Button(){ Text = "Quitar Par", Location = new System.Drawing.Point(320, 140), Size = new System.Drawing.Size(140, 30) };
            _btnGenOneToOne = new System.Windows.Forms.Button(){ Text = "Generar 1:1", Location = new System.Drawing.Point(320, 190), Size = new System.Drawing.Size(140, 30) };
            _btnGenCartesian = new System.Windows.Forms.Button(){ Text = "Generar Cartesiano", Location = new System.Drawing.Point(320, 230), Size = new System.Drawing.Size(140, 30) };

            _btnAddOrigin.Click += (s,e)=>AddOrigin();
            _btnAddDestination.Click += (s,e)=>AddDestination();
            _btnAddPair.Click += (s,e)=>AddSelectedPair();
            _btnRemovePair.Click += (s,e)=>RemoveSelectedPair();
            _btnGenOneToOne.Click += (s,e)=>GenerateOneToOne();
            _btnGenCartesian.Click += (s,e)=>GenerateCartesian();

            _pairingPage.Controls.Add(lblO);
            _pairingPage.Controls.Add(lblD);
            _pairingPage.Controls.Add(_lstOrigins);
            _pairingPage.Controls.Add(_lstDestinations);
            _pairingPage.Controls.Add(_lvPairs);
            _pairingPage.Controls.Add(_btnAddOrigin);
            _pairingPage.Controls.Add(_btnAddDestination);
            _pairingPage.Controls.Add(_btnAddPair);
            _pairingPage.Controls.Add(_btnRemovePair);
            _pairingPage.Controls.Add(_btnGenOneToOne);
            _pairingPage.Controls.Add(_btnGenCartesian);
        }

        private void RefreshPairsView()
        {
            _lvPairs.BeginUpdate();
            _lvPairs.Items.Clear();
            foreach (var p in _generatedPairs)
            {
                var item = new System.Windows.Forms.ListViewItem(new[]{p.SourcePath, p.DestinationPath});
                _lvPairs.Items.Add(item);
            }
            _lvPairs.EndUpdate();
        }

        private void AddOrigin()
        {
            using (var dlg = new FolderBrowserDialog(){ Description = "Seleccione carpeta de origen" })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    if (!_origins.Contains(dlg.SelectedPath))
                        _origins.Add(dlg.SelectedPath);
                    _lstOrigins.DataSource = null; _lstOrigins.DataSource = _origins;
                }
            }
        }

        private void AddDestination()
        {
            using (var dlg = new FolderBrowserDialog(){ Description = "Seleccione carpeta de destino" })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    if (!_destinations.Contains(dlg.SelectedPath))
                        _destinations.Add(dlg.SelectedPath);
                    _lstDestinations.DataSource = null; _lstDestinations.DataSource = _destinations;
                }
            }
        }

        private void AddSelectedPair()
        {
            if (_lstOrigins.SelectedItem is string o && _lstDestinations.SelectedItem is string d)
            {
                if (!_generatedPairs.Any(p=>p.SourcePath==o && p.DestinationPath==d))
                {
                    _generatedPairs.Add(new PathPair(o,d));
                    RefreshPairsView();
                }
            }
        }

        private void RemoveSelectedPair()
        {
            if (_lvPairs.SelectedItems.Count>0)
            {
                var src = _lvPairs.SelectedItems[0].SubItems[0].Text;
                var dst = _lvPairs.SelectedItems[0].SubItems[1].Text;
                var found = _generatedPairs.FirstOrDefault(p=>p.SourcePath==src && p.DestinationPath==dst);
                if (found!=null) { _generatedPairs.Remove(found); RefreshPairsView(); }
            }
        }

        private void GenerateOneToOne()
        {
            int count = Math.Min(_origins.Count, _destinations.Count);
            for (int i=0;i<count;i++)
            {
                var o = _origins[i]; var d = _destinations[i];
                if (!_generatedPairs.Any(p=>p.SourcePath==o && p.DestinationPath==d))
                    _generatedPairs.Add(new PathPair(o,d));
            }
            RefreshPairsView();
        }

        private void GenerateCartesian()
        {
            foreach (var o in _origins)
                foreach (var d in _destinations)
                    if (!_generatedPairs.Any(p=>p.SourcePath==o && p.DestinationPath==d))
                        _generatedPairs.Add(new PathPair(o,d));
            RefreshPairsView();
        }

        private void AddPathPairControls(PathPair pathPair)
        {
            // Colocar inmediatamente después del último control existente
            int yPosition = GetPanelContentBottom() + 6;
            
            // Separador visual (sin espacio antes)
            var separator = new DevExpress.XtraEditors.LabelControl();
            separator.Text = "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━";
            separator.Location = new System.Drawing.Point(14, yPosition);
            separator.Size = new System.Drawing.Size(720, 12);
            separator.Appearance.ForeColor = System.Drawing.Color.DarkGray;
            separator.Appearance.Font = new System.Drawing.Font("Tahoma", 7F);
            separator.Appearance.Options.UseFont = true;
            separator.Appearance.Options.UseForeColor = true;
            separator.Tag = pathPair.Id;
            panelPaths.Controls.Add(separator);
            yPosition += 16;
            
            // Label "Desde dónde"
            var lblSource = new DevExpress.XtraEditors.LabelControl();
            lblSource.Text = "Desde dónde:";
            lblSource.Location = new System.Drawing.Point(14, yPosition);
            lblSource.Size = new System.Drawing.Size(115, 14);
            lblSource.Appearance.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold);
            lblSource.Appearance.Options.UseFont = true;
            lblSource.Tag = pathPair.Id;
            panelPaths.Controls.Add(lblSource);
            yPosition += 20;
            
            // TextBox origen
            var txtSource = new DevExpress.XtraEditors.TextEdit();
            txtSource.Location = new System.Drawing.Point(14, yPosition);
            txtSource.Size = new System.Drawing.Size(panelPaths.Width - 200, 20); // Ancho dinámico
            txtSource.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            txtSource.Tag = pathPair.Id;
            txtSource.EditValueChanged += (s, e) => 
            {
                pathPair.SourcePath = txtSource.Text;
                ValidateFastCopyButton();
                UpdatePathCountLabel();
            };
            panelPaths.Controls.Add(txtSource);
            
            // Botón seleccionar origen
            var btnSource = new DevExpress.XtraEditors.SimpleButton();
            btnSource.Text = "Seleccionar...";
            btnSource.Location = new System.Drawing.Point(panelPaths.Width - 190, yPosition);
            btnSource.Size = new System.Drawing.Size(120, 24);
            btnSource.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            btnSource.Tag = pathPair.Id;
            btnSource.Click += (s, e) => 
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "Seleccione la carpeta de origen";
                    dialog.SelectedPath = txtSource.Text;
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        txtSource.Text = dialog.SelectedPath;
                        pathPair.SourcePath = dialog.SelectedPath;
                        ValidateFastCopyButton();
                        UpdatePathCountLabel();
                    }
                }
            };
            panelPaths.Controls.Add(btnSource);
            yPosition += 30;
            
            // Label "Hacia dónde"
            var lblDest = new DevExpress.XtraEditors.LabelControl();
            lblDest.Text = "Hacia dónde:";
            lblDest.Location = new System.Drawing.Point(14, yPosition);
            lblDest.Size = new System.Drawing.Size(122, 14);
            lblDest.Appearance.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold);
            lblDest.Appearance.Options.UseFont = true;
            lblDest.Tag = pathPair.Id;
            panelPaths.Controls.Add(lblDest);
            yPosition += 20;
            
            // TextBox destino
            var txtDest = new DevExpress.XtraEditors.TextEdit();
            txtDest.Location = new System.Drawing.Point(14, yPosition);
            txtDest.Size = new System.Drawing.Size(panelPaths.Width - 200, 20); // Ancho dinámico
            txtDest.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            txtDest.Tag = pathPair.Id;
            txtDest.EditValueChanged += (s, e) => 
            {
                pathPair.DestinationPath = txtDest.Text;
                ValidateFastCopyButton();
                UpdatePathCountLabel();
            };
            panelPaths.Controls.Add(txtDest);
            
            // Botón seleccionar destino
            var btnDest = new DevExpress.XtraEditors.SimpleButton();
            btnDest.Text = "Seleccionar...";
            btnDest.Location = new System.Drawing.Point(panelPaths.Width - 190, yPosition);
            btnDest.Size = new System.Drawing.Size(120, 24);
            btnDest.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            btnDest.Tag = pathPair.Id;
            btnDest.Click += (s, e) => 
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "Seleccione la carpeta de destino";
                    dialog.SelectedPath = txtDest.Text;
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        txtDest.Text = dialog.SelectedPath;
                        pathPair.DestinationPath = dialog.SelectedPath;
                        ValidateFastCopyButton();
                        UpdatePathCountLabel();
                    }
                }
            };
            panelPaths.Controls.Add(btnDest);
            
            // Botón intercambiar
            var btnSwap = new DevExpress.XtraEditors.SimpleButton();
            btnSwap.Text = "⇅";
            btnSwap.Location = new System.Drawing.Point(panelPaths.Width - 65, yPosition - 30);
            btnSwap.Size = new System.Drawing.Size(65, 21);
            btnSwap.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            btnSwap.Appearance.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
            btnSwap.Appearance.Options.UseFont = true;
            btnSwap.ToolTip = "Intercambiar carpetas";
            btnSwap.Tag = pathPair.Id;
            btnSwap.Click += (s, e) => 
            {
                string temp = txtSource.Text;
                txtSource.Text = txtDest.Text;
                txtDest.Text = temp;
                pathPair.SourcePath = txtSource.Text;
                pathPair.DestinationPath = txtDest.Text;
                ValidateFastCopyButton();
            };
            panelPaths.Controls.Add(btnSwap);
            
            // Botón eliminar
            var btnRemove = new DevExpress.XtraEditors.SimpleButton();
            btnRemove.Text = "✖";
            btnRemove.Location = new System.Drawing.Point(panelPaths.Width - 65, yPosition);
            btnRemove.Size = new System.Drawing.Size(65, 24);
            btnRemove.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            btnRemove.Appearance.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            btnRemove.Appearance.ForeColor = System.Drawing.Color.Red;
            btnRemove.Appearance.Options.UseFont = true;
            btnRemove.Appearance.Options.UseForeColor = true;
            btnRemove.ToolTip = "Eliminar este par de rutas";
            btnRemove.Tag = pathPair.Id;
            btnRemove.Click += (s, e) => RemovePathPair(pathPair.Id);
            panelPaths.Controls.Add(btnRemove);
            
            // Calcular correctamente la siguiente posición Y 
            // yPosition está en el TextBox destino, sumar su altura (20) + altura del botón (24) + espacio (6)
            _currentPathPairY = yPosition + 50;

            // Actualizar los límites del scroll tras insertar controles
            UpdateScrollBounds();
        }
        
        private void RemovePathPair(Guid pathPairId)
        {
            // Remover el par de rutas de la lista
            var pathPair = _pathPairs.FirstOrDefault(p => p.Id == pathPairId);
            if (pathPair != null)
            {
                _pathPairs.Remove(pathPair);
            }
            
            // Remover todos los controles asociados a este par de rutas
            var controlsToRemove = panelPaths.Controls.Cast<System.Windows.Forms.Control>()
                .Where(c => c.Tag != null && c.Tag.Equals(pathPairId))
                .ToList();
            
            foreach (var control in controlsToRemove)
            {
                panelPaths.Controls.Remove(control);
                control.Dispose();
            }
            
            // Reorganizar los controles restantes
            ReorganizePathPairControls();
            ValidateFastCopyButton();
            UpdatePathCountLabel();
        }
        
        private void ReorganizePathPairControls()
        {
            // Suspender el layout para evitar parpadeos
            panelPaths.SuspendLayout();
            
            try
            {
                // Reiniciar la posición Y inmediatamente después del contenido estático (primer par)
                _currentPathPairY = GetStaticContentBottom() + 6;
                
                // Reorganizar todos los pares de rutas adicionales
                foreach (var pathPair in _pathPairs)
                {
                    var controls = panelPaths.Controls.Cast<System.Windows.Forms.Control>()
                        .Where(c => c.Tag != null && c.Tag.Equals(pathPair.Id))
                        .OrderBy(c => c.Top)
                        .ToList();
                    
                    if (controls.Count == 0) continue;
                    
                    int yPosition = _currentPathPairY;
                    
                    // Reorganizar cada control del par (sin espacio antes)
                    foreach (var control in controls)
                    {
                        control.Top = yPosition;
                        
                        // Calcular el siguiente Y basado en el tipo de control
                        if (control is DevExpress.XtraEditors.LabelControl lbl && lbl.Text.Contains("━"))
                        {
                            // Separador
                            yPosition += 16;
                        }
                        else if (control is DevExpress.XtraEditors.LabelControl)
                        {
                            // Labels de "Desde dónde" o "Hacia dónde"
                            yPosition += 20;
                        }
                        else if (control is DevExpress.XtraEditors.TextEdit)
                        {
                            // TextBox
                            yPosition += 30;
                        }
                        else if (control is DevExpress.XtraEditors.SimpleButton btn)
                        {
                            // Botones - no incrementar Y aquí porque están al lado
                            if (btn.Text == "✖")
                            {
                                // Último control del par, sin espacio adicional
                            }
                        }
                    }
                    
                    _currentPathPairY = yPosition;
                }
                // Actualizar límites de scroll y redibujar
                UpdateScrollBounds();
                panelPaths.Refresh();
            }
            finally
            {
                panelPaths.ResumeLayout();
            }
        }
        
        private void UpdatePanelSize()
        {
            // El panel se ajusta automáticamente con Anchor (Top, Bottom, Left, Right)
            // No necesitamos establecer el tamaño manualmente
        }
        
        private List<PathPair> GetAllPathPairs()
        {
            var allPairs = new List<PathPair>();

            // Si el usuario generó pares en la página de emparejamiento, usar esa lista
            if (_generatedPairs.Count > 0)
            {
                allPairs.AddRange(_generatedPairs.Where(p=>p.IsValid));
                return allPairs;
            }
            // Agregar el par de rutas principal
            if (!string.IsNullOrWhiteSpace(txtSourcePath.Text) && !string.IsNullOrWhiteSpace(txtDestinationPath.Text))
            {
                allPairs.Add(new PathPair(txtSourcePath.Text, txtDestinationPath.Text));
            }
            
            // Agregar los pares de rutas adicionales
            allPairs.AddRange(_pathPairs.Where(p => p.IsValid));
            
            return allPairs;
        }

        private async void btnFastCopy_Click(object sender, EventArgs e)
        {
            // Obtener todos los pares de rutas válidos
            var allPathPairs = GetAllPathPairs();
            
            if (allPathPairs.Count == 0)
            {
                XtraMessageBox.Show("Necesitas seleccionar al menos un par de carpetas (origen y destino).", "Falta información",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Construir mensaje de confirmación
            string message = allPathPairs.Count == 1 
                ? $"¿Copiar todo de aquí:\n{allPathPairs[0].SourcePath}\n\na aquí:\n{allPathPairs[0].DestinationPath}?\n\nSe usará el buffer máximo (128 MB) para ir lo más rápido posible."
                : $"¿Copiar archivos de {allPathPairs.Count} pares de carpetas?\n\n" + 
                  string.Join("\n", allPathPairs.Select((p, i) => $"{i + 1}. {p.SourcePath} → {p.DestinationPath}")) +
                  "\n\nSe usará el buffer máximo (128 MB) para ir lo más rápido posible.";

            var result = XtraMessageBox.Show(
                message,
                "Confirmar copia rápida",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                // Prevenir suspensión del sistema
                PreventSystemSleep();

                // Mostrar controles de progreso
                progressBarCopySpeed.Visible = true;
                progressBarBufferOptimization.Visible = true;
                lblCopySpeedInfo.Visible = true;
                lblBufferInfo.Visible = true;
                btnFastCopy.Enabled = false;
                btnSelectSource.Enabled = false;
                btnSelectDestination.Enabled = false;
                btnAddPath.Enabled = false;

                lblCopySpeedInfo.Text = "🚀 Velocidad: 0 MB/s  |  ⚙️ Buffer: 128 MB";
                lblBufferInfo.Text = "📁 Archivo Actual: Iniciando...";
                progressBarCopySpeed.Position = 0;
                progressBarBufferOptimization.Position = 0;

                // Ejecutar copia asíncrona para todos los pares de rutas
                await Task.Run(async () =>
                {
                    for (int i = 0; i < allPathPairs.Count; i++)
                    {
                        var pathPair = allPathPairs[i];
                        
                        Invoke(new Action(() =>
                        {
                            lblBufferInfo.Text = $"📁 Procesando par {i + 1} de {allPathPairs.Count}: {Path.GetFileName(pathPair.SourcePath)}";
                        }));
                        
                        await FastCopyDirectory(pathPair.SourcePath, pathPair.DestinationPath, _cancellationTokenSource.Token);
                    }
                });

                XtraMessageBox.Show(
                    $"¡Listo! Todos los archivos de {allPathPairs.Count} par(es) de carpetas se copiaron correctamente.",
                    "Copia completada",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                XtraMessageBox.Show("Cancelaste la operación.", "Cancelado",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (IOException ex) when (ex.Message.Contains("DOCUMENTO EN USO"))
            {
                XtraMessageBox.Show(
                    $"No se pudo copiar porque el archivo está en uso:\n\n{ex.Message}",
                    "Archivo en uso",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"Hubo un problema al copiar:\n\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // Permitir suspensión del sistema nuevamente
                AllowSystemSleep();

                btnFastCopy.Enabled = true;
                btnSelectSource.Enabled = true;
                btnSelectDestination.Enabled = true;
                btnAddPath.Enabled = true;
                progressBarCopySpeed.Visible = true;
                progressBarBufferOptimization.Visible = true;
                lblCopySpeedInfo.Visible = true;
                lblBufferInfo.Visible = true;
            }
        }

        private async Task FastCopyDirectory(string sourceDir, string destDir, CancellationToken cancellationToken)
        {
            // Buffer MÁXIMO siempre: 128 MB
            int bufferSize = 128 * 1024 * 1024;
            long totalBytesCopied = 0;
            var startTime = DateTime.Now;

            // Obtener todos los archivos
            var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
            int currentFile = 0;

            foreach (var sourceFile in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string relativePath = GetRelativePathCompat(sourceDir, sourceFile);
                string destFile = Path.Combine(destDir, relativePath);
                string destFileDir = Path.GetDirectoryName(destFile);

                // Crear directorio si no existe
                if (!Directory.Exists(destFileDir))
                    Directory.CreateDirectory(destFileDir);

                // Copiar archivo con buffer MÁXIMO
                var fileInfo = new FileInfo(sourceFile);
                string fileName = Path.GetFileName(sourceFile);

                // Actualizar UI ANTES de copiar
                Invoke(new Action(() =>
                {
                    // Barra superior: progreso general
                    progressBarCopySpeed.Position = (int)((double)currentFile / files.Length * 100);

                    // Mostrar archivo actual
                    lblBufferInfo.Text = $"📁 Archivo Actual: {fileName}";

                    // Resetear barra de archivo individual
                    progressBarBufferOptimization.Position = 0;
                }));

                // Copiar archivo con progreso
                await FastCopyFileWithProgress(sourceFile, destFile, bufferSize, files.Length, currentFile, startTime, totalBytesCopied, cancellationToken);

                totalBytesCopied += fileInfo.Length;
                currentFile++;

                // Actualizar velocidad después de copiar
                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                if (elapsed > 0.1)
                {
                    double speedMBps = (totalBytesCopied / (1024.0 * 1024.0)) / elapsed;

                    Invoke(new Action(() =>
                    {
                        lblCopySpeedInfo.Text = $"🚀 Velocidad: {speedMBps:F1} MB/s  |  ⚙️ Buffer: 128 MB";

                        // Marcar archivo como completado (100%)
                        progressBarBufferOptimization.Position = 100;
                    }));
                }
            }
        }

        private async Task FastCopyFileWithProgress(string sourceFile, string destFile, int bufferSize, int totalFiles, int currentFileIndex, DateTime startTime, long totalBytesCopied, CancellationToken cancellationToken)
        {
            try
            {
                var fileInfo = new FileInfo(sourceFile);
                long fileSize = fileInfo.Length;
                long bytesCopied = 0;

                using (var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous))
                using (var destStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.SequentialScan | FileOptions.WriteThrough | FileOptions.Asynchronous))
                {
                    byte[] buffer = new byte[bufferSize];
                    int bytesRead;

                    while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await destStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                        bytesCopied += bytesRead;

                        // Actualizar progreso del archivo individual
                        if (fileSize > 0)
                        {
                            int fileProgress = (int)((double)bytesCopied / fileSize * 100);

                            Invoke(new Action(() =>
                            {
                                progressBarBufferOptimization.Position = fileProgress;
                            }));
                        }
                    }
                }

                // Preservar fechas
                File.SetCreationTime(destFile, File.GetCreationTime(sourceFile));
                File.SetLastWriteTime(destFile, File.GetLastWriteTime(sourceFile));
            }
            catch (IOException ex) when (ex.Message.Contains("being used by another process") || ex.HResult == -2147024864)
            {
                // Archivo en uso por otro proceso
                string fileName = Path.GetFileName(sourceFile);

                Invoke(new Action(() =>
                {
                    lblBufferInfo.Text = $" DOCUMENTO EN USO: {fileName}";
                    progressBarBufferOptimization.Position = 0;
                }));

                throw new IOException($" DOCUMENTO EN USO: {fileName}\n\nEl archivo está siendo usado por otra aplicación.\nCierre el archivo e intente nuevamente.", ex);
            }
        }

        private async Task FastCopyFile(string sourceFile, string destFile, int bufferSize, CancellationToken cancellationToken)
        {
            try
            {
                using (var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous))
                using (var destStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.SequentialScan | FileOptions.WriteThrough | FileOptions.Asynchronous))
                {
                    await sourceStream.CopyToAsync(destStream, bufferSize, cancellationToken);
                }

                // Preservar fechas
                File.SetCreationTime(destFile, File.GetCreationTime(sourceFile));
                File.SetLastWriteTime(destFile, File.GetLastWriteTime(sourceFile));
            }
            catch (IOException ex) when (ex.Message.Contains("being used by another process") || ex.HResult == -2147024864)
            {
                string fileName = Path.GetFileName(sourceFile);
                throw new IOException($"⚠️ DOCUMENTO EN USO: {fileName}\n\nEl archivo está siendo usado por otra aplicación.\nCierre el archivo e intente nuevamente.", ex);
            }
        }

        private void wizardControl1_NextClick(object sender, DevExpress.XtraWizard.WizardCommandButtonClickEventArgs e)
        {
            // Validación al avanzar desde el paso 1
            if (wizardControl1.SelectedPage == wizardPage1)
            {
                if (string.IsNullOrWhiteSpace(txtSourcePath.Text) ||
                    string.IsNullOrWhiteSpace(txtDestinationPath.Text))
                {
                    XtraMessageBox.Show(
                        "Primero tienes que seleccionar las dos carpetas.",
                        "Falta información",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    e.Handled = true;
                    return;
                }

                if (!System.IO.Directory.Exists(txtSourcePath.Text))
                {
                    XtraMessageBox.Show(
                        "La carpeta de origen no existe o no se encuentra.",
                        "Carpeta no encontrada",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    e.Handled = true;
                    return;
                }

                if (!System.IO.Directory.Exists(txtDestinationPath.Text))
                {
                    XtraMessageBox.Show(
                        "La carpeta de destino no existe o no se encuentra.",
                        "Carpeta no encontrada",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    e.Handled = true;
                    return;
                }

                SaveConfiguration();
            }

            // Al avanzar a la pestaña de Sincronización (wizardPage3)
            if (wizardControl1.SelectedPage == wizardPage2 && wizardControl1.SelectedPageIndex == 2)
            {
                // Filtrar y mostrar SOLO items con problemas (Falta o Diferente)
                if (_comparisonResults != null && _comparisonResults.Count > 0)
                {
                    var itemsConProblemas = _comparisonResults
                        .Where(item => item.Status == ComparisonStatus.Missing || item.Status == ComparisonStatus.Different)
                        .ToList();

                    if (itemsConProblemas.Count == 0)
                    {
                        // Mostrar mensaje pero permitir avanzar para ver que todo está bien
                        XtraMessageBox.Show(
                            "Todo está sincronizado. No hay archivos diferentes ni faltantes.",
                            "Sin cambios",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);

                        // Mostrar grid vacío con mensaje
                        lblSyncStatus.Text = "Todos los archivos están sincronizados";
                        _syncGridDataSource = null;
                        gridControl2.DataSource = null;
                    }
                    else
                    {
                        // IMPORTANTE: Crear BindingList y guardarlo en campo de clase
                        // para que las actualizaciones se reflejen en el grid
                        _syncGridDataSource = new BindingList<FileComparisonResult>(itemsConProblemas);

                        // ✅ PRE-SELECCIONAR todos los items con checkbox
                        foreach (var item in _syncGridDataSource)
                        {
                            item.Selected = true;
                        }

                        gridControl2.DataSource = _syncGridDataSource;

                        // ✅ Configurar grid de sincronización con checkbox + hash + fechas
                        FormatSyncGrid();

                        int faltantes = itemsConProblemas.Count(x => x.Status == ComparisonStatus.Missing);
                        int diferentes = itemsConProblemas.Count(x => x.Status == ComparisonStatus.Different);

                        lblSyncStatus.Text = $"Archivos pendientes: {itemsConProblemas.Count} ({faltantes} faltantes, {diferentes} diferentes) - Todos seleccionados";
                    }
                }
            }
        }

        // ===========================================
        // PASO 2: Análisis de archivos
        // ===========================================

        private async void btnAnalyze_Click(object sender, EventArgs e)
        {
            try
            {
                // Obtener todos los pares de rutas válidos
                var allPathPairs = GetAllPathPairs();
                
                if (allPathPairs.Count == 0)
                {
                    XtraMessageBox.Show("Necesitas seleccionar al menos un par de carpetas (origen y destino).", "Falta información",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Prevenir suspensión del sistema
                PreventSystemSleep();

                btnAnalyze.Enabled = false;
                btnCancelAnalysis.Enabled = true;
                progressBarAnalysis.Position = 0;
                gridControl1.DataSource = null;

                _cancellationTokenSource = new CancellationTokenSource();
                _comparisonResults = new List<FileComparisonResult>();

                // Procesar cada par de rutas
                for (int i = 0; i < allPathPairs.Count; i++)
                {
                    var pathPair = allPathPairs[i];
                    
                    lblStatus.Text = $"Analizando par {i + 1} de {allPathPairs.Count}: {Path.GetFileName(pathPair.SourcePath)} → {Path.GetFileName(pathPair.DestinationPath)}";
                    
                    var results = await _comparator.CompareDirectoriesAsync(
                        pathPair.SourcePath,
                        pathPair.DestinationPath,
                        chkUseHash.Checked,
                        _cancellationTokenSource.Token);
                    
                    _comparisonResults.AddRange(results);
                }

                gridControl1.DataSource = _comparisonResults;

                // Aplicar formato al grid
                FormatGrid();

                // Actualizar estadísticas
                UpdateStatistics();

                lblStatus.Text = $"Análisis completado. {_comparisonResults.Count} archivos procesados de {allPathPairs.Count} par(es) de carpetas.";
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Análisis cancelado por el usuario.";
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show(
                    $"Hubo un problema durante la comparación:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                lblStatus.Text = "Error durante el análisis.";
            }
            finally
            {
                // Permitir suspensión del sistema nuevamente
                AllowSystemSleep();

                btnAnalyze.Enabled = true;
                btnCancelAnalysis.Enabled = false;
                progressBarAnalysis.Position = 0;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void btnCancelAnalysis_Click(object sender, EventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            btnCancelAnalysis.Enabled = false;
        }

        private void FormatGrid()
        {
            // Ocultar columnas internas
            if (gridView1.Columns["SourceFullPath"] != null)
                gridView1.Columns["SourceFullPath"].Visible = false;

            if (gridView1.Columns["DestinationFullPath"] != null)
                gridView1.Columns["DestinationFullPath"].Visible = false;

            if (gridView1.Columns["SourceHash"] != null)
                gridView1.Columns["SourceHash"].Visible = false;

            if (gridView1.Columns["DestinationHash"] != null)
                gridView1.Columns["DestinationHash"].Visible = false;

            if (gridView1.Columns["SourceSize"] != null)
                gridView1.Columns["SourceSize"].Visible = false;

            if (gridView1.Columns["DestinationSize"] != null)
                gridView1.Columns["DestinationSize"].Visible = false;

            if (gridView1.Columns["SourceModifiedDate"] != null)
                gridView1.Columns["SourceModifiedDate"].Visible = false;

            if (gridView1.Columns["DestinationModifiedDate"] != null)
                gridView1.Columns["DestinationModifiedDate"].Visible = false;

            if (gridView1.Columns["SourceCreatedDate"] != null)
                gridView1.Columns["SourceCreatedDate"].Visible = false;

            if (gridView1.Columns["DestinationCreatedDate"] != null)
                gridView1.Columns["DestinationCreatedDate"].Visible = false;

            if (gridView1.Columns["Status"] != null)
                gridView1.Columns["Status"].Visible = false;

            if (gridView1.Columns["IsDirectory"] != null)
                gridView1.Columns["IsDirectory"].Visible = false;

            if (gridView1.Columns["FileExtension"] != null)
                gridView1.Columns["FileExtension"].Visible = false;

            if (gridView1.Columns["SubdirectoryLevel"] != null)
                gridView1.Columns["SubdirectoryLevel"].Visible = false;

            // Configurar columnas visibles con ORDEN específico
            int colIndex = 0;

            // 1. Tipo de Item (Archivo o Carpeta)
            if (gridView1.Columns["TipoItem"] != null)
            {
                gridView1.Columns["TipoItem"].Caption = "Tipo";
                gridView1.Columns["TipoItem"].Width = 100;
                gridView1.Columns["TipoItem"].VisibleIndex = colIndex++;
            }

            // 2. Estado
            if (gridView1.Columns["StatusText"] != null)
            {
                gridView1.Columns["StatusText"].Caption = "Estado";
                gridView1.Columns["StatusText"].Width = 90;
                gridView1.Columns["StatusText"].VisibleIndex = colIndex++;
            }

            // 3. Nombre
            if (gridView1.Columns["FileName"] != null)
            {
                gridView1.Columns["FileName"].Caption = "Nombre";
                gridView1.Columns["FileName"].Width = 200;
                gridView1.Columns["FileName"].VisibleIndex = colIndex++;
            }

            // 4. Ruta Relativa
            if (gridView1.Columns["RelativePath"] != null)
            {
                gridView1.Columns["RelativePath"].Caption = "Ruta Relativa";
                gridView1.Columns["RelativePath"].Width = 250;
                gridView1.Columns["RelativePath"].VisibleIndex = colIndex++;
            }

            // 5. Extensión
            if (gridView1.Columns["Extension"] != null)
            {
                gridView1.Columns["Extension"].Caption = "Extensión";
                gridView1.Columns["Extension"].Width = 100;
                gridView1.Columns["Extension"].VisibleIndex = colIndex++;
            }

            // 6. Tamaño
            if (gridView1.Columns["SizeText"] != null)
            {
                gridView1.Columns["SizeText"].Caption = "Tamaño";
                gridView1.Columns["SizeText"].Width = 100;
                gridView1.Columns["SizeText"].VisibleIndex = colIndex++;
            }

            // 7. Nivel de Profundidad
            if (gridView1.Columns["NivelProfundidad"] != null)
            {
                gridView1.Columns["NivelProfundidad"].Caption = "Nivel";
                gridView1.Columns["NivelProfundidad"].Width = 80;
                gridView1.Columns["NivelProfundidad"].VisibleIndex = colIndex++;
            }

            // 8. Fecha de Modificación
            if (gridView1.Columns["FechaModificacion"] != null)
            {
                gridView1.Columns["FechaModificacion"].Caption = "Fecha Modificación";
                gridView1.Columns["FechaModificacion"].Width = 150;
                gridView1.Columns["FechaModificacion"].VisibleIndex = colIndex++;
            }

            // 9. Fecha de Creación
            if (gridView1.Columns["FechaCreacion"] != null)
            {
                gridView1.Columns["FechaCreacion"].Caption = "Fecha Creación";
                gridView1.Columns["FechaCreacion"].Width = 150;
                gridView1.Columns["FechaCreacion"].VisibleIndex = colIndex++;
            }

            // Aplicar estilos condicionales
            gridView1.Appearance.FocusedRow.BackColor = System.Drawing.Color.LightBlue;
            gridView1.Appearance.FocusedRow.ForeColor = System.Drawing.Color.Black;

            gridView1.OptionsView.EnableAppearanceEvenRow = true;
            gridView1.Appearance.EvenRow.BackColor = System.Drawing.Color.FromArgb(245, 245, 245);

            gridView1.BestFitColumns();
        }

        private void FormatSyncGrid()
        {
            // Configurar gridView2 para el Paso 3 (Sincronización)

            // Habilitar edición solo para checkbox
            gridView2.OptionsBehavior.Editable = true;
            gridView2.OptionsBehavior.ReadOnly = false;

            // Ocultar columnas innecesarias
            string[] hiddenColumns = {
                "SourceFullPath", "DestinationFullPath", "SourceHash", "DestinationHash",
                "SourceSize", "DestinationSize", "SourceModifiedDate", "DestinationModifiedDate",
                "SourceCreatedDate", "DestinationCreatedDate", "Status", "IsDirectory",
                "FileExtension", "SubdirectoryLevel", "FileName", "Extension",
                "NivelProfundidad"
            };

            foreach (var colName in hiddenColumns)
            {
                if (gridView2.Columns[colName] != null)
                    gridView2.Columns[colName].Visible = false;
            }

            // Configurar columnas específicas para sincronización con CHECKBOX y HASH
            int colIndex = 0;

            // 1. Checkbox de Selección
            if (gridView2.Columns["Selected"] != null)
            {
                gridView2.Columns["Selected"].Caption = "☑";
                gridView2.Columns["Selected"].Width = 40;
                gridView2.Columns["Selected"].VisibleIndex = colIndex++;
                gridView2.Columns["Selected"].OptionsColumn.AllowEdit = true;
            }

            // 2. Tipo de Item
            if (gridView2.Columns["TipoItem"] != null)
            {
                gridView2.Columns["TipoItem"].Caption = "Tipo";
                gridView2.Columns["TipoItem"].Width = 100;
                gridView2.Columns["TipoItem"].VisibleIndex = colIndex++;
                gridView2.Columns["TipoItem"].OptionsColumn.AllowEdit = false;
            }

            // 3. Ruta Relativa
            if (gridView2.Columns["RelativePath"] != null)
            {
                gridView2.Columns["RelativePath"].Caption = "Ruta";
                gridView2.Columns["RelativePath"].Width = 260;
                gridView2.Columns["RelativePath"].VisibleIndex = colIndex++;
                gridView2.Columns["RelativePath"].OptionsColumn.AllowEdit = false;
            }

            // 4. Estado
            if (gridView2.Columns["StatusText"] != null)
            {
                gridView2.Columns["StatusText"].Caption = "Estado";
                gridView2.Columns["StatusText"].Width = 80;
                gridView2.Columns["StatusText"].VisibleIndex = colIndex++;
                gridView2.Columns["StatusText"].OptionsColumn.AllowEdit = false;
            }

            // 5. Tamaño
            if (gridView2.Columns["SizeText"] != null)
            {
                gridView2.Columns["SizeText"].Caption = "Tamaño";
                gridView2.Columns["SizeText"].Width = 80;
                gridView2.Columns["SizeText"].VisibleIndex = colIndex++;
                gridView2.Columns["SizeText"].OptionsColumn.AllowEdit = false;
            }

            // 6. Hash Origen
            if (gridView2.Columns["HashOrigen"] != null)
            {
                gridView2.Columns["HashOrigen"].Caption = "Hash Origen";
                gridView2.Columns["HashOrigen"].Width = 140;
                gridView2.Columns["HashOrigen"].VisibleIndex = colIndex++;
                gridView2.Columns["HashOrigen"].OptionsColumn.AllowEdit = false;
            }

            // 7. Hash Destino
            if (gridView2.Columns["HashDestino"] != null)
            {
                gridView2.Columns["HashDestino"].Caption = "Hash Destino";
                gridView2.Columns["HashDestino"].Width = 140;
                gridView2.Columns["HashDestino"].VisibleIndex = colIndex++;
                gridView2.Columns["HashDestino"].OptionsColumn.AllowEdit = false;
            }

            // 8. Integridad
            if (gridView2.Columns["IntegridadHash"] != null)
            {
                gridView2.Columns["IntegridadHash"].Caption = "Integridad";
                gridView2.Columns["IntegridadHash"].Width = 90;
                gridView2.Columns["IntegridadHash"].VisibleIndex = colIndex++;
                gridView2.Columns["IntegridadHash"].OptionsColumn.AllowEdit = false;
            }

            // 9. Fecha Modificación
            if (gridView2.Columns["FechaModificacion"] != null)
            {
                gridView2.Columns["FechaModificacion"].Caption = "Fecha Modificación";
                gridView2.Columns["FechaModificacion"].Width = 130;
                gridView2.Columns["FechaModificacion"].VisibleIndex = colIndex++;
                gridView2.Columns["FechaModificacion"].OptionsColumn.AllowEdit = false;
            }

            // 10. Fecha Creación
            if (gridView2.Columns["FechaCreacion"] != null)
            {
                gridView2.Columns["FechaCreacion"].Caption = "Fecha Creación";
                gridView2.Columns["FechaCreacion"].Width = 130;
                gridView2.Columns["FechaCreacion"].VisibleIndex = colIndex++;
                gridView2.Columns["FechaCreacion"].OptionsColumn.AllowEdit = false;
            }

            // Aplicar estilos
            gridView2.Appearance.FocusedRow.BackColor = System.Drawing.Color.LightBlue;
            gridView2.Appearance.FocusedRow.ForeColor = System.Drawing.Color.Black;
            gridView2.OptionsView.EnableAppearanceEvenRow = true;
            gridView2.Appearance.EvenRow.BackColor = System.Drawing.Color.FromArgb(245, 245, 245);

            gridView2.BestFitColumns();
        }

        /// <summary>
        /// Obtiene la ruta relativa compatible con .NET Framework 4.8
        /// </summary>
        private string GetRelativePathCompat(string basePath, string fullPath)
        {
            basePath = Path.GetFullPath(basePath).TrimEnd('\\', '/');
            fullPath = Path.GetFullPath(fullPath);

            Uri baseUri = new Uri(basePath + Path.DirectorySeparatorChar);
            Uri fullUri = new Uri(fullPath);

            Uri relativeUri = baseUri.MakeRelativeUri(fullUri);
            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            return relativePath.Replace('/', Path.DirectorySeparatorChar);
        }

        private void UpdateStatistics()
        {
            int totalFiles = _comparisonResults.Count(r => !r.IsDirectory);
            int totalFolders = _comparisonResults.Count(r => r.IsDirectory);

            int missing = _comparisonResults.Count(r => r.Status == ComparisonStatus.Missing);
            int different = _comparisonResults.Count(r => r.Status == ComparisonStatus.Different);
            int match = _comparisonResults.Count(r => r.Status == ComparisonStatus.Match);

            int missingFiles = _comparisonResults.Count(r => r.Status == ComparisonStatus.Missing && !r.IsDirectory);
            int missingFolders = _comparisonResults.Count(r => r.Status == ComparisonStatus.Missing && r.IsDirectory);

            lblStatistics.Text = $"📊 Total: {_comparisonResults.Count} items ({totalFiles} archivos, {totalFolders} carpetas) | " +
                                $"❌ Faltantes: {missing} ({missingFiles} archivos, {missingFolders} carpetas) | " +
                                $"⚠️ Diferentes: {different} | " +
                                $"✅ Coinciden: {match}";
        }

        private void Comparator_ProgressChanged(object sender, ProgressEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => Comparator_ProgressChanged(sender, e)));
                return;
            }

            progressBarAnalysis.Position = e.Percentage;
        }

        private void Comparator_StatusChanged(object sender, string status)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => Comparator_StatusChanged(sender, status)));
                return;
            }

            lblStatus.Text = status;
        }

        private void Comparator_FileProgressChanged(object sender, ProgressEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => Comparator_FileProgressChanged(sender, e)));
                return;
            }

            // Actualizar progressBarControl1 (barra de progreso individual en Paso 2)
            progressBarControl1.Position = e.Percentage;
            progressBarControl1.Properties.PercentView = true;
        }

        // ===========================================
        // PASO 3: Sincronización
        // ===========================================

        private async void btnCopyMissing_Click(object sender, EventArgs e)
        {
            // ✅ Obtener archivos faltantes SELECCIONADOS (checkbox marcado)
            var selectedMissingFiles = _syncGridDataSource?.Where(f => f.Selected && f.Status == ComparisonStatus.Missing).ToList()
                                      ?? _comparisonResults.Where(f => f.Status == ComparisonStatus.Missing).ToList();

            if (selectedMissingFiles.Count == 0)
            {
                XtraMessageBox.Show(
                    "No seleccionaste ningún archivo. Marca los que quieras copiar.",
                    "Nada seleccionado",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var result = XtraMessageBox.Show(
                $"¿Copiar {selectedMissingFiles.Count} {(selectedMissingFiles.Count == 1 ? "archivo" : "archivos")} al destino?",
                "Confirmar",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                await PerformSyncOperation(() => _synchronizer.CopyMissingFilesAsync(selectedMissingFiles, _cancellationTokenSource.Token));
            }
        }

        private async void btnReplaceDifferent_Click(object sender, EventArgs e)
        {
            // ✅ Obtener archivos diferentes SELECCIONADOS (checkbox marcado)
            var selectedDifferentFiles = _syncGridDataSource?.Where(f => f.Selected && f.Status == ComparisonStatus.Different).ToList()
                                        ?? _comparisonResults.Where(f => f.Status == ComparisonStatus.Different).ToList();

            if (selectedDifferentFiles.Count == 0)
            {
                XtraMessageBox.Show(
                    "No seleccionaste ningún archivo. Marca los que quieras reemplazar.",
                    "Nada seleccionado",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var result = XtraMessageBox.Show(
                $"¿Reemplazar {selectedDifferentFiles.Count} {(selectedDifferentFiles.Count == 1 ? "archivo" : "archivos")} en el destino?\n\nEsto no se puede deshacer.",
                "Confirmar",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                await PerformSyncOperation(() => _synchronizer.ReplaceDifferentFilesAsync(selectedDifferentFiles, _cancellationTokenSource.Token));
            }
        }

        private async Task PerformSyncOperation(Func<Task<SyncResult>> operation)
        {
            try
            {
                // Prevenir suspensión del sistema
                PreventSystemSleep();

                btnCopyMissing.Enabled = false;
                btnReplaceDifferent.Enabled = false;
                btnCancelSync.Enabled = true;
                progressBarSync.Position = 0;
                lblSyncStatus.Text = "Iniciando sincronización...";

                _cancellationTokenSource = new CancellationTokenSource();

                var result = await operation();

                // Mensaje de éxito al terminar
                XtraMessageBox.Show(
                    $"Sincronización terminada:\n\nArchivos copiados: {result.CopiedFiles}\nErrores: {result.Errors}",
                    "Listo",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                lblSyncStatus.Text = "Sincronización completada";
            }
            catch (OperationCanceledException)
            {
                lblSyncStatus.Text = "Operación cancelada por el usuario";
                XtraMessageBox.Show(
                    "Cancelaste la sincronización.",
                    "Cancelado",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show(
                    $"Hubo un problema en la sincronización:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                // Permitir suspensión del sistema nuevamente
                AllowSystemSleep();

                btnCopyMissing.Enabled = true;
                btnReplaceDifferent.Enabled = true;
                btnCancelSync.Enabled = false;
                progressBarSync.Position = 0;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void btnCancelSync_Click(object sender, EventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            btnCancelSync.Enabled = false;
        }

        private void Synchronizer_ProgressChanged(object sender, ProgressEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => Synchronizer_ProgressChanged(sender, e)));
                return;
            }

            progressBarSync.Position = e.Percentage;
        }

        private void Synchronizer_StatusChanged(object sender, string status)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => Synchronizer_StatusChanged(sender, status)));
                return;
            }

            lblSyncStatus.Text = status;
        }

        private void Synchronizer_FileProgressChanged(object sender, ProgressEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => Synchronizer_FileProgressChanged(sender, e)));
                return;
            }

            // Actualizar progressBarControl2 (barra de progreso individual en Paso 3)
            progressBarControl2.Position = e.Percentage;
            progressBarControl2.Properties.PercentView = true;
        }

        private void Synchronizer_LogMessage(object sender, LogEventArgs e)
        {
            // Ya no usamos log de texto, solo actualizamos el grid
        }

        private void Synchronizer_FileCopied(object sender, FileComparisonResult file)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => Synchronizer_FileCopied(sender, file)));
                return;
            }

            // El archivo ya tiene su Status cambiado a Match por el synchronizer
            // Gracias a INotifyPropertyChanged, el cambio debe reflejarse automáticamente

            // Forzar actualización visual del grid2
            if (_syncGridDataSource != null && _syncGridDataSource.Contains(file))
            {
                // Buscar la fila por RelativePath y repintarla
                int rowHandle = gridView2.LocateByValue("RelativePath", file.RelativePath);
                if (rowHandle != DevExpress.XtraGrid.GridControl.InvalidRowHandle)
                {
                    // Refrescar solo esta fila específica
                    gridView2.RefreshRow(rowHandle);
                    gridView2.InvalidateRow(rowHandle);  // Forzar repintado visual
                }

                // Actualizar contador de archivos pendientes
                int pendientes = _syncGridDataSource.Count(item => item.Status != ComparisonStatus.Match);
                lblSyncStatus.Text = $"Archivos pendientes: {pendientes}";
            }
        }

        private void wizardControl1_CancelClick(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (XtraMessageBox.Show(
                "¿Cerrar la aplicación?",
                "Salir",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Application.Exit();
            }
            else
            {
                e.Cancel = true;
            }
        }

        private void wizardControl1_FinishClick(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Guardar configuración antes de cerrar
            SaveConfiguration();

            // Cerrar la aplicación sin confirmación
            Application.Exit();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveConfiguration();
        }

        private void gridView1_DoubleClick(object sender, EventArgs e)
        {
            // Mostrar menú contextual en doble clic
            ShowContextMenu();
        }

        private void gridView1_PopupMenuShowing(object sender, DevExpress.XtraGrid.Views.Grid.PopupMenuShowingEventArgs e)
        {
            // Mostrar menú contextual en clic derecho
            if (e.MenuType == DevExpress.XtraGrid.Views.Grid.GridMenuType.Row)
            {
                e.Allow = false;
                ShowContextMenu();
            }
        }

        private void ShowContextMenu()
        {
            if (gridView1.FocusedRowHandle < 0 || _comparisonResults == null || _comparisonResults.Count == 0)
                return;

            var selectedItem = gridView1.GetRow(gridView1.FocusedRowHandle) as FileComparisonResult;
            if (selectedItem == null)
                return;

            // Habilitar/deshabilitar opciones según el contexto
            menuCopiarArchivo.Enabled = selectedItem.Status == ComparisonStatus.Missing;
            menuAbrirOrigen.Enabled = !selectedItem.IsDirectory && File.Exists(selectedItem.SourceFullPath);
            menuAbrirDestino.Enabled = !selectedItem.IsDirectory && File.Exists(selectedItem.DestinationFullPath);
            menuEliminarOrigen.Enabled = (selectedItem.IsDirectory && Directory.Exists(selectedItem.SourceFullPath)) ||
                                         (!selectedItem.IsDirectory && File.Exists(selectedItem.SourceFullPath));
            menuEliminarDestino.Enabled = (selectedItem.IsDirectory && Directory.Exists(selectedItem.DestinationFullPath)) ||
                                          (!selectedItem.IsDirectory && File.Exists(selectedItem.DestinationFullPath));

            // Desbloquear solo aplica a archivos, NO a carpetas
            menuDesbloquearArchivo.Enabled = !selectedItem.IsDirectory &&
                                             (File.Exists(selectedItem.SourceFullPath) || File.Exists(selectedItem.DestinationFullPath));

            // Verificar solo aplica a archivos, NO a carpetas
            menuVerificarArchivo.Enabled = !selectedItem.IsDirectory &&
                                           File.Exists(selectedItem.SourceFullPath) &&
                                           File.Exists(selectedItem.DestinationFullPath);

            // Mostrar el menú
            contextMenuGrid.Show(gridControl1, gridControl1.PointToClient(Cursor.Position));
        }

        private void gridView1_RowCellStyle(object sender, DevExpress.XtraGrid.Views.Grid.RowCellStyleEventArgs e)
        {
            // Pintar filas en ROJO si son Faltante o Diferente
            if (e.RowHandle < 0)
                return;

            var item = gridView1.GetRow(e.RowHandle) as FileComparisonResult;
            if (item == null)
                return;

            if (item.Status == ComparisonStatus.Missing)
            {
                // ROJO CLARO para faltantes
                e.Appearance.BackColor = System.Drawing.Color.FromArgb(255, 200, 200);
                e.Appearance.ForeColor = System.Drawing.Color.DarkRed;
            }
            else if (item.Status == ComparisonStatus.Different)
            {
                // NARANJA CLARO para diferentes
                e.Appearance.BackColor = System.Drawing.Color.FromArgb(255, 220, 180);
                e.Appearance.ForeColor = System.Drawing.Color.DarkOrange;
            }
            else if (item.Status == ComparisonStatus.Match)
            {
                // VERDE CLARO para coincidencias
                e.Appearance.BackColor = System.Drawing.Color.FromArgb(200, 255, 200);
                e.Appearance.ForeColor = System.Drawing.Color.DarkGreen;
            }
        }

        #region Métodos del Menú Contextual

        private FileComparisonResult GetSelectedItem()
        {
            if (gridView1.FocusedRowHandle < 0)
                return null;
            return gridView1.GetRow(gridView1.FocusedRowHandle) as FileComparisonResult;
        }

        private void menuCopiarArchivo_Click(object sender, EventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null || item.Status != ComparisonStatus.Missing)
                return;

            try
            {
                // Habilitar privilegios administrativos para acceso sin restricciones
                WindowsPermissionsHelper.EnableAdministrativePrivileges();

                if (item.IsDirectory)
                {
                    WindowsPermissionsHelper.CreateDirectorySafe(item.DestinationFullPath);
                    XtraMessageBox.Show($"Listo, se creó la carpeta:\n\n{item.RelativePath}", "Carpeta creada", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    string destinationDir = Path.GetDirectoryName(item.DestinationFullPath);
                    if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                        WindowsPermissionsHelper.CreateDirectorySafe(destinationDir);

                    WindowsPermissionsHelper.CopyFileSafe(item.SourceFullPath, item.DestinationFullPath, overwrite: true);
                    XtraMessageBox.Show($"Listo, se copió el archivo:\n\n{item.RelativePath}", "Archivo copiado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                item.Status = ComparisonStatus.Match;
                gridControl1.RefreshDataSource();
                UpdateStatistics();
            }
            catch (UnauthorizedAccessException ex)
            {
                XtraMessageBox.Show($"Error de permisos:\n\n{ex.Message}\n\nIntente ejecutar la aplicación como Administrador.", "Permiso denegado", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"No se pudo copiar:\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void menuCopiarRutaOrigen_Click(object sender, EventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null) return;

            Clipboard.SetText(item.SourceFullPath);
            XtraMessageBox.Show($"Se copió al portapapeles:\n\n{item.SourceFullPath}", "Ruta copiada", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void menuCopiarRutaDestino_Click(object sender, EventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null) return;

            Clipboard.SetText(item.DestinationFullPath);
            XtraMessageBox.Show($"Se copió al portapapeles:\n\n{item.DestinationFullPath}", "Ruta copiada", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void menuAbrirOrigen_Click(object sender, EventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null || item.IsDirectory) return;

            try
            {
                if (File.Exists(item.SourceFullPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = item.SourceFullPath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    XtraMessageBox.Show("El archivo no existe.", "No encontrado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"No se pudo abrir:\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void menuAbrirDestino_Click(object sender, EventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null || item.IsDirectory) return;

            try
            {
                if (File.Exists(item.DestinationFullPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = item.DestinationFullPath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    XtraMessageBox.Show("El archivo no existe.", "No encontrado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"No se pudo abrir:\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void menuAbrirCarpetaOrigen_Click(object sender, EventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null) return;

            try
            {
                string folder = item.IsDirectory ? item.SourceFullPath : Path.GetDirectoryName(item.SourceFullPath);
                if (Directory.Exists(folder))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{item.SourceFullPath}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"No se pudo abrir la carpeta:\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void menuAbrirCarpetaDestino_Click(object sender, EventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null) return;

            try
            {
                string folder = item.IsDirectory ? item.DestinationFullPath : Path.GetDirectoryName(item.DestinationFullPath);
                if (Directory.Exists(folder))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{item.DestinationFullPath}\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    // Si no existe, abrir la carpeta padre
                    string parentFolder = Path.GetDirectoryName(folder);
                    if (Directory.Exists(parentFolder))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{parentFolder}\"",
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"No se pudo abrir la carpeta:\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void menuRenombrar_Click(object sender, EventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null) return;

            // Solicitar nuevo nombre
            string currentName = item.FileName;
            string newName = DevExpress.XtraEditors.XtraInputBox.Show(
                $"Nuevo nombre:\n\n{currentName}",
                "Renombrar",
                currentName);

            if (string.IsNullOrWhiteSpace(newName) || newName == currentName)
                return;

            try
            {
                // Renombrar en origen
                if (item.IsDirectory)
                {
                    if (Directory.Exists(item.SourceFullPath))
                    {
                        string newPath = Path.Combine(Path.GetDirectoryName(item.SourceFullPath), newName);
                        Directory.Move(item.SourceFullPath, newPath);
                        XtraMessageBox.Show($"Listo, se renombró:\n\n{currentName} → {newName}", "Renombrado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else
                {
                    if (File.Exists(item.SourceFullPath))
                    {
                        string newPath = Path.Combine(Path.GetDirectoryName(item.SourceFullPath), newName);
                        File.Move(item.SourceFullPath, newPath);
                        XtraMessageBox.Show($"Listo, se renombró:\n\n{currentName} → {newName}", "Renombrado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }

                // Actualizar el item
                item.FileName = newName;
                item.SourceFullPath = Path.Combine(Path.GetDirectoryName(item.SourceFullPath), newName);
                item.DestinationFullPath = Path.Combine(Path.GetDirectoryName(item.DestinationFullPath), newName);
                gridControl1.RefreshDataSource();
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"No se pudo renombrar:\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void menuEliminarOrigen_Click(object sender, EventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null) return;

            var result = XtraMessageBox.Show(
                $"¿Eliminar de origen?\n\n{item.RelativePath}\n\nEsto no se puede deshacer.",
                "Confirmar",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) return;

            try
            {
                if (item.IsDirectory)
                {
                    if (Directory.Exists(item.SourceFullPath))
                        Directory.Delete(item.SourceFullPath, true);
                }
                else
                {
                    if (File.Exists(item.SourceFullPath))
                        File.Delete(item.SourceFullPath);
                }

                XtraMessageBox.Show("Listo, se eliminó.", "Eliminado", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Remover del grid
                _comparisonResults.Remove(item);
                gridControl1.RefreshDataSource();
                UpdateStatistics();
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"No se pudo eliminar:\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void menuEliminarDestino_Click(object sender, EventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null) return;

            var result = XtraMessageBox.Show(
                $"¿Eliminar de destino?\n\n{item.RelativePath}\n\nEsto no se puede deshacer.",
                "Confirmar",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) return;

            try
            {
                if (item.IsDirectory)
                {
                    if (Directory.Exists(item.DestinationFullPath))
                        Directory.Delete(item.DestinationFullPath, true);
                }
                else
                {
                    if (File.Exists(item.DestinationFullPath))
                        File.Delete(item.DestinationFullPath);
                }

                XtraMessageBox.Show("Listo, se eliminó.", "Eliminado", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Cambiar estado a "Falta"
                item.Status = ComparisonStatus.Missing;
                gridControl1.RefreshDataSource();
                UpdateStatistics();
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"No se pudo eliminar:\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void menuPropiedades_Click(object sender, EventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null) return;

            string info = $"Propiedades\n\n" +
                          $"Tipo: {item.TipoItem}\n" +
                          $"Nombre: {item.FileName}\n" +
                          $"Extensión: {item.FileExtension}\n" +
                          $"Ruta Relativa: {item.RelativePath}\n" +
                          $"Nivel de Subdirectorio: {item.NivelProfundidad}\n" +
                          $"Estado: {item.StatusText}\n\n" +
                          $"📂 ORIGEN:\n" +
                          $"Ruta: {item.SourceFullPath}\n" +
                          $"Existe: {(item.IsDirectory ? Directory.Exists(item.SourceFullPath) : File.Exists(item.SourceFullPath))}\n";

            if (!item.IsDirectory)
            {
                info += $"Tamaño: {item.SizeText}\n" +
                        $"Fecha Modificación: {item.SourceModifiedDate:dd/MM/yyyy HH:mm:ss}\n" +
                        $"Fecha Creación: {item.SourceCreatedDate:dd/MM/yyyy HH:mm:ss}\n";
            }

            info += $"\n📂 DESTINO:\n" +
                    $"Ruta: {item.DestinationFullPath}\n" +
                    $"Existe: {(item.IsDirectory ? Directory.Exists(item.DestinationFullPath) : File.Exists(item.DestinationFullPath))}\n";

            if (!item.IsDirectory && item.DestinationModifiedDate.HasValue)
            {
                info += $"Tamaño: {FormatSize(item.DestinationSize)}\n" +
                        $"Fecha Modificación: {item.DestinationModifiedDate:dd/MM/yyyy HH:mm:ss}\n" +
                        $"Fecha Creación: {item.DestinationCreatedDate:dd/MM/yyyy HH:mm:ss}\n";
            }

            XtraMessageBox.Show(info, "Propiedades del Item", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async void menuVerificarArchivo_Click(object sender, EventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null) return;

            if (item.IsDirectory)
            {
                XtraMessageBox.Show("Solo se pueden verificar archivos, no carpetas.",
                    "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!File.Exists(item.SourceFullPath))
            {
                XtraMessageBox.Show($"El archivo no existe:\n{item.SourceFullPath}",
                    "No encontrado", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                lblStatus.Text = "Verificando integridad del archivo...";
                btnAnalyze.Enabled = false;

                // Calcular hash del origen
                string sourceHash = await Task.Run(() => CalculateFileSHA256(item.SourceFullPath));
                item.SourceHash = sourceHash;

                string destHash = "N/A";
                if (File.Exists(item.DestinationFullPath))
                {
                    destHash = await Task.Run(() => CalculateFileSHA256(item.DestinationFullPath));
                    item.DestinationHash = destHash;
                }

                bool coinciden = sourceHash == destHash && destHash != "N/A";

                string mensaje = $"🔍 VERIFICACIÓN SHA-256\n\n" +
                                $"Archivo: {item.FileName}\n" +
                                $"Ruta: {item.RelativePath}\n" +
                                $"Tamaño: {item.SizeText}\n\n" +
                                $"📂 Hash Origen:\n{sourceHash}\n\n" +
                                $"📂 Hash Destino:\n{destHash}\n\n" +
                                $"Resultado: {(coinciden ? "✅ Los archivos son IDÉNTICOS" : "❌ Los archivos son DIFERENTES")}";

                XtraMessageBox.Show(mensaje, "Verificación de Integridad",
                    MessageBoxButtons.OK, coinciden ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

                // Actualizar el grid
                if (wizardControl1.SelectedPageIndex == 1)
                    gridView1.RefreshData();
                else if (wizardControl1.SelectedPageIndex == 2)
                    gridView2.RefreshData();
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"No se pudo verificar:\n\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                lblStatus.Text = "Listo";
                btnAnalyze.Enabled = true;
            }
        }

        private async void menuVerificarArchivos_Click(object sender, EventArgs e)
        {
            var gridView = wizardControl1.SelectedPageIndex == 1 ? gridView1 : gridView2;
            var selectedRows = gridView.GetSelectedRows();

            if (selectedRows.Length == 0)
            {
                XtraMessageBox.Show("Selecciona uno o más archivos primero.",
                    "Nada seleccionado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selectedFiles = new List<FileComparisonResult>();
            foreach (var rowHandle in selectedRows)
            {
                if (rowHandle >= 0)
                {
                    var file = gridView.GetRow(rowHandle) as FileComparisonResult;
                    if (file != null && !file.IsDirectory)
                        selectedFiles.Add(file);
                }
            }

            if (selectedFiles.Count == 0)
            {
                XtraMessageBox.Show("Solo seleccionaste carpetas. Necesitas seleccionar archivos.",
                    "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = XtraMessageBox.Show(
                $"¿Verificar {selectedFiles.Count} {(selectedFiles.Count == 1 ? "archivo" : "archivos")}?",
                "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return;

            try
            {
                lblStatus.Text = "Verificando archivos...";
                btnAnalyze.Enabled = false;
                progressBarAnalysis.Position = 0;

                int processed = 0;
                int coinciden = 0;
                int difieren = 0;
                int errores = 0;

                foreach (var file in selectedFiles)
                {
                    try
                    {
                        lblStatus.Text = $"Verificando {processed + 1}/{selectedFiles.Count}: {file.FileName}";

                        // Calcular hash del origen
                        string sourceHash = await Task.Run(() => CalculateFileSHA256(file.SourceFullPath));
                        file.SourceHash = sourceHash;

                        // Calcular hash del destino si existe
                        if (File.Exists(file.DestinationFullPath))
                        {
                            string destHash = await Task.Run(() => CalculateFileSHA256(file.DestinationFullPath));
                            file.DestinationHash = destHash;

                            if (sourceHash == destHash)
                                coinciden++;
                            else
                                difieren++;
                        }
                    }
                    catch
                    {
                        errores++;
                    }

                    processed++;
                    progressBarAnalysis.Position = (int)((double)processed / selectedFiles.Count * 100);
                }

                // Actualizar el grid
                gridView.RefreshData();

                string mensaje = $"Verificación terminada:\n\n" +
                                $"Archivos: {selectedFiles.Count}\n" +
                                $"Coinciden: {coinciden}\n" +
                                $"Difieren: {difieren}\n" +
                                $"Errores: {errores}";

                XtraMessageBox.Show(mensaje, "Listo",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"Hubo un problema al verificar:\n\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                lblStatus.Text = "Listo";
                btnAnalyze.Enabled = true;
                progressBarAnalysis.Position = 0;
            }
        }

        private string CalculateFileSHA256(string filePath)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private string FormatSize(long bytes)
        {
            if (bytes == 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        private void menuDesbloquearArchivo_Click(object sender, EventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null) return;

            if (item.IsDirectory)
            {
                XtraMessageBox.Show("Esta función solo funciona con archivos, no con carpetas.",
                    "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Verificar si el archivo está en uso
            string targetFile = File.Exists(item.DestinationFullPath) ? item.DestinationFullPath : item.SourceFullPath;

            if (!File.Exists(targetFile))
            {
                XtraMessageBox.Show($"El archivo no existe:\n{targetFile}",
                    "No encontrado", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var result = XtraMessageBox.Show(
                $"¿Intentar desbloquear este archivo?\n\n{item.FileName}\n\nALERTA: Si está abierto en otro programa, ese programa podría perder datos no guardados.",
                "Confirmar",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
                return;

            try
            {
                // Intentar abrir el archivo para verificar si está bloqueado
                bool estaBloqueado = false;
                try
                {
                    using (FileStream fs = File.Open(targetFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        // Si llegamos aquí, el archivo NO está bloqueado
                        estaBloqueado = false;
                    }
                }
                catch (IOException)
                {
                    estaBloqueado = true;
                }

                if (!estaBloqueado)
                {
                    XtraMessageBox.Show(
                        "El archivo no está bloqueado. Ya puedes copiarlo sin problemas.",
                        "Todo bien",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                // Intentar desbloquear usando Handle (requiere permisos de administrador)
                lblStatus.Text = "Desbloqueando archivo...";
                Application.DoEvents();

                // Método 1: Reintentar con FileShare permisivo
                bool desbloqueado = TryUnlockFile(targetFile);

                if (desbloqueado)
                {
                    XtraMessageBox.Show(
                        $"Listo, se desbloqueó:\n\n{item.FileName}\n\nAhora puedes copiarlo.",
                        "Desbloqueado",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    XtraMessageBox.Show(
                        "No se pudo desbloquear.\n\nEl archivo sigue en uso. Intenta:\n\n1. Cerrar el programa que lo usa\n2. Ejecutar como Administrador\n3. Reiniciar el equipo",
                        "No desbloqueado",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show(
                    $"No se pudo desbloquear:\n\n{ex.Message}\n\nIntenta:\n• Ejecutar como Administrador\n• Cerrar el programa que lo usa\n• Reiniciar el equipo",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                lblStatus.Text = "Listo";
            }
        }

        private bool TryUnlockFile(string filePath)
        {
            try
            {
                // Método 1: Forzar garbage collection para cerrar handles pendientes
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(100);

                // Método 2: Intentar abrir con FileShare.ReadWrite | FileShare.Delete
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    // Si podemos abrir, el archivo está accesible
                }

                // Método 3: Reintentar apertura exclusiva
                Thread.Sleep(100);
                using (FileStream fs = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return true;  // Desbloqueado exitosamente
                }
            }
            catch
            {
                return false;  // No se pudo desbloquear
            }
        }

        private void PreventSystemSleep()
        {
            if (chkPreventSleep.Checked)
            {
                // Evitar que el sistema se suspenda durante operaciones
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
                _isWorking = true;
            }
        }

        private void AllowSystemSleep()
        {
            if (_isWorking)
            {
                // Permitir que el sistema se suspenda nuevamente
                SetThreadExecutionState(ES_CONTINUOUS);
                _isWorking = false;
            }
        }

        #endregion

        private void lblTopInfo_Click(object sender, EventArgs e)
        {

        }

        // ===========================================
        // CONFIGURACIÓN Y TEMAS
        // ===========================================

        /// <summary>
        /// Aplica el tema seleccionado inmediatamente
        /// </summary>
        private void ApplyTheme(string themeName, bool isDarkTheme)
        {
            try
            {
                string theme = isDarkTheme ? "Office 2019 Black" : themeName;
                DevExpress.LookAndFeel.UserLookAndFeel.Default.SetSkinStyle(theme);
                
                // Guardar en configuración
                _config.LastTheme = themeName;
                _config.UseDarkTheme = isDarkTheme;
                _config.Save();
            }
            catch (Exception ex)
            {
                XtraMessageBox.Show($"No se pudo cambiar el tema:\n\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Carga las configuraciones guardadas en los controles
        /// </summary>
        private void LoadConfigurationToControls()
        {
            // Este método se llamará cuando se creen los controles de configuración
            // Por ahora solo actualizamos los controles existentes
            chkUseHash.Checked = _config.UseHashComparison;
            chkPreventSleep.Checked = _config.PreventSystemSleep;
            chkMaxBuffer.Checked = _config.UseMaximumBuffer;
        }

        /// <summary>
        /// Guarda las configuraciones desde los controles
        /// </summary>
        private void SaveConfigurationFromControls()
        {
            _config.UseHashComparison = chkUseHash.Checked;
            _config.PreventSystemSleep = chkPreventSleep.Checked;
            _config.UseMaximumBuffer = chkMaxBuffer.Checked;
            _config.Save();
        }

        /// <summary>
        /// Abre el panel de configuración en un formulario modal
        /// </summary>
        private void OpenConfigurationPanel()
        {
            using (var configForm = new XtraForm())
            {
                configForm.Text = "⚙️ Configuración - SyncCompareFiles";
                configForm.Size = new System.Drawing.Size(650, 600);
                configForm.StartPosition = FormStartPosition.CenterParent;
                configForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                configForm.MaximizeBox = false;
                configForm.MinimizeBox = false;

                var configPanel = new ConfigurationPanel();
                configPanel.Dock = DockStyle.Fill;
                
                // Manejar cambio de tema
                configPanel.ThemeChanged += (s, e) =>
                {
                    _config.LastTheme = e.ThemeName;
                    _config.UseDarkTheme = e.IsDarkTheme;
                    _config.Save();
                };

                configForm.Controls.Add(configPanel);
                configForm.ShowDialog(this);

                // Recargar configuración después de cerrar
                LoadConfiguration();
            }
        }

    }
}
