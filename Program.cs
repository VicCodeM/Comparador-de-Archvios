using DevExpress.LookAndFeel;
using DevExpress.Skins;
using DevExpress.UserSkins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ComparadorArchivos.Models;

namespace ComparadorArchivos
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            // Cargar configuración y aplicar tema guardado
            var config = AppConfig.Load();
            string theme = config.UseDarkTheme ? "Office 2019 Black" : config.LastTheme;
            
            try
            {
                DevExpress.LookAndFeel.UserLookAndFeel.Default.SetSkinStyle(theme);
            }
            catch
            {
                // Si el tema falla, usar tema por defecto
                DevExpress.LookAndFeel.UserLookAndFeel.Default.SetSkinStyle("Office 2019 Colorful");
            }
            
            Application.Run(new MainForm());
        }
    }
}
