using System.Collections.Concurrent;
using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Ubicaciones;

namespace Comparador.Nucleo.Servicios;

public sealed class ResumenSincronizacion
{
    private int copiados;

    public int Copiados => copiados;

    public ConcurrentBag<ElementoComparado> Fallidos { get; } = [];

    public void ContarCopiado() => Interlocked.Increment(ref copiados);
}

/// <summary>
/// Copia lo que falta y reemplaza lo diferente, con tantas copias a la vez como diga <see cref="Concurrencia"/>.
/// Cada elemento cambia SU estado al terminar; nada recorre la lista entera por cada archivo, que era lo que
/// congelaba la versión anterior.
/// </summary>
public sealed class SincronizadorArchivos
{
    public Task<ResumenSincronizacion> SincronizarAsync(
        IReadOnlyList<ElementoComparado> elementos, bool verificar, int? hilosManuales, ProgresoOperacion progreso, CancellationToken cancelacion) =>
        Task.Run(() => Sincronizar(elementos, verificar, hilosManuales, progreso, cancelacion), cancelacion);

    private static async Task<ResumenSincronizacion> Sincronizar(
        IReadOnlyList<ElementoComparado> elementos, bool verificar, int? hilosManuales, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        var pendientes = elementos.Where(elemento => elemento.Seleccionado && elemento.SePuedeSincronizar).ToList();
        var archivos = pendientes.Where(elemento => !elemento.EsCarpeta).ToList();
        var ubicaciones = pendientes.Select(elemento => elemento.Par).Distinct()
            .ToDictionary(par => par, par => (Origen: CatalogoUbicaciones.Abrir(par.Origen), Destino: CatalogoUbicaciones.Abrir(par.Destino)));
        progreso.Hilos = Concurrencia.Decidir(hilosManuales, ubicaciones.Values.SelectMany(par => new[] { par.Origen, par.Destino }));
        progreso.IniciarFase("Copiando", pendientes.Count, archivos.Sum(archivo => BytesEsperados(archivo, verificar)));
        var resumen = new ResumenSincronizacion();
        CrearCarpetas(pendientes.Where(elemento => elemento.EsCarpeta), ubicaciones, progreso, resumen, cancelacion);
        var opciones = new ParallelOptions { MaxDegreeOfParallelism = progreso.Hilos.Hilos, CancellationToken = cancelacion };
        await Parallel.ForEachAsync(archivos, opciones, (archivo, token) =>
        {
            var (origen, destino) = ubicaciones[archivo.Par];

            return new ValueTask(CopiarAsync(archivo, origen, destino, verificar, progreso, resumen, token));
        });

        return resumen;
    }

    /// <summary>Copiar lee el archivo una vez; verificar relee la copia otra vez.</summary>
    private static long BytesEsperados(ElementoComparado archivo, bool verificar) => archivo.TamanoACopiar * (verificar ? 2 : 1);

    private static void CrearCarpetas(
        IEnumerable<ElementoComparado> carpetas, Dictionary<ParRutas, (IUbicacion Origen, IUbicacion Destino)> ubicaciones,
        ProgresoOperacion progreso, ResumenSincronizacion resumen, CancellationToken cancelacion)
    {
        foreach (var carpeta in carpetas.OrderBy(carpeta => carpeta.RutaRelativa.Length))
        {
            cancelacion.ThrowIfCancellationRequested();
            var archivo = progreso.Empezar(carpeta.RutaRelativa, 0, "Creando carpeta", carpeta.RutaOrigen, carpeta.RutaDestino);
            try
            {
                ubicaciones[carpeta.Par].Destino.CrearCarpeta(carpeta.RutaRelativa);
                MarcarSincronizado(carpeta, "Carpeta creada", resumen);
                progreso.Terminar(archivo, exito: true, carpeta.Motivo);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                MarcarFallido(carpeta, error, resumen);
                progreso.Terminar(archivo, exito: false, carpeta.Motivo);
            }
        }
    }

    private static async Task CopiarAsync(
        ElementoComparado elemento, IUbicacion origen, IUbicacion destino, bool verificar,
        ProgresoOperacion progreso, ResumenSincronizacion resumen, CancellationToken cancelacion)
    {
        var archivo = progreso.Empezar(elemento.RutaRelativa, elemento.TamanoACopiar, "Copiando", elemento.RutaOrigen, elemento.RutaDestino);
        var exito = false;
        try
        {
            await CopiaSegura.CopiarAsync(origen, destino, elemento.RutaRelativa, verificar, archivo, progreso, cancelacion);
            MarcarSincronizado(elemento, verificar ? "Copiado y verificado (SHA-256)" : "Copiado", resumen);
            exito = true;
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            // Cancelar corta la copia aquí mismo; el bucle ya no empieza otras y avisa al terminar. Atraparla
            // dentro evita que salga hacia el código de .NET, donde el depurador se detiene creyendo que es un error.
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            MarcarFallido(elemento, error, resumen);
        }
        finally
        {
            progreso.AjustarBytes(archivo, BytesEsperados(elemento, verificar));
            progreso.Terminar(archivo, exito, cancelacion.IsCancellationRequested && !exito ? "Cancelado" : elemento.Motivo);
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
        IOException when error.HResult == unchecked((int)0x80070035) => "la carpeta de red no responde",
        FileNotFoundException => "el archivo de origen ya no existe",
        System.Runtime.InteropServices.COMException => "el teléfono rechazó la operación (¿se desconectó o se bloqueó la pantalla?)",
        _ => error.Message,
    };
}
