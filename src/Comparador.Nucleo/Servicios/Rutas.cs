namespace Comparador.Nucleo.Servicios;

/// <summary>Rutas que funcionan aunque pasen de 260 caracteres (prefijo \\?\, también en carpetas de red).</summary>
public static class Rutas
{
    private const string PrefijoLargo = @"\\?\";
    private const string PrefijoRedLargo = @"\\?\UNC\";

    public static string ParaIO(string ruta)
    {
        if (ruta.StartsWith(PrefijoLargo, StringComparison.Ordinal))
        {
            return ruta;
        }

        var completa = Path.GetFullPath(ruta);

        return completa.StartsWith(@"\\", StringComparison.Ordinal)
            ? PrefijoRedLargo + completa[2..]
            : PrefijoLargo + completa;
    }
}
