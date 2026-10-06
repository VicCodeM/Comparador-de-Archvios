using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Comparador.App.Servicios;

/// <summary>
/// Qué carpeta muestra la ventana del Explorador que está al frente (en Windows 11, la pestaña activa) y si el
/// foco está en su lista de archivos. Solo sirve en el hilo de la ventana: habla con el Explorador por COM.
/// </summary>
public static class CarpetaExplorador
{
    private static readonly Guid ServicioNavegadorPrincipal = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid InterfazNavegadorShell = new("000214E2-0000-0000-C000-000000000046");

    /// <summary>Una ventana del Explorador o el escritorio, con el foco en la lista de archivos.</summary>
    public static bool EsListaDeArchivos(IntPtr ventana)
    {
        if (!EsExplorador(ventana) && !EsEscritorio(ventana))
        {
            return false;
        }

        // En la barra de dirección, la búsqueda o al renombrar, Ctrl+V pega texto: eso es de Windows.
        var hilo = GetWindowThreadProcessId(ventana, out _);
        var info = new InfoHiloVentana { Tamano = Marshal.SizeOf<InfoHiloVentana>() };

        return GetGUIThreadInfo(hilo, ref info) && Clase(GetParent(info.Foco)) == "SHELLDLL_DefView";
    }

    /// <summary>La carpeta real que se ve; null si es virtual (Este equipo, un .zip, un teléfono) o no se pudo saber.</summary>
    public static string? Actual(IntPtr ventana)
    {
        var ruta = EsEscritorio(ventana) ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : DeLaPestana(ventana);

        return ruta is not null && Directory.Exists(ruta) ? ruta : null;
    }

    private static bool EsExplorador(IntPtr ventana) => Clase(ventana) is "CabinetWClass" or "ExploreWClass";

    private static bool EsEscritorio(IntPtr ventana) => Clase(ventana) is "Progman" or "WorkerW";

    private static string? DeLaPestana(IntPtr ventana)
    {
        // Con pestañas, todas comparten la ventana: la activa es la primera "ShellTabWindowClass".
        var pestana = FindWindowEx(ventana, IntPtr.Zero, "ShellTabWindowClass", null);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        try
        {
            foreach (var abierta in shell.Windows())
            {
                if ((IntPtr)(long)abierta.HWND == ventana && (pestana == IntPtr.Zero || VentanaDe(abierta) == pestana))
                {
                    return (string)abierta.Document.Folder.Self.Path;
                }
            }
        }
        catch (COMException)
        {
            // El Explorador se cerró o está ocupado: que pegue Windows.
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }

        return null;
    }

    private static IntPtr VentanaDe(object abierta)
    {
        var servicio = ServicioNavegadorPrincipal;
        var interfaz = InterfazNavegadorShell;
        if (abierta is not IProveedorServicios proveedor || proveedor.QueryService(ref servicio, ref interfaz, out var navegador) != 0)
        {
            return IntPtr.Zero;
        }

        return ((IVentanaOle)navegador).GetWindow(out var pestana) == 0 ? pestana : IntPtr.Zero;
    }

    private static string Clase(IntPtr ventana)
    {
        var nombre = new StringBuilder(64);

        return GetClassName(ventana, nombre, nombre.Capacity) > 0 ? nombre.ToString() : string.Empty;
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IProveedorServicios
    {
        [PreserveSig]
        int QueryService(ref Guid servicio, ref Guid interfaz, [MarshalAs(UnmanagedType.Interface)] out object resultado);
    }

    /// <summary>El primer método de IShellBrowser (hereda de IOleWindow): la ventana de la pestaña.</summary>
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVentanaOle
    {
        [PreserveSig]
        int GetWindow(out IntPtr ventana);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InfoHiloVentana
    {
        public int Tamano;
        public int Estado;
        public IntPtr Activa;
        public IntPtr Foco;
        public IntPtr Captura;
        public IntPtr Menu;
        public IntPtr Movimiento;
        public IntPtr Cursor;
        public int CursorIzquierda;
        public int CursorArriba;
        public int CursorDerecha;
        public int CursorAbajo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr ventana, StringBuilder nombre, int capacidad);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr ventana);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr padre, IntPtr despuesDe, string clase, string? titulo);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr ventana, out uint proceso);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint hilo, ref InfoHiloVentana info);
}
