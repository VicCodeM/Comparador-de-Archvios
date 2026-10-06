using Microsoft.Win32.SafeHandles;

namespace Comparador.Nucleo.Clonacion;

/// <summary>Donde se escribe la clonación. Se lee de vuelta para verificar antes de darla por terminada.</summary>
internal interface IDestinoClon : IDisposable
{
    /// <summary>Cuánto cabe; null si no hay límite (un archivo de imagen).</summary>
    long? Capacidad { get; }

    void Escribir(ReadOnlySpan<byte> datos, long posicion);

    int Leer(Span<byte> destino, long posicion);

    /// <summary>Solo tras escribir y verificar todo: deja el resultado a la vista (renombra la imagen, avisa a Windows).</summary>
    void Terminar();
}

/// <summary>
/// Un disco entero o una partición. Un disco solo acepta trozos de sectores completos: el final de una imagen que no
/// llena el último sector se completa con ceros.
/// </summary>
internal sealed class DestinoDisco(DiscoCrudo disco, long inicio, long capacidad, int sector) : IDestinoClon
{
    public long? Capacidad => capacidad;

    public void Escribir(ReadOnlySpan<byte> datos, long posicion)
    {
        if (datos.Length % sector == 0)
        {
            disco.EscribirEn(datos, inicio + posicion);
            return;
        }

        var completo = new byte[RedondearASector(datos.Length)];
        datos.CopyTo(completo);
        disco.EscribirEn(completo, inicio + posicion);
    }

    public int Leer(Span<byte> destino, long posicion)
    {
        if (destino.Length % sector == 0)
        {
            return disco.LeerEn(destino, inicio + posicion);
        }

        var completo = new byte[RedondearASector(destino.Length)];
        var leidos = disco.LeerEn(completo, inicio + posicion);
        completo.AsSpan(0, Math.Min(leidos, destino.Length)).CopyTo(destino);

        return Math.Min(leidos, destino.Length);
    }

    public void Terminar() => disco.AvisarCambios();

    public void Dispose() => disco.Dispose();

    private int RedondearASector(int largo) => (largo + sector - 1) / sector * sector;
}

/// <summary>
/// Un archivo de imagen. Se escribe como ".parcial" y solo se renombra si la clonación terminó y se verificó: nunca
/// queda una imagen a medias con aspecto de buena.
/// </summary>
internal sealed class DestinoArchivo : IDestinoClon
{
    private const string Extension = ".parcial";

    private readonly string ruta;
    private readonly SafeFileHandle archivo;
    private bool terminado;

    public DestinoArchivo(string ruta)
    {
        this.ruta = ruta;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ruta))!);
        archivo = File.OpenHandle(ruta + Extension, FileMode.Create, FileAccess.ReadWrite);
    }

    public long? Capacidad => null;

    public void Escribir(ReadOnlySpan<byte> datos, long posicion) => RandomAccess.Write(archivo, datos, posicion);

    public int Leer(Span<byte> destino, long posicion) => RandomAccess.Read(archivo, destino, posicion);

    public void Terminar()
    {
        RandomAccess.FlushToDisk(archivo);
        archivo.Dispose();
        File.Move(ruta + Extension, ruta, overwrite: true);
        terminado = true;
    }

    public void Dispose()
    {
        archivo.Dispose();
        if (!terminado)
        {
            File.Delete(ruta + Extension);
        }
    }
}
