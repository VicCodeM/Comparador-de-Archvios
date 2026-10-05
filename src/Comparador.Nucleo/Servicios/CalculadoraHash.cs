using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Comparador.Nucleo.Ubicaciones;

namespace Comparador.Nucleo.Servicios;

/// <summary>Huella SHA-256 de un archivo, leída por bloques y avisando de cada bloque leído.</summary>
public static class CalculadoraHash
{
    private const int TamanoBloque = 4 * 1024 * 1024;
    private const int AlineacionSector = 4096;
    private const FileOptions SinCacheDeWindows = (FileOptions)0x20000000;

    /// <summary>Para cualquier ubicación (también teléfonos): lee por el camino normal.</summary>
    public static async Task<string> CalcularAsync(IUbicacion ubicacion, string relativa, Action<int> alLeer, CancellationToken cancelacion)
    {
        await using var flujo = new FlujoMedido(ubicacion.AbrirLectura(relativa), alLeer);
        await flujo.CopyToAsync(Stream.Null, TamanoBloque, cancelacion);

        return flujo.Huella();
    }

    /// <summary>
    /// Lee el archivo del disco mismo, saltándose la memoria de Windows. Recién copiado, el archivo sigue en esa
    /// memoria: leerlo por el camino normal devolvería lo que se escribió, no lo que quedó grabado, y la verificación
    /// no probaría nada.
    /// </summary>
    public static Task<string> CalcularDelDiscoAsync(string rutaIO, Action<int> alLeer, CancellationToken cancelacion) =>
        Task.Factory.StartNew(() => CalcularDelDisco(rutaIO, alLeer, cancelacion), cancelacion, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static unsafe string CalcularDelDisco(string rutaIO, Action<int> alLeer, CancellationToken cancelacion)
    {
        using var archivo = File.OpenHandle(rutaIO, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            SinCacheDeWindows | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        // Sin la memoria de Windows, cada lectura debe ir en memoria alineada al sector del disco.
        var bloque = NativeMemory.AlignedAlloc(TamanoBloque, AlineacionSector);
        try
        {
            var vista = new Span<byte>(bloque, TamanoBloque);
            long posicion = 0;
            int leidos;
            while ((leidos = RandomAccess.Read(archivo, vista, posicion)) > 0)
            {
                cancelacion.ThrowIfCancellationRequested();
                hash.AppendData(vista[..leidos]);
                alLeer(leidos);
                posicion += leidos;
            }
        }
        finally
        {
            NativeMemory.AlignedFree(bloque);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
