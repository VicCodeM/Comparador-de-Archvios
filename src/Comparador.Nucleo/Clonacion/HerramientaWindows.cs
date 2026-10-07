using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Comparador.Nucleo.Clonacion;

/// <summary>
/// Ejecuta una herramienta de Windows (diskpart, dism, bcdboot) y devuelve lo que escribió. Si falla, la excepción
/// lleva su salida: es lo único que explica qué pasó. Hablan en la página de códigos OEM de la consola (850 en
/// español), no en UTF-8: sin eso los acentos de sus mensajes salen rotos.
/// </summary>
internal static class HerramientaWindows
{
    static HerramientaWindows() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string Ejecutar(string programa, string argumentos, Action<string>? alLeerLinea = null, CancellationToken cancelacion = default)
    {
        var consola = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        using var proceso = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, programa), argumentos)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = consola,
            StandardErrorEncoding = consola,
        })!;
        using var alCancelar = cancelacion.Register(() => proceso.Kill(entireProcessTree: true));
        var salida = new StringBuilder();
        var errores = proceso.StandardError.ReadToEndAsync(cancelacion);
        // ReadLine también corta en '\r': DISM redibuja su barra de avance en la misma línea con él.
        while (proceso.StandardOutput.ReadLine() is { } linea)
        {
            salida.AppendLine(linea);
            alLeerLinea?.Invoke(linea);
        }

        proceso.WaitForExit();
        cancelacion.ThrowIfCancellationRequested();
        var texto = salida.ToString() + errores.Result;
        if (proceso.ExitCode != 0)
        {
            throw new InvalidOperationException($"{programa} falló (código {proceso.ExitCode}):{Environment.NewLine}{Resumir(texto)}");
        }

        return texto;
    }

    /// <summary>Las últimas líneas con contenido: ahí está el error, no en la cabecera de la herramienta.</summary>
    private static string Resumir(string texto) => string.Join(Environment.NewLine,
        texto.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).TakeLast(8));
}
