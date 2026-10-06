using System.ComponentModel;
using System.Runtime.InteropServices;
using Comparador.Nucleo.Ubicaciones;
using Microsoft.Win32.SafeHandles;

namespace Comparador.Nucleo.Clonacion;

/// <summary>
/// Un disco físico leído o escrito sector a sector. Exige administrador. Para escribir, antes bloquea y desmonta
/// todos sus volúmenes (con letra o sin ella): si Windows los tuviera montados, rechazaría las escrituras dentro de
/// ellos o, peor, las mezclaría con las suyas. Los bloqueos se sueltan al terminar y Windows vuelve a leer el disco.
/// </summary>
internal sealed class DiscoCrudo : IDisposable
{
    private const uint Leer = 0x80000000;
    private const uint Escribir = 0x40000000;
    private const uint EscrituraDirecta = 0x80000000;
    private const uint BloquearVolumen = 0x90018;
    private const uint DesmontarVolumen = 0x90020;
    private const uint ActualizarPropiedades = 0x70140;
    private const uint ExtensionesDeVolumen = 0x560000;
    private const int AccesoDenegado = 5;

    private readonly SafeFileHandle disco;
    private readonly List<SafeFileHandle> volumenesBloqueados;

    private DiscoCrudo(SafeFileHandle disco, List<SafeFileHandle> volumenesBloqueados)
    {
        this.disco = disco;
        this.volumenesBloqueados = volumenesBloqueados;
    }

    public static DiscoCrudo AbrirLectura(int numero) => new(Abrir($@"\\.\PhysicalDrive{numero}", Leer), []);

    public static DiscoCrudo AbrirEscritura(int numero)
    {
        var bloqueados = BloquearVolumenesDe(numero);

        return new DiscoCrudo(Abrir($@"\\.\PhysicalDrive{numero}", Leer | Escribir), bloqueados);
    }

    public int LeerEn(Span<byte> destino, long posicion) => RandomAccess.Read(disco, destino, posicion);

    public void EscribirEn(ReadOnlySpan<byte> datos, long posicion) => RandomAccess.Write(disco, datos, posicion);

    /// <summary>Que Windows relea la tabla de particiones nueva (si no, sigue viendo la vieja hasta reconectar).</summary>
    public void AvisarCambios() => DetectorDiscos.DeviceIoControl(disco, ActualizarPropiedades, null, 0, [], 0, out _, IntPtr.Zero);

    private static SafeFileHandle Abrir(string ruta, uint acceso)
    {
        var manejador = DetectorDiscos.CreateFile(ruta, acceso, DetectorDiscos.CompartirLecturaEscritura, IntPtr.Zero, DetectorDiscos.AbrirExistente, EscrituraDirecta, IntPtr.Zero);
        if (manejador.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            manejador.Dispose();
            throw new UnauthorizedAccessException(error == AccesoDenegado
                ? "Para leer o escribir un disco entero hace falta abrir Espejo como administrador."
                : $"No se pudo abrir {ruta}: {new Win32Exception(error).Message}");
        }

        return manejador;
    }

    private static List<SafeFileHandle> BloquearVolumenesDe(int numero)
    {
        var bloqueados = new List<SafeFileHandle>();
        foreach (var volumen in Volumenes())
        {
            var manejador = DetectorDiscos.CreateFile(volumen, Leer | Escribir, DetectorDiscos.CompartirLecturaEscritura, IntPtr.Zero, DetectorDiscos.AbrirExistente, 0, IntPtr.Zero);
            if (manejador.IsInvalid || !EstaEnDisco(manejador, numero))
            {
                manejador.Dispose();
                continue;
            }

            // Si algún programa lo usa, el bloqueo falla; desmontar igual lo libera (sus archivos abiertos se invalidan).
            DetectorDiscos.DeviceIoControl(manejador, BloquearVolumen, null, 0, [], 0, out _, IntPtr.Zero);
            DetectorDiscos.DeviceIoControl(manejador, DesmontarVolumen, null, 0, [], 0, out _, IntPtr.Zero);
            bloqueados.Add(manejador);
        }

        return bloqueados;
    }

    private static bool EstaEnDisco(SafeFileHandle volumen, int numero)
    {
        var respuesta = new byte[1024];
        if (!DetectorDiscos.DeviceIoControl(volumen, ExtensionesDeVolumen, null, 0, respuesta, respuesta.Length, out _, IntPtr.Zero))
        {
            return false;
        }

        // VOLUME_DISK_EXTENTS: cantidad (4) + relleno (4) + extensiones de 24 bytes con el número de disco al inicio.
        var cantidad = BitConverter.ToInt32(respuesta, 0);

        return Enumerable.Range(0, cantidad).Any(indice => BitConverter.ToInt32(respuesta, 8 + indice * 24) == numero);
    }

    /// <summary>Todos los volúmenes del equipo como "\\?\Volume{...}" (sin la barra final, para abrir el volumen y no su raíz).</summary>
    private static IEnumerable<string> Volumenes()
    {
        var nombre = new char[64];
        var busqueda = FindFirstVolume(nombre, nombre.Length);
        if (busqueda == new IntPtr(-1))
        {
            yield break;
        }

        try
        {
            do
            {
                yield return new string(nombre, 0, Array.IndexOf(nombre, '\0')).TrimEnd('\\');
            }
            while (FindNextVolume(busqueda, nombre, nombre.Length));
        }
        finally
        {
            FindVolumeClose(busqueda);
        }
    }

    public void Dispose()
    {
        disco.Dispose();
        foreach (var volumen in volumenesBloqueados)
        {
            volumen.Dispose();
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindFirstVolumeW")]
    private static extern IntPtr FindFirstVolume(char[] nombre, int largo);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindNextVolumeW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextVolume(IntPtr busqueda, char[] nombre, int largo);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindVolumeClose(IntPtr busqueda);
}
