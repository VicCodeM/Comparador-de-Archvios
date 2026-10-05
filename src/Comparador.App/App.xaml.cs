using System.Windows;
using Comparador.Nucleo.Ubicaciones;

namespace Comparador.App;

public partial class App : Application
{
    /// <summary>Suelta los teléfonos al salir: si no, Windows los deja "ocupados" hasta desconectarlos.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        CatalogoUbicaciones.CerrarTelefonos();
        base.OnExit(e);
    }
}
