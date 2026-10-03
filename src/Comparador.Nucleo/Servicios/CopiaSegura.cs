using System.Buffers;
using Comparador.Nucleo.Modelos;

namespace Comparador.Nucleo.Servicios;

/// <summary>El archivo está abierto por otro programa; dice cuál.</summary>
public sealed class ArchivoEnUsoException(string programas, Exception interna)
    : IOException($"Está abierto en {programas}", interna);

/// <summary>La copia no es idéntica al origen según SHA-256.</summary>
public sealed class CopiaNoIdenticaException() : IOException("La verificación SHA-256 falló: la copia no es idéntica al origen");

/// <summary>
/// Copia un archivo sin dejarlo a medias: primero a un temporal junto al destino, opcionalmente lo verifica con
/// SHA-256 y solo entonces reemplaza el destino de una vez. Conserva fechas y atributos del origen.
/// </summary>
public static class CopiaSegura
{
    public const string ExtensionTemporal = ".comparador-tmp";
    private const int TamanoBloque = 4 * 1024 * 1024;
    private const int Reintentos = 3;
    private const int HResultEnUso = unchecked((int)0x80070020);
    private const int HResultBloqueoParcial = unchecked((int)0x80070021);

    public static async Task CopiarAsync(string origen, string destino, bool verificar, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        for (var intento = 1; ; intento++)
        {
            try
            {
                await IntentarCopiarAsync(origen, destino, verificar, progreso, cancelacion);
                return;
            }
            catch (Exception error) when (EstaEnUso(error, origen, destino) && intento < Reintentos)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500 * intento), cancelacion);
            }
            catch (Exception error) when (EstaEnUso(error, origen, destino))
            {
                throw new ArchivoEnUsoException(DetectorBloqueos.Describir(origen, destino), error);
            }
        }
    }

    /// <summary>
    /// Reemplazar un archivo que otro programa tiene abierto NO da "en uso": Windows responde "acceso denegado".
    /// Por eso, ante un acceso denegado se pregunta a Windows si alguien lo tiene abierto antes de culpar a los permisos.
    /// </summary>
    private static bool EstaEnUso(Exception error, string origen, string destino) => error switch
    {
        IOException io => io.HResult is HResultEnUso or HResultBloqueoParcial,
        UnauthorizedAccessException => DetectorBloqueos.QuienLoUsa(destino).Count > 0 || DetectorBloqueos.QuienLoUsa(origen).Count > 0,
        _ => false,
    };

    private static async Task IntentarCopiarAsync(string origen, string destino, bool verificar, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        var origenIO = Rutas.ParaIO(origen);
        var destinoIO = Rutas.ParaIO(destino);
        var temporalIO = destinoIO + ExtensionTemporal;
        Directory.CreateDirectory(Path.GetDirectoryName(destinoIO)!);
        try
        {
            await CopiarContenidoAsync(origenIO, temporalIO, progreso, cancelacion);
            if (verificar)
            {
                await VerificarAsync(origenIO, temporalIO, progreso, cancelacion);
            }

            Reemplazar(origenIO, temporalIO, destinoIO);
        }
        catch
        {
            BorrarSiExiste(temporalIO);
            throw;
        }
    }

    private static async Task CopiarContenidoAsync(string origenIO, string temporalIO, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        await using var lectura = CalculadoraHash.AbrirParaLeer(origenIO);
        await using var escritura = new FileStream(temporalIO, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 0,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            PreallocationSize = lectura.Length,
        });
        var bloque = ArrayPool<byte>.Shared.Rent(TamanoBloque);
        try
        {
            int leidos;
            while ((leidos = await lectura.ReadAsync(bloque.AsMemory(0, TamanoBloque), cancelacion)) > 0)
            {
                await escritura.WriteAsync(bloque.AsMemory(0, leidos), cancelacion);
                progreso.SumarBytes(leidos);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bloque);
        }
    }

    private static async Task VerificarAsync(string origenIO, string temporalIO, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        var huellaOrigen = await CalculadoraHash.CalcularAsync(origenIO, progreso, cancelacion);
        var huellaCopia = await CalculadoraHash.CalcularAsync(temporalIO, progreso, cancelacion);
        if (huellaOrigen != huellaCopia)
        {
            throw new CopiaNoIdenticaException();
        }
    }

    private static void Reemplazar(string origenIO, string temporalIO, string destinoIO)
    {
        File.SetCreationTime(temporalIO, File.GetCreationTime(origenIO));
        File.SetLastWriteTime(temporalIO, File.GetLastWriteTime(origenIO));
        if (File.Exists(destinoIO))
        {
            File.SetAttributes(destinoIO, FileAttributes.Normal);
        }

        File.Move(temporalIO, destinoIO, overwrite: true);
        File.SetAttributes(destinoIO, File.GetAttributes(origenIO));
    }

    private static void BorrarSiExiste(string rutaIO)
    {
        try
        {
            File.Delete(rutaIO);
        }
        catch (IOException)
        {
            // Si no se puede borrar el temporal no es grave: se reintenta en la siguiente copia.
        }
    }
}
