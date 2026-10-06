using System.Windows;
using Comparador.App.ModelosVista;

namespace Comparador.App.Vistas;

public partial class VistaClonar
{
    public VistaClonar() => InitializeComponent();

    /// <summary>Cada vez que se abre la página se releen los discos: pudo conectarse una USB o una tarjeta.</summary>
    private void AlMostrarse(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && DataContext is ClonarModeloVista modelo)
        {
            modelo.ActualizarCommand.Execute(null);
        }
    }
}
