using System.ComponentModel;
using System.Windows;
using Comparador.App.ModelosVista;
using Comparador.App.Vistas;
using Comparador.Nucleo.Ubicaciones;

namespace Comparador.App.Servicios;

/// <summary>Las ventanas propias que abre la app.</summary>
public static class Dialogos
{
    /// <summary>La carpeta del teléfono elegida, como ruta mtp:\\, o null si se canceló.</summary>
    public static string? ElegirCarpetaDeTelefono(DispositivoDisponible telefono)
    {
        var modelo = new ExploradorTelefonoModeloVista(telefono.Nombre);
        var ventana = new ExploradorTelefonoVentana { DataContext = modelo, Owner = Application.Current.MainWindow };

        return ventana.ShowDialog() == true ? modelo.RutaElegida : null;
    }

    /// <summary>True si el usuario confirmó; si el destino es un disco, tuvo que escribir BORRAR.</summary>
    public static bool ConfirmarClonado(string resumen, bool borraDatos) =>
        new ConfirmarClonVentana(resumen, borraDatos) { Owner = Application.Current.MainWindow }.ShowDialog() == true;

    private static readonly HashSet<Window> CierresConfirmados = [];

    /// <summary>
    /// Cerrar con una copia en marcha la cancelaría sin avisar: primero se pregunta. Si confirma, se cancela la copia
    /// (sin dejar archivos a medias) y la ventana se cierra.
    /// </summary>
    public static void ConfirmarCierreSiCopia(Window ventana, CancelEventArgs cierre, bool copiando, Action cancelar)
    {
        if (!copiando || CierresConfirmados.Remove(ventana))
        {
            return;
        }

        cierre.Cancel = true;
        _ = PreguntarYCerrarAsync(ventana, cancelar);
    }

    private static async Task PreguntarYCerrarAsync(Window ventana, Action cancelar)
    {
        var pregunta = new Wpf.Ui.Controls.MessageBox
        {
            Title = "Hay una copia en marcha",
            Content = "Si cierras, la copia se cancela. Lo que no terminó queda como estaba: no hay archivos a medias.",
            PrimaryButtonText = "Cancelar la copia y cerrar",
            CloseButtonText = "Seguir copiando",
        };
        if (await pregunta.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        cancelar();
        CierresConfirmados.Add(ventana);
        ventana.Close();
    }
}
