using System.Windows.Controls;
using System.Windows.Input;
using Comparador.App.ModelosVista;

namespace Comparador.App.Vistas;

public partial class VistaCopia : UserControl
{
    public VistaCopia() => InitializeComponent();

    private void AlDobleClic(object sender, MouseButtonEventArgs e)
    {
        if (ListaTerminados.DataContext is TerminadosModeloVista terminados && ListaTerminados.SelectedItem is FilaTerminada fila)
        {
            terminados.AbrirCopiaCommand.Execute(fila);
        }
    }
}
