using System.Windows;

namespace Comparador.App.Vistas;

/// <summary>Si el destino es un disco, hay que escribir BORRAR: un clic despistado no debe poder borrar un disco.</summary>
public partial class ConfirmarClonVentana
{
    private const string Palabra = "BORRAR";

    private readonly bool borraDatos;

    public ConfirmarClonVentana(string resumen, bool borraDatos)
    {
        InitializeComponent();
        this.borraDatos = borraDatos;
        Resumen.Text = resumen;
        PanelBorrar.Visibility = borraDatos ? Visibility.Visible : Visibility.Collapsed;
        BotonClonar.IsEnabled = !borraDatos;
    }

    private void AlEscribir(object sender, RoutedEventArgs e) =>
        BotonClonar.IsEnabled = !borraDatos || string.Equals(Confirmacion.Text.Trim(), Palabra, StringComparison.OrdinalIgnoreCase);

    private void AlClonar(object sender, RoutedEventArgs e) => DialogResult = true;
}
