using Comparador.Nucleo.Servicios;

namespace Comparador.Nucleo.Clonacion;

public enum EstiloParticiones
{
    Mbr,
    Gpt,
    SinParticiones,
}

/// <summary>Un trozo del disco: dónde empieza, cuánto mide, qué es y, si Windows lo monta, con qué letra.</summary>
public sealed record Particion(int Numero, long Inicio, long Tamano, string Tipo, string? Letra)
{
    public string Titulo => $"Partición {Numero}";

    public string Detalle => string.Join(" · ", new[] { Formatos.Tamano(Tamano), Tipo, Letra ?? "sin letra" });
}
