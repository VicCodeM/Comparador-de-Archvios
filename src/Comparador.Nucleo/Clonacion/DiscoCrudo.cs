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
        foreach (var volumen in VolumenesWindows.Leer().Where(volumen => volumen.Disco == numero).DistinctBy(volumen => volumen.Ruta))
        {
            var manejador = DetectorDiscos.CreateFile(volumen.Dispositivo, Leer | Escribir, DetectorDiscos.CompartirLecturaEscritura, IntPtr.Zero, DetectorDiscos.AbrirExistente, 0, IntPtr.Zero);
            if (manejador.IsInvalid)
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

    public void Dispose()
    {
        disco.Dispose();
        foreach (var volumen in volumenesBloqueados)
        {
            volumen.Dispose();
        }
    }
}
