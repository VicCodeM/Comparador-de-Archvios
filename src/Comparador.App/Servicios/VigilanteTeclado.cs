using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Comparador.App.Servicios;

/// <summary>
/// Se queda con el Ctrl+V del Explorador cuando lo copiado (o cortado) son archivos, para que lo pegue Espejo. Si
/// Espejo no puede, le devuelve el Ctrl+V a Windows: el usuario nunca se queda sin pegar. Fuera del Explorador, o
/// pegando texto, no toca nada.
/// </summary>
public sealed class VigilanteTeclado : IDisposable
{
    private const int GanchoTecladoBajoNivel = 13;
    private const int TeclaPulsada = 0x0100;
    private const int TeclaSoltada = 0x0101;
    private const int TeclaV = 0x56;
    private const int TeclaControl = 0x11;
    private const int TeclaMayusculas = 0x10;
    private const int TeclaAlt = 0x12;
    private const int TeclaWindowsIzquierda = 0x5B;
    private const int TeclaWindowsDerecha = 0x5C;
    private const uint ListaDeArchivos = 15;
    private const uint EntradaDeTeclado = 1;
    private const uint SoltarTecla = 0x0002;

    /// <summary>Marca el Ctrl+V que Espejo le devuelve a Windows, para no volver a quedárselo.</summary>
    private static readonly IntPtr MarcaPropia = 0x45535045;

    private readonly Func<IntPtr, bool> alPegar;
    private readonly ProcedimientoGancho procedimiento;
    private readonly Dispatcher hiloVentana;
    private readonly IntPtr gancho;
    private bool vRetenida;

    /// <param name="alPegar">Recibe la ventana del Explorador; true si Espejo se encargó del pegado.</param>
    public VigilanteTeclado(Func<IntPtr, bool> alPegar)
    {
        this.alPegar = alPegar;
        hiloVentana = Dispatcher.CurrentDispatcher;
        // Guardado en un campo: si el recolector se lo llevara, Windows llamaría a un puntero muerto.
        procedimiento = Recibir;
        gancho = SetWindowsHookEx(GanchoTecladoBajoNivel, procedimiento, GetModuleHandle(null), 0);
        if (gancho == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Windows no dejó vigilar el teclado (error {Marshal.GetLastWin32Error()}).");
        }
    }

    /// <summary>Debe ser rapidísimo: Windows espera a esta función antes de entregar cada tecla a todo el equipo.</summary>
    private IntPtr Recibir(int codigo, IntPtr mensaje, IntPtr datos)
    {
        if (codigo >= 0 && Marshal.ReadInt32(datos) == TeclaV && Marshal.ReadIntPtr(datos, 16) != MarcaPropia)
        {
            if ((int)mensaje == TeclaSoltada && vRetenida)
            {
                vRetenida = false;

                return 1;
            }

            if ((int)mensaje == TeclaPulsada && (vRetenida || EsPegarArchivos(GetForegroundWindow())))
            {
                // Al dejar la V apretada Windows repite la tecla: se pega una sola vez.
                if (!vRetenida)
                {
                    vRetenida = true;
                    var ventana = GetForegroundWindow();
                    hiloVentana.BeginInvoke(() => Pegar(ventana));
                }

                return 1;
            }
        }

        return CallNextHookEx(gancho, codigo, mensaje, datos);
    }

    private static bool EsPegarArchivos(IntPtr ventana) =>
        Apretada(TeclaControl) && !Apretada(TeclaMayusculas) && !Apretada(TeclaAlt)
        && !Apretada(TeclaWindowsIzquierda) && !Apretada(TeclaWindowsDerecha)
        && IsClipboardFormatAvailable(ListaDeArchivos) && CarpetaExplorador.EsListaDeArchivos(ventana);

    private void Pegar(IntPtr ventana)
    {
        bool hecho;
        try
        {
            hecho = alPegar(ventana);
        }
        catch (Exception error) when (error is COMException or InvalidCastException or ExternalException)
        {
            hecho = false;
        }

        if (!hecho)
        {
            DevolverAWindows();
        }
    }

    private static void DevolverAWindows()
    {
        var soltarControl = !Apretada(TeclaControl);
        Entrada[] teclas =
        [
            Tecla(TeclaControl, soltar: false),
            Tecla(TeclaV, soltar: false),
            Tecla(TeclaV, soltar: true),
            .. soltarControl ? [Tecla(TeclaControl, soltar: true)] : Array.Empty<Entrada>(),
        ];
        SendInput((uint)teclas.Length, teclas, Marshal.SizeOf<Entrada>());
    }

    private static Entrada Tecla(int tecla, bool soltar) => new()
    {
        Tipo = EntradaDeTeclado,
        Teclado = new EntradaTeclado { Codigo = (ushort)tecla, Banderas = soltar ? SoltarTecla : 0, Extra = MarcaPropia },
    };

    private static bool Apretada(int tecla) => GetAsyncKeyState(tecla) < 0;

    public void Dispose() => UnhookWindowsHookEx(gancho);

    private delegate IntPtr ProcedimientoGancho(int codigo, IntPtr mensaje, IntPtr datos);

    [StructLayout(LayoutKind.Sequential)]
    private struct Entrada
    {
        public uint Tipo;
        public EntradaTeclado Teclado;
    }

    /// <summary>KEYBDINPUT con relleno hasta el tamaño de MOUSEINPUT, el mayor de la unión de INPUT.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct EntradaTeclado
    {
        public ushort Codigo;
        public ushort Escaneo;
        public uint Banderas;
        public uint Momento;
        public IntPtr Extra;
        private readonly long relleno;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int tipo, ProcedimientoGancho procedimiento, IntPtr modulo, uint hilo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr gancho);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr gancho, int codigo, IntPtr mensaje, IntPtr datos);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? modulo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int tecla);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint formato);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint cantidad, Entrada[] entradas, int tamano);
}
