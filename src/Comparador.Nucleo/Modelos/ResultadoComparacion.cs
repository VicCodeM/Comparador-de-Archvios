namespace Comparador.Nucleo.Modelos;

public sealed class ResultadoComparacion
{
    public required IReadOnlyList<ElementoComparado> Elementos { get; init; }

    public required TimeSpan Duracion { get; init; }
}
