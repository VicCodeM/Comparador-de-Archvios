using System.Buffers;
using System.Security.Cryptography;
using Comparador.Nucleo.Modelos;

namespace Comparador.Nucleo.Servicios;

/// <summary>Huella SHA-256 de un archivo, leída por bloques y sumando al progreso los bytes leídos.</summary>
public static class CalculadoraHash
{
    private const int TamanoBloque = 1024 * 1024;

    public static async Task<string> CalcularAsync(string ruta, ProgresoOperacion? progreso, CancellationToken cancelacion)
    {
        await using var flujo = AbrirParaLeer(ruta);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var bloque = ArrayPool<byte>.Shared.Rent(TamanoBloque);
        try
        {
            int leidos;
            while ((leidos = await flujo.ReadAsync(bloque.AsMemory(0, TamanoBloque), cancelacion)) > 0)
            {
                hash.AppendData(bloque, 0, leidos);
                progreso?.SumarBytes(leidos);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bloque);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>Lectura que no choca con quien tenga el archivo abierto para escribir (por ejemplo un log).</summary>
    public static FileStream AbrirParaLeer(string ruta) => new(Rutas.ParaIO(ruta), new FileStreamOptions
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.ReadWrite | FileShare.Delete,
        BufferSize = 0,
        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
    });
}
