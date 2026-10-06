using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Comparador.App.Servicios;

/// <summary>Tema claro, oscuro o el de Windows (y entonces lo sigue si el usuario lo cambia), con fondo Mica.</summary>
public static class Apariencia
{
    private static Window? vigilada;

    public static void Aplicar(TemaApp tema)
    {
        // En segundo plano la ventana se cierra y se vuelve a abrir: la cerrada ya no se puede soltar (Wpf.Ui lanza).
        if (vigilada is { IsLoaded: true })
        {
            SystemThemeWatcher.UnWatch(vigilada);
        }

        vigilada = null;

        if (tema == TemaApp.Sistema)
        {
            ApplicationThemeManager.ApplySystemTheme(true);
            if (Application.Current?.MainWindow is { IsLoaded: true } ventana)
            {
                SystemThemeWatcher.Watch(ventana, WindowBackdropType.Mica, true);
                vigilada = ventana;
            }

            return;
        }

        ApplicationThemeManager.Apply(tema == TemaApp.Oscuro ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.Mica, true);
    }
}
