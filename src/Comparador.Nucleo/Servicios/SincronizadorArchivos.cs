using System.Collections.Concurrent;
using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Ubicaciones;

namespace Comparador.Nucleo.Servicios;

public sealed class ResumenSincronizacion
{
    private int copiados;
    private int saltados;

    public int Copiados => copiados;

    public int Saltados => saltados;

    public ConcurrentBag<ElementoComparado> Fallidos { get; } = [];

    public void ContarCopiado() => Interlocked.Increment(ref copiados);

    public void ContarSaltado() => Interlocked.Increment(ref saltados);
}

/// <summary>
/// Copia lo que falta y reemplaza lo diferente según la regla elegida. Las copias a la vez parten de
/// <see cref="Concurrencia"/> y, en automático, las va ajustando <see cref="AjustadorHilos"/> según la velocidad que mide
/// en este equipo. Se puede pausar, saltar un archivo o cancelar todo. Cada elemento cambia SU estado al terminar;
/// nada recorre la lista entera por cada archivo, que era lo que congelaba la versión anterior.
/// </summary>
public sealed class SincronizadorArchivos
{
    /// <summary>Lo bastante largo para que la velocidad medida no sea ruido, lo bastante corto para reaccionar pronto.</summary>
    private static readonly TimeSpan TramoDeMedicion = TimeSpan.FromSeconds(1.5);

    public Task<ResumenSincronizacion> SincronizarAsync(
        IReadOnlyList<ElementoComparado> elementos, OpcionesCopia opciones, ProgresoOperacion progreso, CancellationToken cancelacion) =>
        Task.Run(() => Sincronizar(elementos, opciones, progreso, cancelacion), cancelacion);

    private static async Task<ResumenSincronizacion> Sincronizar(
        IReadOnlyList<ElementoComparado> elementos, OpcionesCopia opciones, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        var pendientes = elementos.Where(elemento => elemento.Seleccionado && elemento.SePuedeSincronizar).ToList();
        var archivos = pendientes.Where(elemento => !elemento.EsCarpeta).ToList();
        var ubicaciones = pendientes.Select(elemento => elemento.Par).Distinct()
            .ToDictionary(par => par, par => (Origen: CatalogoUbicaciones.Abrir(par.Origen), Destino: CatalogoUbicaciones.Abrir(par.Destino)));
        progreso.Hilos = Concurrencia.Decidir(opciones.HilosManuales, ubicaciones.Values.SelectMany(par => new[] { par.Origen, par.Destino }));
        progreso.IniciarFase("Copiando", pendientes.Count, archivos.Sum(archivo => archivo.TamanoACopiar));
        var resumen = new ResumenSincronizacion();
        CrearCarpetas(pendientes.Where(elemento => elemento.EsCarpeta), ubicaciones, progreso, resumen, cancelacion);
        var limite = new LimiteDinamico(progreso.Hilos.Hilos);
        using var finAjuste = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        var ajuste = progreso.Hilos.SeAjusta ? AjustarMientrasCopiaAsync(limite, progreso, finAjuste.Token) : Task.CompletedTask;
        var paralelo = new ParallelOptions { MaxDegreeOfParallelism = Concurrencia.Maximo, CancellationToken = cancelacion };
        try
        {
            await Parallel.ForEachAsync(archivos, paralelo, async (archivo, token) =>
            {
                await limite.EsperarAsync(token);
                try
                {
                    await CopiarAsync(archivo, ubicaciones[archivo.Par], opciones, progreso, resumen, token);
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
                if (progreso.Pausado)
                {
                    // En pausa no se avanza: medir ahora haría creer al ajustador que todo va lentísimo.
                    anterior = actual;
                    continue;
                }

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
                MarcarCopiado(carpeta, "Carpeta creada", resumen);
                progreso.Terminar(archivo, ResultadoArchivo.Copiado, carpeta.Motivo);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                MarcarFallido(carpeta, error, resumen);
                progreso.Terminar(archivo, ResultadoArchivo.Fallido, carpeta.Motivo);
            }
        }
    }

    private static async Task CopiarAsync(
        ElementoComparado elemento, (IUbicacion Origen, IUbicacion Destino) par, OpcionesCopia opciones,
        ProgresoOperacion progreso, ResumenSincronizacion resumen, CancellationToken cancelacion)
    {
        var archivo = progreso.Empezar(elemento.RutaRelativa, elemento.TamanoACopiar, "Copiando", elemento.RutaOrigen, elemento.RutaDestino, cancelacion);
        var resultado = ResultadoArchivo.Fallido;
        try
        {
            // En pausa el archivo ya figura en curso (y se puede saltar desde la lista) pero no empieza a copiarse.
            archivo.Etapa = "En pausa";
            await progreso.EsperarSiPausadoAsync(archivo.Cancelacion);
            archivo.Etapa = "Copiando";
            if (MotivoParaNoTocar(elemento, opciones.SiYaExiste) is { } motivo)
            {
                MarcarSaltado(elemento, motivo, resumen);
                resultado = ResultadoArchivo.Saltado;
                return;
            }

            var relativaDestino = opciones.SiYaExiste == ReglaConflicto.ConservarAmbos && elemento.Estado == EstadoElemento.Diferente
                ? NombreLibre(par.Destino, elemento.RutaRelativa)
                : elemento.RutaRelativa;
            // Se consulta en cada bloque: apagarla con la copia en marcha vale al instante, también para lo que ya verificaba.
            var verificada = await CopiaSegura.CopiarAsync(
                par.Origen, par.Destino, elemento.RutaRelativa, relativaDestino, () => opciones.Verificar, archivo, progreso, archivo.Cancelacion);
            var hecho = verificada ? "Copiado y verificado (SHA-256)" : "Copiado";
            MarcarCopiado(elemento, relativaDestino == elemento.RutaRelativa ? hecho : $"{hecho} como {Path.GetFileName(relativaDestino)}", resumen);
            resultado = ResultadoArchivo.Copiado;
        }
        catch (OperationCanceledException) when (archivo.Saltado && !cancelacion.IsCancellationRequested)
        {
            MarcarSaltado(elemento, "Saltado: lo pediste durante la copia (el destino quedó como estaba)", resumen);
            resultado = ResultadoArchivo.Saltado;
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
            progreso.AjustarBytes(archivo, elemento.TamanoACopiar);
            progreso.Terminar(archivo, resultado, cancelacion.IsCancellationRequested && resultado == ResultadoArchivo.Fallido ? "Cancelado" : elemento.Motivo);
        }
    }

    /// <summary>Por qué un archivo que ya existe no se toca según la regla elegida, o null si hay que copiarlo.</summary>
    private static string? MotivoParaNoTocar(ElementoComparado elemento, ReglaConflicto regla)
    {
        if (elemento.Estado != EstadoElemento.Diferente)
        {
            return null;
        }

        return regla switch
        {
            ReglaConflicto.Saltar => "Saltado: ya existía en el destino y se eligió no reemplazar",
            ReglaConflicto.SoloSiEsMasNuevo when elemento.FechaOrigen <= elemento.FechaDestino =>
                "Saltado: el del destino es igual o más nuevo",
            _ => null,
        };
    }

    /// <summary>"foto.jpg" → "foto (2).jpg", "foto (3).jpg"... el primero que no exista en el destino.</summary>
    public static string NombreLibre(IUbicacion destino, string relativa)
    {
        var carpeta = Path.GetDirectoryName(relativa) ?? string.Empty;
        var nombre = Path.GetFileNameWithoutExtension(relativa);
        var extension = Path.GetExtension(relativa);
        for (var numero = 2; ; numero++)
        {
            var candidato = Path.Combine(carpeta, $"{nombre} ({numero}){extension}");
            if (!destino.ExisteArchivo(candidato))
            {
                return candidato;
            }
        }
    }

    private static void MarcarCopiado(ElementoComparado elemento, string motivo, ResumenSincronizacion resumen)
    {
        elemento.Estado = EstadoElemento.Coincide;
        elemento.Seleccionado = false;
        elemento.Motivo = motivo;
        elemento.CopiaFallida = false;
        resumen.ContarCopiado();
    }

    /// <summary>No se copió a propósito: deja de estar marcado (no se reintenta) pero conserva su estado real.</summary>
    private static void MarcarSaltado(ElementoComparado elemento, string motivo, ResumenSincronizacion resumen)
    {
        elemento.Seleccionado = false;
        elemento.Motivo = motivo;
        resumen.ContarSaltado();
    }

    private static void MarcarFallido(ElementoComparado elemento, Exception error, ResumenSincronizacion resumen)
    {
        elemento.Motivo = "No se pudo copiar: " + DescribirError(error);
        elemento.CopiaFallida = true;
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
