using Microsoft.Win32;

namespace Comparador.App.Servicios;

/// <summary>
/// Espejo en segundo plano: icono junto al reloj, arranca con Windows y pega con Ctrl+V en el Explorador. Es
/// opcional (Configuración); al quitarlo no queda nada: ni el arranque con Windows ni la vigilancia del teclado.
/// </summary>
public sealed class ModoResidente : IDisposable
{
    public const string Argumento = "--residente";

    private const string ClaveArranque = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string NombreArranque = "Espejo";

    private readonly Bandeja bandeja;
    private readonly VigilanteTeclado vigilante;

    public ModoResidente(Func<IntPtr, bool> alPegar, Action alAbrir, Action alSalir)
    {
        vigilante = new VigilanteTeclado(alPegar);
        bandeja = new Bandeja(alAbrir, alSalir);
    }

    /// <summary>Si la app se movió de carpeta, el arranque con Windows se corrige solo al volver a activarlo.</summary>
    public static void ArrancarConWindows(bool activo)
    {
        using var clave = Registry.CurrentUser.CreateSubKey(ClaveArranque);
        if (activo)
        {
            clave.SetValue(NombreArranque, $"\"{Environment.ProcessPath}\" {Argumento}");
        }
        else
        {
            clave.DeleteValue(NombreArranque, throwOnMissingValue: false);
        }
    }

    public void Dispose()
    {
        vigilante.Dispose();
        bandeja.Dispose();
    }
}
