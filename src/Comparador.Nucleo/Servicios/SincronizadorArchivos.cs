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
/// Copia lo que falta y reemplaza lo diferente. Las copias a la vez parten de <see cref="Concurrencia"/> y, en automático,
/// las va ajustando <see cref="AjustadorHilos"/> según la velocidad que mide en este equipo.
/// Cada elemento cambia SU estado al terminar; nada recorre la lista entera por cada archivo, que era lo que
/// congelaba la versión anterior.
/// </summary>
public sealed class SincronizadorArchivos
{
    /// <summary>Lo bastante largo para que la velocidad medida no sea ruido, lo bastante corto para reaccionar pronto.</summary>
    private static readonly TimeSpan TramoDeMedicion = TimeSpan.FromSeconds(1.5);

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
        progreso.IniciarFase("Copiando", pendientes.Count, archivos.Sum(archivo => BytesEsperados(archivo, ubicaciones[archivo.Par], verificar)));
        var resumen = new ResumenSincronizacion();
        CrearCarpetas(pendientes.Where(elemento => elemento.EsCarpeta), ubicaciones, progreso, resumen, cancelacion);
        var limite = new LimiteDinamico(progreso.Hilos.Hilos);
        using var finAjuste = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        var ajuste = progreso.Hilos.SeAjusta ? AjustarMientrasCopiaAsync(limite, progreso, finAjuste.Token) : Task.CompletedTask;
        var opciones = new ParallelOptions { MaxDegreeOfParallelism = Concurrencia.Maximo, CancellationToken = cancelacion };
        try
        {
            await Parallel.ForEachAsync(archivos, opciones, async (archivo, token) =>
            {
                await limite.EsperarAsync(token);
                try
                {
                    var (origen, destino) = ubicaciones[archivo.Par];
                    await CopiarAsync(archivo, origen, destino, verificar, progreso, resumen, token);
                }
                finally
                {
                    limite.Liberar();
                }
            });
        }
        finally
        {
            await finAjuste.CancelAsync();
            await ajuste;
        }

        return resumen;
    }

    /// <summary>Cada tramo mide lo avanzado y deja que el ajustador suba o baje los hilos; la pantalla lo ve en vivo.</summary>
    private static async Task AjustarMientrasCopiaAsync(LimiteDinamico limite, ProgresoOperacion progreso, CancellationToken fin)
    {
        var ajustador = new AjustadorHilos(limite, Concurrencia.Minimo, Concurrencia.Maximo);
        using var reloj = new PeriodicTimer(TramoDeMedicion);
        var anterior = progreso.Instantanea();
        try
        {
            while (await reloj.WaitForNextTickAsync(fin))
            {
                var actual = progreso.Instantanea();
                var hilos = ajustador.Medir(
                    actual.BytesProcesados - anterior.BytesProcesados, actual.Procesados - anterior.Procesados, actual.Transcurrido - anterior.Transcurrido);
                progreso.Hilos = progreso.Hilos with { Hilos = hilos };
                anterior = actual;
            }
        }
        catch (OperationCanceledException)
        {
            // Terminó la copia (o se canceló): se deja de medir.
        }
    }

    private static long BytesEsperados(ElementoComparado archivo, (IUbicacion Origen, IUbicacion Destino) par, bool verificar) =>
        archivo.TamanoACopiar * CopiaSegura.Pasadas(par.Origen, par.Destino, verificar);

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
            progreso.AjustarBytes(archivo, BytesEsperados(elemento, (origen, destino), verificar));
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
        FileNotFoundException noEncontrado => $"no se encontró {Path.GetFileName(noEncontrado.FileName) ?? "el archivo"}",
        System.Runtime.InteropServices.COMException => "el teléfono rechazó la operación (¿se desconectó o se bloqueó la pantalla?)",
        _ => error.Message,
    };
}
