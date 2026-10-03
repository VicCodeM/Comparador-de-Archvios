using System.IO;
using System.Windows;
using Comparador.App.ModelosVista;

namespace Comparador.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Ruta_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Origen_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] rutas &&
            rutas.Length > 0 &&
            Directory.Exists(rutas[0]))
        {
            if (DataContext is PrincipalModeloVista vm)
            {
                vm.RutaOrigen = rutas[0];
            }
        }
    }

    private void Destino_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] rutas &&
            rutas.Length > 0 &&
            Directory.Exists(rutas[0]))
        {
            if (DataContext is PrincipalModeloVista vm)
            {
                vm.RutaDestino = rutas[0];
            }
        }
    }
}