using System.IO.Enumeration;

namespace Comparador.Nucleo.Servicios;

/// <summary>Decide si un archivo o carpeta se ignora por su nombre ("node_modules", ".git", "*.tmp"...).</summary>
public sealed class FiltroExclusiones(IEnumerable<string> patrones)
{
    private readonly string[] patrones = patrones
        .Select(patron => patron.Trim())
        .Where(patron => patron.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public bool Excluye(string nombre) =>
        patrones.Any(patron => FileSystemName.MatchesSimpleExpression(patron, nombre, ignoreCase: true));

    /// <summary>Convierte "node_modules; .git, *.tmp" en una lista de patrones.</summary>
    public static IReadOnlyList<string> DesdeTexto(string? texto) =>
        (texto ?? string.Empty).Split([';', ',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
