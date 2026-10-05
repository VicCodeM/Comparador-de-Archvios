using Comparador.Nucleo.Servicios;

namespace Comparador.Nucleo.Ubicaciones;

/// <summary>
/// Cualquier carpeta con ruta de Windows: disco local, memoria USB, unidad de red mapeada (Z:\) o ruta de red (\\servidor\carpeta).
/// Todas pasan por el prefijo \\?\ para aguantar rutas de más de 260 caracteres.
/// </summary>
public sealed class UbicacionDisco(string raiz) : IUbicacion
{
    private const int TamanoBloque = 4 * 1024 * 1024;

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

    public bool Existe() => Directory.Exists(Rutas.ParaIO(Raiz));

    public void CrearCarpeta(string relativa) => Directory.CreateDirectory(Completa(relativa));

    public IReadOnlyList<EntradaEscaneada> ListarCarpeta(string relativa) =>
        new DirectoryInfo(Completa(relativa)).EnumerateFileSystemInfos("*", SoloEsteNivel).Select(info => Describir(relativa, info)).ToList();

    public Stream AbrirLectura(string relativa) => new FileStream(Completa(relativa), new FileStreamOptions
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.ReadWrite | FileShare.Delete,
        BufferSize = 0,
        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
    });

    public MetadatosArchivo LeerMetadatos(string relativa)
    {
        var info = new FileInfo(Completa(relativa));

        return new MetadatosArchivo(info.CreationTime, info.LastWriteTime, info.Attributes);
    }

    public async Task EscribirAsync(string relativa, Stream contenido, CancellationToken cancelacion)
    {
        var ruta = Completa(relativa);
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
        var rutaTemporal = Completa(temporal);
        var rutaDefinitiva = Completa(definitiva);
        if (metadatos.Creacion is { } creacion)
        {
            File.SetCreationTime(rutaTemporal, creacion);
        }

        if (metadatos.Modificacion is { } modificacion)
        {
            File.SetLastWriteTime(rutaTemporal, modificacion);
        }

        if (File.Exists(rutaDefinitiva))
        {
            File.SetAttributes(rutaDefinitiva, FileAttributes.Normal);
        }

        File.Move(rutaTemporal, rutaDefinitiva, overwrite: true);
        if (metadatos.Atributos is { } atributos)
        {
            File.SetAttributes(rutaDefinitiva, atributos);
        }
    }

    public void BorrarArchivo(string relativa) => File.Delete(Completa(relativa));

    private string Completa(string relativa) => Rutas.ParaIO(Path.Combine(Raiz, relativa));

    private static EntradaEscaneada Describir(string carpeta, FileSystemInfo info)
    {
        var ruta = Path.Combine(carpeta, info.Name);

        return info is FileInfo archivo
            ? new EntradaEscaneada(ruta, EsCarpeta: false, archivo.Length, archivo.LastWriteTime)
            : new EntradaEscaneada(ruta, EsCarpeta: true, 0, info.LastWriteTime, EsEnlace: info.Attributes.HasFlag(FileAttributes.ReparsePoint));
    }
}
