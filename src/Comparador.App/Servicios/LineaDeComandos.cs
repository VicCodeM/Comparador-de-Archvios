using System.IO;
using System.Windows;
using Comparador.Nucleo.Servicios;

namespace Comparador.App.Servicios;

/// <summary>Un pedido que llega por la línea de comandos (o desde el clic derecho del Explorador).</summary>
public sealed record PedidoExterno(IReadOnlyList<string> Rutas, string? Destino, bool Mover, bool PreguntarDestino);

/// <summary>
/// Entiende lo que se le pide a la app al abrirla:
/// <code>
///   copiar  &lt;rutas...&gt; --destino &lt;carpeta&gt;   copia (si falta el destino, lo pregunta)
///   mover   &lt;rutas...&gt; --destino &lt;carpeta&gt;   mueve
///   pegar   &lt;carpeta&gt;                           pega lo copiado o cortado con Ctrl+C / Ctrl+X en el Explorador
/// </code>
/// </summary>
public static class LineaDeComandos
{
    private const string MarcaDestino = "--destino";

    public static PedidoExterno? Interpretar(IReadOnlyList<string> argumentos)
    {
        if (argumentos.Count == 0)
        {
            return null;
        }

        var verbo = argumentos[0].ToLowerInvariant();
        var resto = argumentos.Skip(1).ToList();

        return verbo switch
        {
            "copiar" or "mover" => DeRutas(resto, mover: verbo == "mover"),
            "pegar" when resto.Count > 0 => DelPortapapeles(resto[0]),
            _ => null,
        };
    }

    private static PedidoExterno? DeRutas(List<string> resto, bool mover)
    {
        var posicion = resto.FindIndex(argumento => argumento.Equals(MarcaDestino, StringComparison.OrdinalIgnoreCase));
        var destino = posicion >= 0 && posicion + 1 < resto.Count ? resto[posicion + 1] : null;
        var rutas = posicion >= 0 ? resto.Take(posicion).ToList() : resto;

        return rutas.Count == 0 ? null : new PedidoExterno(rutas, destino, mover, PreguntarDestino: destino is null);
    }

    /// <summary>
    /// Lo que se copió o cortó en el Explorador: si fue con Ctrl+X, Windows lo marca como "mover" y así se respeta.
    /// </summary>
    private static PedidoExterno? DelPortapapeles(string destino)
    {
        if (!Clipboard.ContainsFileDropList())
        {
            return null;
        }

        var rutas = Clipboard.GetFileDropList().Cast<string>().ToList();

        return new PedidoExterno(rutas, destino, EsCortar(), PreguntarDestino: false);
    }

    private static bool EsCortar()
    {
        const int Mover = 2;

        return Clipboard.GetData("Preferred DropEffect") is MemoryStream efecto && efecto.Length >= 4
            && (BitConverter.ToInt32(efecto.ToArray(), 0) & Mover) != 0;
    }

    /// <summary>El pedido listo para copiar, preguntando el destino si hace falta. Null si se canceló.</summary>
    public static PedidoCopia? Completar(PedidoExterno pedido)
    {
        var destino = pedido.Destino ?? Escritorio.ElegirCarpeta(pedido.Mover ? "Mover a..." : "Copiar a...");

        return destino is null ? null : new PedidoCopia(pedido.Rutas, destino, pedido.Mover);
    }
}
