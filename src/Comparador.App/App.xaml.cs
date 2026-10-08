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
    private ModoResidente? residente;

    /// <summary>La cola de copias rápidas: la usan el Explorador y el botón "Copiar archivos..." de la ventana principal.</summary>
    public static ColaDeCopias Cola { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args is ["--desinstalar"])
        {
            // Lo llama el desinstalador: nada de Espejo debe quedar en el Explorador ni en el arranque de Windows.
            IntegracionExplorador.Quitar();
            ModoResidente.ArrancarConWindows(false);
            Shutdown();
            return;
        }

        if (e.Args is ["--instalar", var opcion])
        {
            // Lo llama el instalador con las casillas marcadas: apuntan a este Espejo.exe, el ya instalado.
            Instalar(opcion);
            Shutdown();
            return;
        }

        if (e.Args is ["clonar", var origen, var destino, ..])
        {
            Clonar(origen, destino, verificar: !e.Args.Contains("--sin-verificar"), ajustar: e.Args.Contains("--ajustar"));
            return;
        }

        instancia = InstanciaUnica.Tomar(e.Args);
        if (instancia is null)
        {
            Shutdown();
            return;
        }

        var config = Configuracion.Cargar();
        Apariencia.Aplicar(config.Tema);
        Cola = new ColaDeCopias(new AccionesArchivo(new AvisosModeloVista()));
        instancia.Escuchar(Recibir, MostrarPrincipal);
        CambiarResidente(config.PegarConEspejo);
        if (LineaDeComandos.Interpretar(e.Args) is { } pedido)
        {
            instancia.RecibirPropio(pedido, Recibir);
        }
        else if (residente is null || e.Args is not [ModoResidente.Argumento])
        {
            MostrarPrincipal();
        }
    }

    /// <summary>"menu": clic derecho y menú del arrastre. "pegar": Ctrl+V del Explorador con Espejo y arranque con Windows.</summary>
    private static void Instalar(string opcion)
    {
        if (opcion == "menu")
        {
            IntegracionExplorador.Instalar();
        }
        else if (opcion == "pegar")
        {
            (Configuracion.Cargar() with { PegarConEspejo = true }).Guardar();
            ModoResidente.ArrancarConWindows(true);
        }
    }

    /// <summary>
    /// El Espejo que se abre como administrador desde la página Clonar: solo su ventanita, sin la ventana principal ni
    /// la cola de copias, y se cierra al cerrarla.
    /// </summary>
    private void Clonar(string origen, string destino, bool verificar, bool ajustar)
    {
        Apariencia.Aplicar(Configuracion.Cargar().Tema);
        MainWindow = new Vistas.VentanaClonado
        {
            DataContext = new ClonadoModeloVista(Nucleo.Clonacion.ExtremoClon.Interpretar(origen), Nucleo.Clonacion.ExtremoClon.Interpretar(destino), verificar, ajustar),
        };
        MainWindow.Show();
    }

    /// <summary>Activa o quita el modo en segundo plano (Ctrl+V del Explorador, icono junto al reloj, arranque con Windows).</summary>
    public void CambiarResidente(bool activo)
    {
        residente?.Dispose();
        residente = activo ? new ModoResidente(PegarDesdeExplorador, MostrarPrincipal, () => Shutdown()) : null;
        ModoResidente.ArrancarConWindows(activo);
        ShutdownMode = activo ? ShutdownMode.OnExplicitShutdown : ShutdownMode.OnLastWindowClose;
    }

    /// <summary>False si la carpeta no es real (Este equipo, un .zip) o lo copiado ya no son archivos: pega Windows.</summary>
    private bool PegarDesdeExplorador(IntPtr ventana)
    {
        if (CarpetaExplorador.Actual(ventana) is not { } carpeta || LineaDeComandos.Interpretar(["pegar", carpeta]) is not { } pedido)
        {
            return false;
        }

        Recibir(pedido);

        return true;
    }

    private void Recibir(PedidoExterno pedido)
    {
        if (LineaDeComandos.Completar(pedido) is { } listo)
        {
            Cola.Agregar(listo);
        }
        else if (Windows.Count == 0 && residente is null)
        {
            // Se canceló la elección del destino y no hay nada abierto: no dejar la app viva sin ventana.
            Shutdown();
        }
    }

    private void MostrarPrincipal()
    {
        if (MainWindow is not MainWindow { IsLoaded: true } principal)
        {
            principal = new MainWindow();
            MainWindow = principal;
            principal.Show();
        }

        principal.WindowState = WindowState.Maximized;
        TraerAlFrente(principal);
    }

    /// <summary>
    /// Desde segundo plano Windows no deja ponerse delante con Activate: la ventana quedaba detrás de las demás.
    /// Subirla un instante por encima de todo la trae al frente sin dejarla fija ahí.
    /// </summary>
    private static void TraerAlFrente(Window ventana)
    {
        ventana.Topmost = true;
        ventana.Activate();
        ventana.Topmost = false;
        ventana.Focus();
    }

    /// <summary>Suelta los teléfonos al salir: si no, Windows los deja "ocupados" hasta desconectarlos.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        CatalogoUbicaciones.CerrarTelefonos();
        residente?.Dispose();
        instancia?.Dispose();
        base.OnExit(e);
    }
}
