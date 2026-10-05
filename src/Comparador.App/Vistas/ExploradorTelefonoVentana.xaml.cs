using System.Windows;
using System.Windows.Input;
using Comparador.App.ModelosVista;

namespace Comparador.App.Vistas;

public partial class ExploradorTelefonoVentana
{
    public ExploradorTelefonoVentana() => InitializeComponent();

    private ExploradorTelefonoModeloVista Modelo => (ExploradorTelefonoModeloVista)DataContext;

    private void AlEntrar(object sender, MouseButtonEventArgs e) => Modelo.EntrarCommand.Execute(Lista.SelectedItem as string);

    private void AlPulsarTecla(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Modelo.EntrarCommand.Execute(Lista.SelectedItem as string);
        }
        else if (e.Key == Key.Back)
        {
            Modelo.SubirCommand.Execute(null);
        }
    }

    private void AlElegir(object sender, RoutedEventArgs e) => DialogResult = true;
}
