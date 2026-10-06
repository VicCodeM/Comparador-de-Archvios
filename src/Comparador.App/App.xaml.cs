using System.Windows;
using Comparador.App.ModelosVista;
using Comparador.App.Servicios;
using Comparador.Nucleo.Ubicaciones;

namespace Comparador.App;

/// <summary>
/// Arranque: una sola app abierta. Sin pedido se abre la ventana principal; con pedido (desde el Explorador o la
/// línea de comandos) se abre directo la ventanita de copia, como TeraCopy.
/// </summary>
public partial class App : Application
{
    private InstanciaUnica? instancia;

    /// <summary>La cola de copias rápidas: la usan el Explorador y el botón "Copiar archivos..." de la ventana principal.</summary>
    public static ColaDeCopias Cola { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instancia = InstanciaUnica.Tomar(e.Args);
        if (instancia is null)
        {
            Shutdown();
            return;
        }

        Apariencia.Aplicar(Configuracion.Cargar().Tema);
        Cola = new ColaDeCopias(new AccionesArchivo(new AvisosModeloVista()));
        instancia.Escuchar(Recibir, MostrarPrincipal);
        if (LineaDeComandos.Interpretar(e.Args) is { } pedido)
        {
            instancia.RecibirPropio(pedido, Recibir);
        }
        else
        {
            MostrarPrincipal();
        }
    }

    private void Recibir(PedidoExterno pedido)
    {
        if (LineaDeComandos.Completar(pedido) is { } listo)
        {
            Cola.Agregar(listo);
        }
        else if (Windows.Count == 0)
        {
            // Se canceló la elección del destino y no hay nada abierto: no dejar la app viva sin ventana.
            Shutdown();
        }
    }

    private void MostrarPrincipal()
    {
        if (MainWindow is MainWindow { IsLoaded: true } principal)
        {
            principal.WindowState = principal.WindowState == WindowState.Minimized ? WindowState.Normal : principal.WindowState;
            principal.Activate();
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    /// <summary>Suelta los teléfonos al salir: si no, Windows los deja "ocupados" hasta desconectarlos.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        CatalogoUbicaciones.CerrarTelefonos();
        instancia?.Dispose();
        base.OnExit(e);
    }
}
