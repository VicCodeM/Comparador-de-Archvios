using System.Windows;
using Comparador.App.ModelosVista;
using Comparador.App.Vistas;
using Comparador.Nucleo.Ubicaciones;

namespace Comparador.App.Servicios;

/// <summary>Las ventanas propias que abre la app.</summary>
public static class Dialogos
{
    /// <summary>La carpeta del teléfono elegida, como ruta mtp:\\, o null si se canceló.</summary>
    public static string? ElegirCarpetaDeTelefono(DispositivoDisponible telefono)
    {
        var modelo = new ExploradorTelefonoModeloVista(telefono.Nombre);
        var ventana = new ExploradorTelefonoVentana { DataContext = modelo, Owner = Application.Current.MainWindow };

        return ventana.ShowDialog() == true ? modelo.RutaElegida : null;
    }

    /// <summary>True si el usuario confirmó; si el destino es un disco, tuvo que escribir BORRAR.</summary>
    public static bool ConfirmarClonado(string resumen, bool borraDatos) =>
        new ConfirmarClonVentana(resumen, borraDatos) { Owner = Application.Current.MainWindow }.ShowDialog() == true;
}
