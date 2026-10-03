using System.Globalization;
using System.Text;
using Comparador.Nucleo.Modelos;

namespace Comparador.Nucleo.Servicios;

/// <summary>Guarda la comparación en CSV (se abre en Excel con acentos bien, por eso UTF-8 con BOM).</summary>
public static class ExportadorReporte
{
    private static readonly string[] Columnas =
        ["Origen", "Destino", "Ruta", "Tipo", "Estado", "Motivo", "Tamaño origen", "Tamaño destino", "Fecha origen", "Fecha destino"];

    public static async Task GuardarCsvAsync(string archivo, IEnumerable<ElementoComparado> elementos, CancellationToken cancelacion)
    {
        var separador = CultureInfo.CurrentCulture.TextInfo.ListSeparator;
        await using var escritor = new StreamWriter(archivo, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await escritor.WriteLineAsync(string.Join(separador, Columnas));
        foreach (var elemento in elementos)
        {
            cancelacion.ThrowIfCancellationRequested();
            await escritor.WriteLineAsync(string.Join(separador, Fila(elemento).Select(Escapar)));
        }
    }

    private static IEnumerable<string> Fila(ElementoComparado elemento) =>
    [
        elemento.Par.Origen,
        elemento.Par.Destino,
        elemento.RutaRelativa,
        elemento.EsCarpeta ? "Carpeta" : "Archivo",
        NombreEstado(elemento.Estado),
        elemento.Motivo,
        elemento.TamanoOrigen?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        elemento.TamanoDestino?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        elemento.FechaOrigen?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
        elemento.FechaDestino?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
    ];

    public static string NombreEstado(EstadoElemento estado) => estado switch
    {
        EstadoElemento.Coincide => "Igual",
        EstadoElemento.Falta => "Falta en destino",
        EstadoElemento.Diferente => "Diferente",
        EstadoElemento.Sobra => "Solo en destino",
        EstadoElemento.SinAcceso => "Sin acceso",
        _ => "Error",
    };

    private static string Escapar(string valor) =>
        valor.IndexOfAny(['"', ';', ',', '\n', '\r']) >= 0 ? $"\"{valor.Replace("\"", "\"\"")}\"" : valor;
}
