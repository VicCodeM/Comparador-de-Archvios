using System.Security.Cryptography;

namespace Comparador.Nucleo.Servicios;

/// <summary>
/// Envuelve un flujo de lectura: cuenta los bytes que pasan y calcula su SHA-256 al vuelo. Así la huella del origen
/// sale de la misma lectura que hace la copia, sin leer el archivo dos veces (importa mucho en red y en teléfonos).
/// </summary>
public sealed class FlujoMedido(Stream interno, Action<int> alLeer) : Stream
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public string Huella() => Convert.ToHexString(hash.GetHashAndReset());

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    /// <summary>Se deja ver el largo para que el destino reserve el espacio de una vez (en teléfonos puede no saberse).</summary>
    public override long Length => interno.CanSeek ? interno.Length : throw new NotSupportedException();

    public override long Position
    {
        get => interno.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Contar(buffer.AsSpan(offset, interno.Read(buffer, offset, count)));

    public override int Read(Span<byte> buffer) => Contar(buffer[..interno.Read(buffer)]);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancelacion = default)
    {
        var leidos = await interno.ReadAsync(buffer, cancelacion);

        return Contar(buffer.Span[..leidos]);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancelacion) =>
        ReadAsync(buffer.AsMemory(offset, count), cancelacion).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            interno.Dispose();
            hash.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await interno.DisposeAsync();
        hash.Dispose();
        await base.DisposeAsync();
    }

    private int Contar(ReadOnlySpan<byte> leidos)
    {
        if (leidos.Length > 0)
        {
            hash.AppendData(leidos);
            alLeer(leidos.Length);
        }

        return leidos.Length;
    }
}
