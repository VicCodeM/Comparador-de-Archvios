using Comparador.Nucleo.Servicios;

namespace Comparador.Nucleo.Ubicaciones;

/// <summary>
/// Cualquier carpeta con ruta de Windows: disco local, memoria USB, unidad de red mapeada (Z:\) o ruta de red (\\servidor\carpeta).
/// Todas pasan por el prefijo \\?\ para aguantar rutas de más de 260 caracteres.
/// </summary>
public sealed class UbicacionDisco(string raiz) : IUbicacion
{
    private const int TamanoBloque = 4 * 1024 * 1024;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> carpetasCreadas = new(StringComparer.OrdinalIgnoreCase);
    private PerfilDisco? perfil;

    private static readonly EnumerationOptions SoloEsteNivel = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    public string Raiz { get; } = raiz;

    public TipoUbicacion Tipo { get; } = CatalogoUbicaciones.TipoDe(raiz);

    public bool FechasFiables => true;

    /// <summary>El disco físico de verdad (SSD, mecánico, USB...), preguntado una vez y recordado.</summary>
    public PerfilDisco Perfil => perfil ??= DetectorDiscos.Detectar(Raiz);

    public bool Existe() => Directory.Exists(Rutas.ParaIO(Raiz));

    /// <summary>
    /// Con miles de archivos en la misma carpeta, preguntarle a Windows por ella en cada uno es puro gasto: se recuerda
    /// cuáles ya se crearon en esta sesión.
    /// </summary>
    public void CrearCarpeta(string relativa)
    {
        if (carpetasCreadas.TryAdd(relativa, 0))
        {
            Directory.CreateDirectory(RutaIO(relativa));
        }
    }

    public IReadOnlyList<EntradaEscaneada> ListarCarpeta(string relativa) =>
        new DirectoryInfo(RutaIO(relativa)).EnumerateFileSystemInfos("*", SoloEsteNivel).Select(info => Describir(relativa, info)).ToList();

    public Stream AbrirLectura(string relativa) => new FileStream(RutaIO(relativa), new FileStreamOptions
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.ReadWrite | FileShare.Delete,
        BufferSize = 0,
        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
    });

    public MetadatosArchivo LeerMetadatos(string relativa)
    {
        var info = new FileInfo(RutaIO(relativa));

        return new MetadatosArchivo(info.CreationTime, info.LastWriteTime, info.Attributes);
    }

    public async Task EscribirAsync(string relativa, Stream contenido, CancellationToken cancelacion)
    {
        var ruta = RutaIO(relativa);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        await using var escritura = new FileStream(ruta, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 0,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            PreallocationSize = contenido.CanSeek ? contenido.Length : 0,
        });
        await contenido.CopyToAsync(escritura, TamanoBloque, cancelacion);
    }

    public void Reemplazar(string temporal, string definitiva, MetadatosArchivo metadatos)
    {
        var rutaTemporal = RutaIO(temporal);
        var rutaDefinitiva = RutaIO(definitiva);
        if (metadatos.Creacion is { } creacion)
        {
            File.SetCreationTime(rutaTemporal, creacion);
        }

        if (metadatos.Modificacion is { } modificacion)
        {
            File.SetLastWriteTime(rutaTemporal, modificacion);
        }

        MoverEncima(rutaTemporal, rutaDefinitiva);
        if (metadatos.Atributos is { } atributos)
        {
            File.SetAttributes(rutaDefinitiva, atributos);
        }
    }

    /// <summary>
    /// Reemplaza de una vez y solo si Windows se niega por ser de solo lectura se le quita ese atributo y se repite.
    /// Preguntar antes "¿existe?" era una ida y vuelta más por archivo y, en red, la respuesta podía venir de una
    /// caché vieja: decía que el archivo existía cuando ya no, y la copia fallaba.
    /// </summary>
    private static void MoverEncima(string rutaTemporal, string rutaDefinitiva)
    {
        try
        {
            File.Move(rutaTemporal, rutaDefinitiva, overwrite: true);
        }
        catch (UnauthorizedAccessException) when (EsSoloLectura(rutaDefinitiva))
        {
            File.SetAttributes(rutaDefinitiva, FileAttributes.Normal);
            File.Move(rutaTemporal, rutaDefinitiva, overwrite: true);
        }
    }

    private static bool EsSoloLectura(string ruta)
    {
        try
        {
            return File.GetAttributes(ruta).HasFlag(FileAttributes.ReadOnly);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    public void BorrarArchivo(string relativa) => File.Delete(RutaIO(relativa));

    /// <summary>La ruta completa lista para Windows (con \\?\ para rutas largas).</summary>
    public string RutaIO(string relativa) => Rutas.ParaIO(Path.Combine(Raiz, relativa));

    private static EntradaEscaneada Describir(string carpeta, FileSystemInfo info)
    {
        var ruta = Path.Combine(carpeta, info.Name);

        return info is FileInfo archivo
            ? new EntradaEscaneada(ruta, EsCarpeta: false, archivo.Length, archivo.LastWriteTime)
            : new EntradaEscaneada(ruta, EsCarpeta: true, 0, info.LastWriteTime, EsEnlace: info.Attributes.HasFlag(FileAttributes.ReparsePoint));
    }
}
