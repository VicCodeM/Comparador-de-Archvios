using System.Diagnostics;
using Comparador.Nucleo.Modelos;

namespace Comparador.Nucleo.Servicios;

/// <summary>
/// Compara cada par origen/destino. Todo corre fuera de la ventana; el progreso solo suma contadores. Busca en el
/// destino con un diccionario (no recorriendo listas), así 100.000 archivos se comparan en segundos.
/// </summary>
public sealed class ComparadorCarpetas
{
    private const int LecturasSimultaneas = 2;
    private const string MotivoPendienteContenido = "Pendiente de comparar el contenido";

    public async Task<ResultadoComparacion> CompararAsync(
        IReadOnlyList<ParRutas> pares, OpcionesComparacion opciones, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        var reloj = Stopwatch.StartNew();
        var elementos = new List<ElementoComparado>();
        var porContenido = new List<ElementoComparado>();
        var filtro = new FiltroExclusiones(opciones.Exclusiones);
        for (var i = 0; i < pares.Count; i++)
        {
            var etiqueta = pares.Count > 1 ? $" (par {i + 1} de {pares.Count})" : string.Empty;
            var par = pares[i];
            await Task.Run(() => CompararPar(par, etiqueta, opciones, filtro, progreso, elementos, porContenido, cancelacion), cancelacion);
        }

        if (porContenido.Count > 0)
        {
            await CompararContenidoAsync(porContenido, progreso, cancelacion);
        }

        return new ResultadoComparacion { Elementos = elementos, Duracion = reloj.Elapsed };
    }

    private static void CompararPar(
        ParRutas par, string etiqueta, OpcionesComparacion opciones, FiltroExclusiones filtro, ProgresoOperacion progreso,
        List<ElementoComparado> elementos, List<ElementoComparado> porContenido, CancellationToken cancelacion)
    {
        progreso.IniciarFase($"Leyendo el origen{etiqueta}");
        var origen = EscanerCarpetas.Escanear(par.Origen, filtro, progreso, cancelacion);
        progreso.IniciarFase($"Leyendo el destino{etiqueta}");
        var destino = Indexar(EscanerCarpetas.Escanear(par.Destino, filtro, progreso, cancelacion));
        progreso.IniciarFase($"Comparando{etiqueta}", origen.Count);
        foreach (var entrada in origen)
        {
            cancelacion.ThrowIfCancellationRequested();
            destino.Remove(entrada.RutaRelativa, out var enDestino);
            var elemento = Clasificar(par, entrada, enDestino, opciones);
            elementos.Add(elemento);
            if (elemento.Motivo == MotivoPendienteContenido)
            {
                porContenido.Add(elemento);
            }

            progreso.Avanzar();
        }

        if (opciones.DetectarSobrantes)
        {
            elementos.AddRange(destino.Values.Select(sobrante => Sobrante(par, sobrante)));
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

    private static ElementoComparado Clasificar(ParRutas par, EntradaEscaneada origen, EntradaEscaneada? destino, OpcionesComparacion opciones)
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
        (elemento.Estado, elemento.Motivo) = Decidir(origen, destino, opciones);
        elemento.Seleccionado = true;

        return elemento;
    }

    private static (EstadoElemento, string) Decidir(EntradaEscaneada origen, EntradaEscaneada? destino, OpcionesComparacion opciones)
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

        return origen.EsCarpeta ? (EstadoElemento.Coincide, string.Empty) : DecidirArchivo(origen, destino, opciones);
    }

    private static (EstadoElemento, string) DecidirArchivo(EntradaEscaneada origen, EntradaEscaneada destino, OpcionesComparacion opciones)
    {
        if (origen.Tamano != destino.Tamano)
        {
            return (EstadoElemento.Diferente, $"Tamaño distinto: {Formatos.Tamano(origen.Tamano)} en origen, {Formatos.Tamano(destino.Tamano)} en destino");
        }

        if (opciones.Modo == ModoComparacion.Exacto)
        {
            return (EstadoElemento.Coincide, MotivoPendienteContenido);
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
        var opciones = new ParallelOptions { MaxDegreeOfParallelism = LecturasSimultaneas, CancellationToken = cancelacion };
        await Parallel.ForEachAsync(elementos, opciones, async (elemento, token) =>
        {
            progreso.MarcarActual(elemento.RutaRelativa);
            (elemento.Estado, elemento.Motivo) = await CompararHuellasAsync(elemento, progreso, token);
            elemento.Seleccionado = true;
            progreso.Avanzar();
        });
    }

    private static async Task<(EstadoElemento, string)> CompararHuellasAsync(ElementoComparado elemento, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        try
        {
            var huellaOrigen = await CalculadoraHash.CalcularAsync(elemento.RutaOrigen, progreso, cancelacion);
            var huellaDestino = await CalculadoraHash.CalcularAsync(elemento.RutaDestino, progreso, cancelacion);

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
