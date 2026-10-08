using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Ubicaciones;

namespace Comparador.Nucleo.Servicios;

/// <summary>Un archivo o carpeta encontrado al recorrer una carpeta.</summary>
public sealed record EntradaEscaneada(
    string RutaRelativa, bool EsCarpeta, long Tamano, DateTime FechaModificacion, bool SinAcceso = false, bool EsEnlace = false);

/// <summary>
/// Recorre una ubicación entera contando en vivo lo que encuentra. Las carpetas sin permiso se anotan como
/// "sin acceso" y se sigue: NO se les cambia el dueño ni los permisos (la versión anterior lo hacía sola).
/// Los enlaces (junctions) no se recorren por dentro, para no entrar en bucles.
/// </summary>
public static class EscanerCarpetas
{
    private const int CadaCuantosMarcar = 250;

    public static List<EntradaEscaneada> Escanear(IUbicacion ubicacion, FiltroExclusiones filtro, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        var encontradas = new List<EntradaEscaneada>();
        var pendientes = new Stack<string>();
        pendientes.Push(string.Empty);
        while (pendientes.Count > 0)
        {
            cancelacion.ThrowIfCancellationRequested();
            var carpeta = pendientes.Pop();
            foreach (var entrada in LeerCarpeta(ubicacion, carpeta, filtro))
            {
                encontradas.Add(entrada);
                ContarEncontrada(entrada, encontradas.Count, progreso);
                if (entrada.EsCarpeta && !entrada.SinAcceso && !entrada.EsEnlace)
                {
                    pendientes.Push(entrada.RutaRelativa);
                }
            }
        }

        return encontradas;
    }

    private static IEnumerable<EntradaEscaneada> LeerCarpeta(IUbicacion ubicacion, string carpeta, FiltroExclusiones filtro)
    {
        try
        {
            return ubicacion.ListarCarpeta(carpeta).ToList()
                .Where(entrada => !(EsTemporalDeCopia(entrada) && Desechar(ubicacion, entrada)) && !filtro.Excluye(Path.GetFileName(entrada.RutaRelativa)))
                .ToList();
        }
        catch (UnauthorizedAccessException) when (carpeta.Length > 0)
        {
            return [new EntradaEscaneada(carpeta, EsCarpeta: true, 0, default, SinAcceso: true)];
        }
        catch (DirectoryNotFoundException) when (carpeta.Length > 0)
        {
            return [];
        }
    }

    /// <summary>Lo que dejó una copia interrumpida (un apagón, la app cerrada de golpe) no es un archivo del usuario.</summary>
    private static bool EsTemporalDeCopia(EntradaEscaneada entrada) =>
        !entrada.EsCarpeta && entrada.RutaRelativa.EndsWith(CopiaSegura.ExtensionTemporal, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Se borra al encontrarlo: antes solo se saltaba y quedaba para siempre en el destino, ocupando espacio sin que
    /// nadie lo viera (Victor, 2026-10-08). Si una copia en marcha lo tiene abierto, Windows no deja borrarlo y se
    /// salta igual. Siempre devuelve true: no se muestra en ningún caso.
    /// </summary>
    private static bool Desechar(IUbicacion ubicacion, EntradaEscaneada temporal)
    {
        try
        {
            ubicacion.BorrarArchivo(temporal.RutaRelativa);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // Abierto por una copia en marcha o sin permiso: se deja.
        }

        return true;
    }

    private static void ContarEncontrada(EntradaEscaneada entrada, int cuantas, ProgresoOperacion progreso)
    {
        progreso.Avanzar();
        progreso.SumarBytes(entrada.Tamano);
        if (cuantas % CadaCuantosMarcar == 1)
        {
            progreso.MarcarActual(entrada.RutaRelativa);
        }
    }
}
