using System.ComponentModel;
using System.Windows;
using Comparador.App.ModelosVista;

namespace Comparador.App.Vistas;

public partial class VentanaClonado
{
    public VentanaClonado() => InitializeComponent();

    private ClonadoModeloVista Modelo => (ClonadoModeloVista)DataContext;

    private async void AlCargar(object sender, RoutedEventArgs e) => await Modelo.EjecutarAsync();

    /// <summary>Cerrar a mitad dejaría el disco a medias sin decirlo: primero hay que cancelar.</summary>
    private void AlCerrar(object? sender, CancelEventArgs e)
    {
        if (!Modelo.Terminado)
        {
            e.Cancel = true;
            Modelo.CancelarCommand.Execute(null);
        }
    }

    private void AlPulsarCerrar(object sender, RoutedEventArgs e) => Close();
}
