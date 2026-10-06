using System.IO.Compression;
using SharpCompress.Compressors.Xz;

namespace Comparador.Nucleo.Clonacion;

/// <summary>
/// Lo que se lee, como un flujo de bytes de principio a fin. Largo null: no se sabe antes de terminar (imagen .xz o
/// .gz); entonces el avance se calcula con lo leído del archivo comprimido.
/// </summary>
internal sealed class OrigenClon(Stream datos, long? largo, Stream? comprimido, IDisposable? extra = null) : IDisposable
{
    public Stream Datos => datos;

    public long? Largo => largo;

    /// <summary>Qué parte del archivo comprimido ya se leyó (0 a 1), o null si no es comprimido.</summary>
    public double? Fraccion => comprimido is { Length: > 0 } archivo ? (double)archivo.Position / archivo.Length : null;

    public static OrigenClon DeTramo(DiscoCrudo disco, long inicio, long largo) => new(new TramoDisco(disco, inicio, largo), largo, null, disco);

    /// <summary>.img sin comprimir, o comprimida como las de Raspberry Pi: .xz, .gz o .zip (se usa la .img de dentro).</summary>
    public static OrigenClon DeImagen(string ruta)
    {
        var archivo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        switch (Path.GetExtension(ruta).ToLowerInvariant())
        {
            case ".xz":
                return new OrigenClon(new XZStream(archivo), null, archivo);
            case ".gz":
                return new OrigenClon(new GZipStream(archivo, CompressionMode.Decompress), null, archivo);
            case ".zip":
                var zip = new ZipArchive(archivo, ZipArchiveMode.Read);
                var imagen = zip.Entries.Where(entrada => entrada.Length > 0).MaxBy(entrada => entrada.Length)
                    ?? throw new InvalidDataException("El .zip no tiene ninguna imagen dentro.");

                return new OrigenClon(imagen.Open(), imagen.Length, null, zip);
            default:
                return new OrigenClon(archivo, archivo.Length, null);
        }
    }

    public void Dispose()
    {
        datos.Dispose();
        comprimido?.Dispose();
        extra?.Dispose();
    }

    /// <summary>Un trozo de un disco (todo o una partición) leído de principio a fin.</summary>
    private sealed class TramoDisco(DiscoCrudo disco, long inicio, long largo) : Stream
    {
        private long posicion;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => largo;

        public override long Position
        {
            get => posicion;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var cuanto = (int)Math.Min(buffer.Length, largo - posicion);
            if (cuanto <= 0)
            {
                return 0;
            }

            var leidos = disco.LeerEn(buffer[..cuanto], inicio + posicion);
            posicion += leidos;

            return leidos;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
