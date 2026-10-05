using System.IO;
using System.Windows;
using System.Windows.Controls;
using Comparador.App.ModelosVista;
using Comparador.App.Servicios;
using Comparador.Nucleo.Ubicaciones;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Comparador.App.Vistas;

public partial class SelectorUbicacion : UserControl
{
    public SelectorUbicacion() => InitializeComponent();

    private SelectorUbicacionModeloVista Modelo => (SelectorUbicacionModeloVista)DataContext;

    /// <summary>Abre el menú enseguida con "Buscando..." y lo llena cuando llega la lista (buscar puede tardar).</summary>
    private async void AbrirDispositivos(object sender, RoutedEventArgs e)
    {
        MenuDispositivos.Items.Clear();
        MenuDispositivos.Items.Add(new MenuItem { Header = "Buscando dispositivos...", IsEnabled = false });
        MenuDispositivos.PlacementTarget = BotonDispositivos;
        MenuDispositivos.IsOpen = true;
        await Modelo.ActualizarDispositivosCommand.ExecuteAsync(null);
        MenuDispositivos.Items.Clear();
        foreach (var dispositivo in Modelo.Dispositivos)
        {
            MenuDispositivos.Items.Add(CrearEntrada(dispositivo));
        }

        if (MenuDispositivos.Items.Count == 0)
        {
            MenuDispositivos.Items.Add(new MenuItem { Header = "No hay dispositivos conectados", IsEnabled = false });
        }
    }

    private MenuItem CrearEntrada(DispositivoDisponible dispositivo)
    {
        var textos = new StackPanel();
        textos.Children.Add(new TextBlock { Text = dispositivo.Nombre });
        textos.Children.Add(new TextBlock { Text = dispositivo.Detalle, Style = (Style)FindResource("TextoSecundario") });
        var entrada = new MenuItem { Header = textos, Icon = new SymbolIcon { Symbol = IconosUbicacion.Para(dispositivo.Tipo) } };
        entrada.Click += (_, _) => Modelo.ElegirDispositivoCommand.Execute(dispositivo);

        return entrada;
    }

    private void AlArrastrar(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Soltar una carpeta encima la elige; soltar un archivo elige la carpeta donde está.</summary>
    private void AlSoltar(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } rutas)
        {
            Modelo.Ruta = Directory.Exists(rutas[0]) ? rutas[0] : Path.GetDirectoryName(rutas[0]) ?? rutas[0];
        }
    }
}
