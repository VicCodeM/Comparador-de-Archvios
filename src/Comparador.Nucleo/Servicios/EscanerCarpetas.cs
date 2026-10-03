using Comparador.Nucleo.Modelos;

namespace Comparador.Nucleo.Servicios;

/// <summary>Un archivo o carpeta encontrado al recorrer una carpeta.</summary>
public sealed record EntradaEscaneada(
    string RutaRelativa, bool EsCarpeta, long Tamano, DateTime FechaModificacion, bool SinAcceso = false, bool EsEnlace = false);

/// <summary>
/// Recorre una carpeta entera contando en vivo lo que encuentra. Las carpetas sin permiso se anotan como
/// "sin acceso" y se sigue: NO se les cambia el dueño ni los permisos (la versión anterior lo hacía sola).
/// Los enlaces (junctions) no se recorren por dentro, para no entrar en bucles.
/// </summary>
public static class EscanerCarpetas
{
    private const int CadaCuantosMarcar = 250;

    private static readonly EnumerationOptions SoloEsteNivel = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    public static List<EntradaEscaneada> Escanear(string raiz, FiltroExclusiones filtro, ProgresoOperacion progreso, CancellationToken cancelacion)
    {
        var encontradas = new List<EntradaEscaneada>();
        var pendientes = new Stack<string>();
        pendientes.Push(string.Empty);
        while (pendientes.Count > 0)
        {
            cancelacion.ThrowIfCancellationRequested();
            var carpeta = pendientes.Pop();
            foreach (var entrada in LeerCarpeta(raiz, carpeta, filtro))
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

    private static IEnumerable<EntradaEscaneada> LeerCarpeta(string raiz, string carpeta, FiltroExclusiones filtro)
    {
        try
        {
            return new DirectoryInfo(Rutas.ParaIO(Path.Combine(raiz, carpeta)))
                .EnumerateFileSystemInfos("*", SoloEsteNivel)
                .Where(info => !filtro.Excluye(info.Name))
                .Select(info => Describir(carpeta, info))
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

    private static EntradaEscaneada Describir(string carpeta, FileSystemInfo info)
    {
        var ruta = Path.Combine(carpeta, info.Name);

        return info is FileInfo archivo
            ? new EntradaEscaneada(ruta, EsCarpeta: false, archivo.Length, archivo.LastWriteTime)
            : new EntradaEscaneada(ruta, EsCarpeta: true, 0, info.LastWriteTime, EsEnlace: info.Attributes.HasFlag(FileAttributes.ReparsePoint));
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
