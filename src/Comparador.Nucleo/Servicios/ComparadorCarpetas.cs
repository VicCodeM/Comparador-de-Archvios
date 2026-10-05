using System.Diagnostics;
using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Ubicaciones;

namespace Comparador.Nucleo.Servicios;

/// <summary>
/// Compara cada par origen/destino. Todo corre fuera de la ventana (incluso comprobar que las carpetas existen, que
/// en una red caída tarda decenas de segundos); el progreso solo suma contadores. Busca en el destino con un
/// diccionario (no recorriendo listas), así 100.000 archivos se comparan en segundos.
/// </summary>
public sealed class ComparadorCarpetas
{
    private const string MotivoPendienteContenido = "Pendiente de comparar el contenido";

    public Task<ResultadoComparacion> CompararAsync(
        IReadOnlyList<ParRutas> pares, OpcionesComparacion opciones, ProgresoOperacion progreso, CancellationToken cancelacion) =>
        Task.Run(() => Comparar(pares, opciones, progreso, cancelacion), cancelacion);

    private static async Task<ResultadoComparacion> Comparar(
        IReadOnlyList<ParRutas> pares, OpcionesComparacion opciones, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        var reloj = Stopwatch.StartNew();
        var elementos = new List<ElementoComparado>();
        var porContenido = new List<ElementoComparado>();
        var filtro = new FiltroExclusiones(opciones.Exclusiones);
        var ubicaciones = new List<IUbicacion>();
        for (var i = 0; i < pares.Count; i++)
        {
            var etiqueta = pares.Count > 1 ? $" (par {i + 1} de {pares.Count})" : string.Empty;
            var (origen, destino) = Preparar(pares[i], progreso, etiqueta);
            ubicaciones.AddRange([origen, destino]);
            CompararPar(pares[i], origen, destino, etiqueta, opciones, filtro, progreso, elementos, porContenido, cancelacion);
        }

        if (porContenido.Count > 0)
        {
            progreso.Hilos = Concurrencia.Decidir(opciones.HilosManuales, ubicaciones);
            await CompararContenidoAsync(porContenido, progreso, cancelacion);
        }

        return new ResultadoComparacion { Elementos = elementos, Duracion = reloj.Elapsed };
    }

    private static (IUbicacion Origen, IUbicacion Destino) Preparar(ParRutas par, ProgresoOperacion progreso, string etiqueta)
    {
        if (par.EsLaMismaCarpeta)
        {
            throw new InvalidOperationException($"El origen y el destino son la misma carpeta: {par.Origen}");
        }

        if (par.EntreTelefonos)
        {
            throw new InvalidOperationException("No se puede copiar de un teléfono a otro directamente: usa una carpeta del equipo como paso intermedio.");
        }

        progreso.IniciarFase($"Conectando{etiqueta}");
        var origen = CatalogoUbicaciones.Abrir(par.Origen);
        if (!origen.Existe())
        {
            throw new DirectoryNotFoundException($"No se encuentra el origen: {par.Origen}");
        }

        var destino = CatalogoUbicaciones.Abrir(par.Destino);
        if (!destino.Existe())
        {
            destino.CrearCarpeta(string.Empty);
        }

        return (origen, destino);
    }

    private static void CompararPar(
        ParRutas par, IUbicacion origen, IUbicacion destino, string etiqueta, OpcionesComparacion opciones, FiltroExclusiones filtro,
        ProgresoOperacion progreso, List<ElementoComparado> elementos, List<ElementoComparado> porContenido, CancellationToken cancelacion)
    {
        progreso.IniciarFase($"Leyendo el origen{etiqueta}");
        var entradasOrigen = EscanerCarpetas.Escanear(origen, filtro, progreso, cancelacion);
        progreso.IniciarFase($"Leyendo el destino{etiqueta}");
        var entradasDestino = Indexar(EscanerCarpetas.Escanear(destino, filtro, progreso, cancelacion));
        progreso.IniciarFase($"Comparando{etiqueta}", entradasOrigen.Count);
        var fechasFiables = origen.FechasFiables && destino.FechasFiables;
        foreach (var entrada in entradasOrigen)
        {
            cancelacion.ThrowIfCancellationRequested();
            entradasDestino.Remove(entrada.RutaRelativa, out var enDestino);
            var elemento = Clasificar(par, entrada, enDestino, opciones, fechasFiables);
            elementos.Add(elemento);
            if (elemento.Motivo == MotivoPendienteContenido)
            {
                porContenido.Add(elemento);
            }

            progreso.Avanzar();
        }

        if (opciones.DetectarSobrantes)
        {
            elementos.AddRange(entradasDestino.Values.Select(sobrante => Sobrante(par, sobrante)));
        }
    }

    private static Dictionary<string, EntradaEscaneada> Indexar(List<EntradaEscaneada> entradas)
    {
        var indice = new Dictionary<string, EntradaEscaneada>(entradas.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entrada in entradas)
        {
            indice.TryAdd(entrada.RutaRelativa, entrada);
        }

        return indice;
    }

    private static ElementoComparado Clasificar(
        ParRutas par, EntradaEscaneada origen, EntradaEscaneada? destino, OpcionesComparacion opciones, bool fechasFiables)
    {
        var elemento = new ElementoComparado
        {
            Par = par,
            RutaRelativa = origen.RutaRelativa,
            EsCarpeta = origen.EsCarpeta,
            TamanoOrigen = origen.EsCarpeta ? null : origen.Tamano,
            FechaOrigen = origen.SinAcceso ? null : origen.FechaModificacion,
            TamanoDestino = destino is null || destino.EsCarpeta ? null : destino.Tamano,
            FechaDestino = destino is null || destino.SinAcceso ? null : destino.FechaModificacion,
        };
        (elemento.Estado, elemento.Motivo) = Decidir(origen, destino, opciones, fechasFiables);
        elemento.Seleccionado = true;

        return elemento;
    }

    private static (EstadoElemento, string) Decidir(EntradaEscaneada origen, EntradaEscaneada? destino, OpcionesComparacion opciones, bool fechasFiables)
    {
        if (origen.SinAcceso)
        {
            return (EstadoElemento.SinAcceso, "Sin permiso para leer esta carpeta en el origen");
        }

        if (destino is null)
        {
            return (EstadoElemento.Falta, origen.EsCarpeta ? "La carpeta no existe en el destino" : "No existe en el destino");
        }

        if (destino.SinAcceso)
        {
            return (EstadoElemento.SinAcceso, "Sin permiso para leer esta carpeta en el destino");
        }

        if (origen.EsCarpeta != destino.EsCarpeta)
        {
            return (EstadoElemento.Diferente, "En un lado es carpeta y en el otro es archivo");
        }

        return origen.EsCarpeta ? (EstadoElemento.Coincide, string.Empty) : DecidirArchivo(origen, destino, opciones, fechasFiables);
    }

    private static (EstadoElemento, string) DecidirArchivo(EntradaEscaneada origen, EntradaEscaneada destino, OpcionesComparacion opciones, bool fechasFiables)
    {
        if (origen.Tamano != destino.Tamano)
        {
            return (EstadoElemento.Diferente, $"Tamaño distinto: {Formatos.Tamano(origen.Tamano)} en origen, {Formatos.Tamano(destino.Tamano)} en destino");
        }

        if (opciones.Modo == ModoComparacion.Exacto)
        {
            return (EstadoElemento.Coincide, MotivoPendienteContenido);
        }

        if (!fechasFiables)
        {
            return (EstadoElemento.Coincide, "Mismo tamaño (el teléfono no guarda la fecha; usa el modo exacto para asegurar)");
        }

        var diferencia = (origen.FechaModificacion - destino.FechaModificacion).Duration();

        return diferencia > opciones.ToleranciaFecha
            ? (EstadoElemento.Diferente, "Mismo tamaño pero distinta fecha de modificación")
            : (EstadoElemento.Coincide, string.Empty);
    }

    private static ElementoComparado Sobrante(ParRutas par, EntradaEscaneada destino) => new()
    {
        Par = par,
        RutaRelativa = destino.RutaRelativa,
        EsCarpeta = destino.EsCarpeta,
        TamanoDestino = destino.EsCarpeta ? null : destino.Tamano,
        FechaDestino = destino.SinAcceso ? null : destino.FechaModificacion,
        Estado = destino.SinAcceso ? EstadoElemento.SinAcceso : EstadoElemento.Sobra,
        Motivo = destino.SinAcceso ? "Sin permiso para leer esta carpeta en el destino" : "Solo existe en el destino",
    };

    private static async Task CompararContenidoAsync(List<ElementoComparado> elementos, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        progreso.IniciarFase("Comparando el contenido (SHA-256)", elementos.Count, elementos.Sum(e => 2 * e.TamanoACopiar));
        var opciones = new ParallelOptions { MaxDegreeOfParallelism = progreso.Hilos.Hilos, CancellationToken = cancelacion };
        await Parallel.ForEachAsync(elementos, opciones, async (elemento, token) =>
        {
            var archivo = progreso.Empezar(elemento.RutaRelativa, elemento.TamanoACopiar, "Leyendo el origen");
            (elemento.Estado, elemento.Motivo) = await CompararHuellasAsync(elemento, archivo, progreso, token);
            elemento.Seleccionado = true;
            progreso.AjustarBytes(archivo, 2 * elemento.TamanoACopiar);
            progreso.Terminar(archivo, elemento.Estado != EstadoElemento.Error, elemento.Motivo);
        });
    }

    private static async Task<(EstadoElemento, string)> CompararHuellasAsync(
        ElementoComparado elemento, ArchivoEnCurso archivo, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        try
        {
            var origen = CatalogoUbicaciones.Abrir(elemento.Par.Origen);
            var destino = CatalogoUbicaciones.Abrir(elemento.Par.Destino);
            var huellaOrigen = await CalculadoraHash.CalcularAsync(origen, elemento.RutaRelativa, leidos => progreso.SumarBytes(archivo, leidos), cancelacion);
            archivo.Reiniciar("Leyendo el destino");
            var huellaDestino = await CalculadoraHash.CalcularAsync(destino, elemento.RutaRelativa, leidos => progreso.SumarBytes(archivo, leidos), cancelacion);

            return huellaOrigen == huellaDestino
                ? (EstadoElemento.Coincide, "Contenido idéntico (SHA-256)")
                : (EstadoElemento.Diferente, "Mismo tamaño pero distinto contenido (SHA-256)");
        }
        catch (IOException) when (!cancelacion.IsCancellationRequested)
        {
            return (EstadoElemento.Error, $"No se pudo leer: lo está usando {DetectorBloqueos.Describir(elemento.RutaOrigen, elemento.RutaDestino)}");
        }
        catch (UnauthorizedAccessException)
        {
            return (EstadoElemento.Error, "Sin permiso para leer el archivo");
        }
    }
}
