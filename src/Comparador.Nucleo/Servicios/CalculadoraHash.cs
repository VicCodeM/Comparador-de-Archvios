using Comparador.Nucleo.Ubicaciones;

namespace Comparador.Nucleo.Servicios;

/// <summary>Huella SHA-256 de un archivo de cualquier ubicación, leída por bloques y avisando de cada bloque leído.</summary>
public static class CalculadoraHash
{
    private const int TamanoBloque = 1024 * 1024;

    public static async Task<string> CalcularAsync(IUbicacion ubicacion, string relativa, Action<int> alLeer, CancellationToken cancelacion)
    {
        await using var flujo = new FlujoMedido(ubicacion.AbrirLectura(relativa), alLeer);
        await flujo.CopyToAsync(Stream.Null, TamanoBloque, cancelacion);

        return flujo.Huella();
    }
}
