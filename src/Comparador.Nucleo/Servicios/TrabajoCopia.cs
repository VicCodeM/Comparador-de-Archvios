using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Ubicaciones;

namespace Comparador.Nucleo.Servicios;

/// <summary>Lo que se pidió copiar (o mover): archivos y carpetas sueltos hacia una carpeta destino.</summary>
public sealed record PedidoCopia(IReadOnlyList<string> Rutas, string Destino, bool Mover = false)
{
    /// <summary>Qué se copia, en corto: el nombre si es uno solo, o cuántos.</summary>
    public string Que => Rutas.Count == 1 ? Path.GetFileName(Rutas[0].TrimEnd('\\')) : $"{Rutas.Count:N0} elementos";
}

/// <summary>
/// Copia lo que se eligió en el Explorador hacia una carpeta, como TeraCopy: sin pasar por la pantalla de
/// comparación. Las carpetas se comparan rápido contra el destino y solo se copia lo que falta o cambió; los
/// archivos sueltos se copian siguiendo la regla de "si ya existe". Al mover, el original se borra solo después de
/// que su copia quedó bien.
/// </summary>
public static class TrabajoCopia
{
    public static async Task<ResumenSincronizacion> EjecutarAsync(
        PedidoCopia pedido, OpcionesCopia opciones, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        progreso.IniciarFase("Preparando");
        var elementos = await Task.Run(() => Preparar(pedido, progreso, cancelacion), cancelacion);
        var yaIguales = elementos.Where(elemento => !elemento.EsCarpeta && elemento.Estado == EstadoElemento.Coincide).ToHashSet();
        var resumen = await new SincronizadorArchivos().SincronizarAsync(elementos, opciones, progreso, cancelacion);
        if (pedido.Mover)
        {
            await QuitarOriginalesAsync(pedido, elementos, yaIguales, cancelacion);
        }

        return resumen;
    }

    private static async Task<List<ElementoComparado>> Preparar(PedidoCopia pedido, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        var elementos = new List<ElementoComparado>();
        var rapido = new OpcionesComparacion { DetectarSobrantes = false };
        foreach (var ruta in pedido.Rutas.Select(ruta => ruta.TrimEnd('\\')))
        {
            cancelacion.ThrowIfCancellationRequested();
            if (Directory.Exists(ruta))
            {
                var par = new ParRutas(ruta, Path.Combine(pedido.Destino, Path.GetFileName(ruta)));
                var comparacion = await new ComparadorCarpetas().CompararAsync([par], rapido, progreso, cancelacion);
                elementos.AddRange(comparacion.Elementos);
            }
            else if (File.Exists(ruta))
            {
                elementos.Add(ArchivoSuelto(ruta, pedido.Destino));
            }
        }

        return elementos;
    }

    /// <summary>Un archivo elegido suelto: se compara solo con el del mismo nombre en el destino (tamaño y fecha).</summary>
    private static ElementoComparado ArchivoSuelto(string ruta, string destino)
    {
        var origen = new FileInfo(ruta);
        var enDestino = new FileInfo(Path.Combine(destino, origen.Name));
        var existe = enDestino.Exists;
        var igual = existe && enDestino.Length == origen.Length && (enDestino.LastWriteTime - origen.LastWriteTime).Duration() <= TimeSpan.FromSeconds(2);
        var elemento = new ElementoComparado
        {
            Par = new ParRutas(origen.DirectoryName!, destino),
            RutaRelativa = origen.Name,
            EsCarpeta = false,
            TamanoOrigen = origen.Length,
            FechaOrigen = origen.LastWriteTime,
            TamanoDestino = existe ? enDestino.Length : null,
            FechaDestino = existe ? enDestino.LastWriteTime : null,
            Estado = !existe ? EstadoElemento.Falta : igual ? EstadoElemento.Coincide : EstadoElemento.Diferente,
            Motivo = !existe ? "No existe en el destino" : igual ? "Ya está igual en el destino" : "Ya existe en el destino y es distinto",
        };
        elemento.Seleccionado = true;

        return elemento;
    }

    /// <summary>
    /// Al mover se borra el original solo si es seguro: si se copió bien en esta pasada, o si ya estaba en el destino y
    /// su contenido es idéntico comprobado con SHA-256 (mismo tamaño y fecha NO basta: podría ser otro archivo y se
    /// perdería). Lo que falló, se saltó o no coincide se queda en el origen. Al final se quitan las carpetas vacías.
    /// </summary>
    private static async Task QuitarOriginalesAsync(
        PedidoCopia pedido, List<ElementoComparado> elementos, HashSet<ElementoComparado> yaIguales, CancellationToken cancelacion)
    {
        foreach (var archivo in elementos.Where(elemento => !elemento.EsCarpeta && elemento.Estado == EstadoElemento.Coincide))
        {
            if (yaIguales.Contains(archivo) && !await MismoContenidoAsync(archivo, cancelacion))
            {
                archivo.Motivo = "No se movió: en el destino hay un archivo con el mismo tamaño y fecha pero distinto contenido";
                continue;
            }

            try
            {
                File.SetAttributes(archivo.RutaOrigen, FileAttributes.Normal);
                File.Delete(archivo.RutaOrigen);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                archivo.Motivo = "Copiado, pero no se pudo borrar el original: " + SincronizadorArchivos.DescribirError(error);
            }
        }

        foreach (var carpeta in pedido.Rutas.Where(Directory.Exists))
        {
            BorrarCarpetasVacias(carpeta);
        }
    }

    private static async Task<bool> MismoContenidoAsync(ElementoComparado archivo, CancellationToken cancelacion)
    {
        static void SinAvisar(int _)
        {
        }

        var huellas = await Task.WhenAll(
            CalculadoraHash.CalcularDelDiscoAsync(Rutas.ParaIO(archivo.RutaOrigen), SinAvisar, cancelacion),
            CalculadoraHash.CalcularDelDiscoAsync(Rutas.ParaIO(archivo.RutaDestino), SinAvisar, cancelacion));

        return huellas[0] == huellas[1];
    }

    private static void BorrarCarpetasVacias(string carpeta)
    {
        foreach (var subcarpeta in Directory.EnumerateDirectories(carpeta))
        {
            BorrarCarpetasVacias(subcarpeta);
        }

        if (!Directory.EnumerateFileSystemEntries(carpeta).Any())
        {
            Directory.Delete(carpeta);
        }
    }
}
