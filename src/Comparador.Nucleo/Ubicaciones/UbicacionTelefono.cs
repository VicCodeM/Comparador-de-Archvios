using MediaDevices;
using Comparador.Nucleo.Servicios;

namespace Comparador.Nucleo.Ubicaciones;

/// <summary>
/// Una carpeta dentro de un teléfono o cámara conectado por USB (protocolo MTP, el que usa el Explorador).
/// Se escribe como <c>mtp:\\Nombre del teléfono\Almacenamiento interno\DCIM</c>. El teléfono atiende una cosa a la
/// vez, así que todas las operaciones van en fila; por eso con un teléfono siempre se copia de uno en uno.
/// </summary>
public sealed class UbicacionTelefono : IUbicacion
{
    public const string Prefijo = @"mtp:\\";

    private readonly MediaDevice dispositivo;
    private readonly string carpeta;
    private readonly SemaphoreSlim turno;

    internal UbicacionTelefono(string raiz, MediaDevice dispositivo, SemaphoreSlim turno)
    {
        Raiz = raiz;
        this.dispositivo = dispositivo;
        this.turno = turno;
        carpeta = RutaInterna(raiz);
    }

    public string Raiz { get; }

    public TipoUbicacion Tipo => TipoUbicacion.Telefono;

    public bool FechasFiables => false;

    public static bool EsRutaDeTelefono(string ruta) => ruta.StartsWith(Prefijo, StringComparison.OrdinalIgnoreCase);

    /// <summary>"mtp:\\Pixel 8\Interno\DCIM" se parte en "Pixel 8" y "\Interno\DCIM".</summary>
    public static (string Dispositivo, string Carpeta) Partir(string ruta)
    {
        var resto = ruta[Prefijo.Length..].TrimEnd('\\');
        var corte = resto.IndexOf('\\');

        return corte < 0 ? (resto, @"\") : (resto[..corte], resto[corte..]);
    }

    public static string Componer(string dispositivo, string carpeta) => Prefijo + dispositivo + (carpeta == @"\" ? string.Empty : carpeta);

    private static string RutaInterna(string raiz) => Partir(raiz).Carpeta;

    public bool Existe() => EnTurno(() => dispositivo.DirectoryExists(carpeta));

    public void CrearCarpeta(string relativa) => EnTurno(() =>
    {
        var ruta = Completa(relativa);
        if (!dispositivo.DirectoryExists(ruta))
        {
            dispositivo.CreateDirectory(ruta);
        }
    });

    public bool ExisteArchivo(string relativa) => EnTurno(() => dispositivo.FileExists(Completa(relativa)));

    public IReadOnlyList<EntradaEscaneada> ListarCarpeta(string relativa) => EnTurno(() =>
        dispositivo.GetDirectoryInfo(Completa(relativa)).EnumerateFileSystemInfos().Select(info => Describir(relativa, info)).ToList());

    public Stream AbrirLectura(string relativa) => EnTurno(() => dispositivo.GetFileInfo(Completa(relativa)).OpenRead());

    public MetadatosArchivo LeerMetadatos(string relativa) =>
        EnTurno(() => new MetadatosArchivo(null, dispositivo.GetFileInfo(Completa(relativa)).LastWriteTime, null));

    public async Task EscribirAsync(string relativa, Stream contenido, CancellationToken cancelacion)
    {
        await turno.WaitAsync(cancelacion);
        try
        {
            var ruta = Completa(relativa);
            await Task.Run(() =>
            {
                if (dispositivo.FileExists(ruta))
                {
                    dispositivo.DeleteFile(ruta);
                }

                dispositivo.UploadFile(contenido, ruta);
            }, cancelacion);
        }
        finally
        {
            turno.Release();
        }
    }

    /// <summary>El teléfono no deja poner fechas ni atributos: guardará la hora en que se copió.</summary>
    public void Reemplazar(string temporal, string definitiva, MetadatosArchivo metadatos) => EnTurno(() =>
    {
        var rutaDefinitiva = Completa(definitiva);
        if (dispositivo.FileExists(rutaDefinitiva))
        {
            dispositivo.DeleteFile(rutaDefinitiva);
        }

        dispositivo.Rename(Completa(temporal), Path.GetFileName(definitiva));
    });

    public void BorrarArchivo(string relativa) => EnTurno(() =>
    {
        var ruta = Completa(relativa);
        if (dispositivo.FileExists(ruta))
        {
            dispositivo.DeleteFile(ruta);
        }
    });

    private string Completa(string relativa) => relativa.Length == 0 ? carpeta : Path.Combine(carpeta, relativa);

    private static EntradaEscaneada Describir(string relativa, MediaFileSystemInfo info) => new(
        Path.Combine(relativa, info.Name),
        EsCarpeta: info is MediaDirectoryInfo,
        info is MediaDirectoryInfo ? 0 : (long)info.Length,
        info.LastWriteTime ?? default);

    private T EnTurno<T>(Func<T> accion)
    {
        turno.Wait();
        try
        {
            return accion();
        }
        finally
        {
            turno.Release();
        }
    }

    private void EnTurno(Action accion) => EnTurno(() =>
    {
        accion();
        return true;
    });
}
