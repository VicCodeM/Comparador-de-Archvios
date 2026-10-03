namespace Comparador.Nucleo.Modelos;

/// <summary>Formas de juntar varios orígenes con varios destinos.</summary>
public enum ModoEmparejado
{
    /// <summary>El primer origen con el primer destino, el segundo con el segundo...</summary>
    UnoAUno,

    /// <summary>Cada origen con cada destino.</summary>
    TodosConTodos,
}

public static class GeneradorPares
{
    public static IReadOnlyList<ParRutas> Generar(IReadOnlyList<string> origenes, IReadOnlyList<string> destinos, ModoEmparejado modo)
    {
        var pares = modo == ModoEmparejado.UnoAUno ? UnoAUno(origenes, destinos) : TodosConTodos(origenes, destinos);

        return pares.Where(par => !par.EsLaMismaCarpeta).Distinct().ToList();
    }

    private static IEnumerable<ParRutas> UnoAUno(IReadOnlyList<string> origenes, IReadOnlyList<string> destinos) =>
        origenes.Zip(destinos, (origen, destino) => new ParRutas(origen, destino));

    private static IEnumerable<ParRutas> TodosConTodos(IReadOnlyList<string> origenes, IReadOnlyList<string> destinos) =>
        from origen in origenes from destino in destinos select new ParRutas(origen, destino);
}
