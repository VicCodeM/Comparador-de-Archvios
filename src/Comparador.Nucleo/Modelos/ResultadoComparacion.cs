namespace Comparador.Nucleo.Modelos;

public sealed class ResultadoComparacion
{
    public required IReadOnlyList<ElementoComparado> Elementos { get; init; }

    public required TimeSpan Duracion { get; init; }

    public bool Cancelado { get; init; }

    public int Contar(EstadoElemento estado) => Elementos.Count(elemento => elemento.Estado == estado);

    public long BytesPendientes => Elementos.Where(elemento => elemento.SePuedeSincronizar).Sum(elemento => elemento.TamanoACopiar);

    public bool TodoSincronizado => !Elementos.Any(elemento => elemento.SePuedeSincronizar);
}
