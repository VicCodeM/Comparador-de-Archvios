using System.Collections.Concurrent;
using Comparador.Nucleo.Modelos;

namespace Comparador.Nucleo.Servicios;

public sealed class ResumenSincronizacion
{
    private int copiados;

    public int Copiados => copiados;

    public ConcurrentBag<ElementoComparado> Fallidos { get; } = [];

    public void ContarCopiado() => Interlocked.Increment(ref copiados);
}

/// <summary>
/// Copia lo que falta y reemplaza lo diferente. Cada elemento cambia SU estado al terminar (la lista se repinta
/// sola fila a fila); nada recorre la lista entera por cada archivo, que era lo que congelaba la versión anterior.
/// </summary>
public sealed class SincronizadorArchivos
{
    private const int CopiasSimultaneas = 2;

    public async Task<ResumenSincronizacion> SincronizarAsync(
        IReadOnlyList<ElementoComparado> elementos, bool verificar, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        var pendientes = elementos.Where(elemento => elemento.Seleccionado && elemento.SePuedeSincronizar).ToList();
        var archivos = pendientes.Where(elemento => !elemento.EsCarpeta).ToList();
        var lecturasPorByte = verificar ? 3 : 1;
        progreso.IniciarFase("Sincronizando", pendientes.Count, archivos.Sum(archivo => archivo.TamanoACopiar) * lecturasPorByte);
        var resumen = new ResumenSincronizacion();
        await Task.Run(() => CrearCarpetas(pendientes.Where(elemento => elemento.EsCarpeta), progreso, resumen, cancelacion), cancelacion);
        var opciones = new ParallelOptions { MaxDegreeOfParallelism = CopiasSimultaneas, CancellationToken = cancelacion };
        await Parallel.ForEachAsync(archivos, opciones, (archivo, token) =>
            new ValueTask(CopiarAsync(archivo, verificar, progreso, resumen, token)));

        return resumen;
    }

    private static void CrearCarpetas(IEnumerable<ElementoComparado> carpetas, ProgresoOperacion progreso, ResumenSincronizacion resumen, CancellationToken cancelacion)
    {
        foreach (var carpeta in carpetas.OrderBy(carpeta => carpeta.RutaRelativa.Length))
        {
            cancelacion.ThrowIfCancellationRequested();
            Ejecutar(carpeta, resumen, () => Directory.CreateDirectory(Rutas.ParaIO(carpeta.RutaDestino)));
            progreso.Avanzar();
        }
    }

    private static async Task CopiarAsync(ElementoComparado archivo, bool verificar, ProgresoOperacion progreso, ResumenSincronizacion resumen, CancellationToken cancelacion)
    {
        progreso.MarcarActual(archivo.RutaRelativa);
        try
        {
            await CopiaSegura.CopiarAsync(archivo.RutaOrigen, archivo.RutaDestino, verificar, progreso, cancelacion);
            MarcarSincronizado(archivo, verificar ? "Copiado y verificado (SHA-256)" : "Copiado", resumen);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            MarcarFallido(archivo, error, resumen);
        }
        finally
        {
            progreso.Avanzar();
        }
    }

    private static void Ejecutar(ElementoComparado elemento, ResumenSincronizacion resumen, Action accion)
    {
        try
        {
            accion();
            MarcarSincronizado(elemento, "Carpeta creada", resumen);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            MarcarFallido(elemento, error, resumen);
        }
    }

    private static void MarcarSincronizado(ElementoComparado elemento, string motivo, ResumenSincronizacion resumen)
    {
        elemento.Estado = EstadoElemento.Coincide;
        elemento.Seleccionado = false;
        elemento.Motivo = motivo;
        resumen.ContarCopiado();
    }

    private static void MarcarFallido(ElementoComparado elemento, Exception error, ResumenSincronizacion resumen)
    {
        elemento.Motivo = "No se pudo copiar: " + DescribirError(error);
        resumen.Fallidos.Add(elemento);
    }

    public static string DescribirError(Exception error) => error switch
    {
        ArchivoEnUsoException or CopiaNoIdenticaException => error.Message,
        UnauthorizedAccessException => "sin permiso para escribir en el destino",
        IOException when error.HResult == unchecked((int)0x80070070) => "no queda espacio en el disco de destino",
        FileNotFoundException => "el archivo de origen ya no existe",
        _ => error.Message,
    };
}
