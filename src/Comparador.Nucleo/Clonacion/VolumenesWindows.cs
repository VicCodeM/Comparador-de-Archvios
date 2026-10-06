using System.Runtime.InteropServices;
using System.Text;
using Comparador.Nucleo.Ubicaciones;
using Microsoft.Win32.SafeHandles;

namespace Comparador.Nucleo.Clonacion;

/// <summary>Un volumen de Windows ("\\?\Volume{...}\") y en qué disco y en qué byte empieza.</summary>
internal sealed record VolumenWindows(string Ruta, int Disco, long Inicio)
{
    /// <summary>Sin la barra final: así se abre el volumen en sí y no su carpeta raíz.</summary>
    public string Dispositivo => Ruta.TrimEnd('\\');

    /// <summary>Sistema de archivos y bytes ocupados; null si Windows no lo deja ver (EFI sin permisos, sin formato).</summary>
    public (string SistemaArchivos, long Usado)? Contenido()
    {
        var sistema = new StringBuilder(32);
        if (!GetVolumeInformation(Ruta, null, 0, out _, out _, out _, sistema, sistema.Capacity)
            || !GetDiskFreeSpaceEx(Ruta, out _, out var total, out var libre))
        {
            return null;
        }

        return (sistema.ToString(), (long)(total - libre));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumeInformationW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(
        string raiz, StringBuilder? nombre, int largoNombre, out uint serie, out uint largoMaximo, out uint banderas, StringBuilder sistema, int largoSistema);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetDiskFreeSpaceExW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string carpeta, out ulong libreParaUsuario, out ulong total, out ulong libre);
}

/// <summary>Todos los volúmenes del equipo, con letra o sin ella (EFI, recuperación), y dónde está cada uno.</summary>
internal static class VolumenesWindows
{
    private const uint ExtensionesDeVolumen = 0x560000;

    public static IReadOnlyList<VolumenWindows> Leer()
    {
        var volumenes = new List<VolumenWindows>();
        foreach (var ruta in Rutas())
        {
            using var volumen = DetectorDiscos.CreateFile(
                ruta.TrimEnd('\\'), 0, DetectorDiscos.CompartirLecturaEscritura, IntPtr.Zero, DetectorDiscos.AbrirExistente, 0, IntPtr.Zero);
            if (!volumen.IsInvalid)
            {
                volumenes.AddRange(Extensiones(volumen).Select(extension => new VolumenWindows(ruta, extension.Disco, extension.Inicio)));
            }
        }

        return volumenes;
    }

    /// <summary>VOLUME_DISK_EXTENTS: cantidad (4) + relleno (4) + extensiones de 24 bytes: disco (4), relleno (4), inicio (8).</summary>
    internal static IEnumerable<(int Disco, long Inicio)> Extensiones(SafeFileHandle volumen)
    {
        var respuesta = new byte[1024];
        if (!DetectorDiscos.DeviceIoControl(volumen, ExtensionesDeVolumen, null, 0, respuesta, respuesta.Length, out _, IntPtr.Zero))
        {
            return [];
        }

        return Enumerable.Range(0, BitConverter.ToInt32(respuesta, 0))
            .Select(indice => (BitConverter.ToInt32(respuesta, 8 + indice * 24), BitConverter.ToInt64(respuesta, 16 + indice * 24)))
            .ToList();
    }

    private static IEnumerable<string> Rutas()
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
                yield return new string(nombre, 0, Array.IndexOf(nombre, '\0'));
            }
            while (FindNextVolume(busqueda, nombre, nombre.Length));
        }
        finally
        {
            FindVolumeClose(busqueda);
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
