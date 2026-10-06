using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Comparador.App.Servicios;

/// <summary>
/// El icono de Espejo junto al reloj mientras se queda en segundo plano. Clic: abre la ventana; clic derecho: menú.
/// Hecho con la función de Windows directamente: el paquete de bandeja de WPF-UI no es compatible con .NET 10.
/// </summary>
public sealed class Bandeja : IDisposable
{
    private const int MensajeDelIcono = 0x8000 + 1;
    private const int ClicIzquierdoSoltado = 0x0202;
    private const int ClicDerechoSoltado = 0x0205;
    private const uint Agregar = 0;
    private const uint Borrar = 2;
    private const uint ConMensaje = 0x1;
    private const uint ConIcono = 0x2;
    private const uint ConTexto = 0x4;

    /// <summary>Windows lo manda cuando el Explorador se reinicia y la barra de tareas vuelve a nacer sin iconos.</summary>
    private static readonly uint BarraDeTareasCreada = RegisterWindowMessage("TaskbarCreated");

    private readonly HwndSource receptor;
    private readonly IntPtr icono;
    private readonly Action alAbrir;
    private readonly ContextMenu menu;

    public Bandeja(Action alAbrir, Action alSalir)
    {
        this.alAbrir = alAbrir;
        receptor = new HwndSource(new HwndSourceParameters("Espejo.Bandeja") { Width = 0, Height = 0, WindowStyle = 0 });
        receptor.AddHook(AlRecibirMensaje);
        ExtractIconEx(Environment.ProcessPath!, 0, IntPtr.Zero, out icono, 1);
        menu = new ContextMenu();
        menu.Items.Add(Opcion("Abrir Espejo", alAbrir));
        menu.Items.Add(new Separator());
        menu.Items.Add(Opcion("Salir (Ctrl+V vuelve a ser de Windows)", alSalir));
        Avisar(Agregar);
    }

    private static MenuItem Opcion(string texto, Action accion)
    {
        var opcion = new MenuItem { Header = texto };
        opcion.Click += (_, _) => accion();

        return opcion;
    }

    private void Avisar(uint accion)
    {
        var datos = Datos();
        Shell_NotifyIcon(accion, ref datos);
    }

    private DatosIcono Datos() => new()
    {
        Tamano = Marshal.SizeOf<DatosIcono>(),
        Ventana = receptor.Handle,
        Identificador = 1,
        Banderas = ConMensaje | ConIcono | ConTexto,
        MensajeDeVuelta = MensajeDelIcono,
        Icono = icono,
        Texto = "Espejo: pega con Ctrl+V en el Explorador",
    };

    private IntPtr AlRecibirMensaje(IntPtr ventana, int mensaje, IntPtr parametro, IntPtr datos, ref bool atendido)
    {
        if (mensaje == BarraDeTareasCreada)
        {
            Avisar(Agregar);
        }
        else if (mensaje == MensajeDelIcono && (int)datos == ClicIzquierdoSoltado)
        {
            alAbrir();
        }
        else if (mensaje == MensajeDelIcono && (int)datos == ClicDerechoSoltado)
        {
            // Sin pasar al frente, el menú no se cierra al hacer clic fuera de él.
            SetForegroundWindow(receptor.Handle);
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Avisar(Borrar);
        DestroyIcon(icono);
        receptor.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DatosIcono
    {
        public int Tamano;
        public IntPtr Ventana;
        public uint Identificador;
        public uint Banderas;
        public int MensajeDeVuelta;
        public IntPtr Icono;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Texto;
        public uint Estado;
        public uint MascaraEstado;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Aviso;
        public uint VersionOEspera;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string TituloAviso;
        public uint BanderasAviso;
        public Guid Guia;
        public IntPtr IconoAviso;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint accion, ref DatosIcono datos);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string archivo, int indice, IntPtr grandes, out IntPtr pequeno, uint cantidad);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icono);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string nombre);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr ventana);
}
