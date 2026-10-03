namespace Comparador.Pruebas;

/// <summary>Un origen y un destino temporales que se borran al terminar la prueba.</summary>
public sealed class CarpetasDePrueba : IDisposable
{
    private readonly string raiz = Path.Combine(Path.GetTempPath(), "comparador-pruebas", Guid.NewGuid().ToString("N"));

    public CarpetasDePrueba()
    {
        Directory.CreateDirectory(Origen);
        Directory.CreateDirectory(Destino);
    }

    public string Origen => Path.Combine(raiz, "origen");

    public string Destino => Path.Combine(raiz, "destino");

    public static string Escribir(string carpeta, string rutaRelativa, string contenido, DateTime? fecha = null)
    {
        var ruta = Path.Combine(carpeta, rutaRelativa);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, contenido);
        File.SetLastWriteTime(ruta, fecha ?? new DateTime(2026, 1, 15, 10, 0, 0));

        return ruta;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Comparador.Nucleo.Servicios.Rutas.ParaIO(raiz), recursive: true);
        }
        catch (IOException)
        {
            // Una prueba que deja un archivo abierto no debe tumbar a las demás.
        }
    }
}
