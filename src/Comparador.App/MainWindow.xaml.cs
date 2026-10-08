using System.ComponentModel;
using System.Windows;
using Comparador.App.ModelosVista;
using Comparador.App.Servicios;

namespace Comparador.App;

public partial class MainWindow
{
    private const double AnchoPlegado = 60;
    private const double AnchoMinimoCredito = 150;
    private GridLength anchoDesplegado = new(280);

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Apariencia.Aplicar(((PrincipalModeloVista)DataContext).Tema);
    }

    /// <summary>Cerrar con una copia en marcha pregunta antes; si confirma, la copia se cancela.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        var principal = (PrincipalModeloVista)DataContext;
        Dialogos.ConfirmarCierreSiCopia(this, e, principal.Copiando, () => principal.CancelarCommand.Execute(null));
        base.OnClosing(e);
    }

    /// <summary>Con el menú angosto (solo iconos) el crédito no cabe y se partiría letra por letra: se oculta.</summary>
    private void AlCambiarAnchoMenu(object sender, SizeChangedEventArgs e) =>
        Credito.Visibility = e.NewSize.Width < AnchoMinimoCredito ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Pliega el menú a solo iconos para dar todo el ancho a la tabla; al desplegar vuelve al ancho que tenía.</summary>
    private void PlegarMenu(object sender, RoutedEventArgs e)
    {
        if (ColumnaMenu.ActualWidth > AnchoPlegado + 1)
        {
            anchoDesplegado = ColumnaMenu.Width;
            ColumnaMenu.Width = new GridLength(AnchoPlegado);
            return;
        }

        ColumnaMenu.Width = anchoDesplegado;
    }
}
