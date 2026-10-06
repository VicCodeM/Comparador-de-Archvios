using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Comparador.App.ModelosVista;
using Comparador.App.Servicios;
using Comparador.Nucleo.Modelos;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Comparador.App.Vistas;

/// <summary>La ventanita de una copia, como la de TeraCopy.</summary>
public partial class VentanaCopia
{
    public VentanaCopia()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is TrabajoCopiaModeloVista trabajo)
            {
                trabajo.Termino += AlTerminarTrabajo;
            }
        };
    }

    private TrabajoCopiaModeloVista Trabajo => (TrabajoCopiaModeloVista)DataContext;

    /// <summary>
    /// El menú "⋯" se arma al abrirlo, con la opción vigente marcada. Lo de "si ya existe" y "verificar" vale para
    /// las copias que aún no empezaron; "al terminar" se puede cambiar en cualquier momento.
    /// </summary>
    private void AbrirOpciones(object sender, RoutedEventArgs e)
    {
        MenuOpciones.Items.Clear();
        MenuOpciones.Items.Add(Submenu("Si el archivo ya existe", Trabajo.Reglas, regla => regla == Trabajo.SiYaExiste,
            regla => Trabajo.SiYaExiste = regla, Convertidores.ReglaTextoConverter.Texto, !Trabajo.EnCola));
        var verificar = new MenuItem { Header = "Verificar cada copia (SHA-256)", IsCheckable = true, IsChecked = Trabajo.Verificar, IsEnabled = Trabajo.EnCola };
        verificar.Click += (_, _) => Trabajo.Verificar = verificar.IsChecked;
        MenuOpciones.Items.Add(verificar);
        MenuOpciones.Items.Add(new Separator());
        MenuOpciones.Items.Add(Submenu("Al terminar", Trabajo.OpcionesAlTerminar, accion => accion == Trabajo.AlTerminar,
            accion => Trabajo.AlTerminar = accion, AccionAlTerminar.Nombre, bloqueado: false));
        MenuOpciones.Items.Add(new Separator());
        MenuOpciones.Items.Add(Accion("Abrir la carpeta destino", SymbolRegular.FolderOpen24, () => Trabajo.AbrirDestinoCommand.Execute(null)));
        MenuOpciones.Items.Add(Accion(Trabajo.VerDetalles ? "Ocultar detalles" : "Ver detalles", SymbolRegular.TextBulletListLtr24,
            () => Trabajo.VerDetalles = !Trabajo.VerDetalles));
        MenuOpciones.PlacementTarget = BotonOpciones;
        MenuOpciones.IsOpen = true;
    }

    private static MenuItem Submenu<T>(string titulo, IEnumerable<T> opciones, Func<T, bool> elegida, Action<T> elegir, Func<T, string> nombre, bool bloqueado)
    {
        var menu = new MenuItem { Header = titulo, IsEnabled = !bloqueado };
        foreach (var opcion in opciones)
        {
            var entrada = new MenuItem { Header = nombre(opcion), IsCheckable = true, IsChecked = elegida(opcion) };
            entrada.Click += (_, _) => elegir(opcion);
            menu.Items.Add(entrada);
        }

        return menu;
    }

    private static MenuItem Accion(string titulo, SymbolRegular icono, Action alPulsar)
    {
        var entrada = new MenuItem { Header = titulo, Icon = new SymbolIcon { Symbol = icono } };
        entrada.Click += (_, _) => alPulsar();

        return entrada;
    }

    private void AlTerminarTrabajo(TrabajoCopiaModeloVista trabajo)
    {
        if (trabajo.AlTerminar != AlTerminar.Nada && trabajo.Severidad != InfoBarSeverity.Warning && trabajo.Severidad != InfoBarSeverity.Error)
        {
            Dispatcher.InvokeAsync(Close);
        }
    }

    /// <summary>Cerrar mientras copia cancela la copia (sin dejar nada a medias), igual que el botón Cancelar.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!Trabajo.Terminado)
        {
            Trabajo.CancelarCommand.Execute(null);
        }

        base.OnClosing(e);
    }

    private void Cerrar(object sender, RoutedEventArgs e) => Close();
}
