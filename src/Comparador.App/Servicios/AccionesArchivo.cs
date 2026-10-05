using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Comparador.App.ModelosVista;
using Comparador.Nucleo.Servicios;

namespace Comparador.App.Servicios;

/// <summary>
/// Lo que se puede hacer con un archivo desde cualquier lista (la comparación o los terminados de una copia):
/// abrirlo, mostrarlo en el Explorador, copiar su ruta o ver qué programa lo tiene abierto. Un error se muestra
/// como aviso, nunca tumba la app.
/// </summary>
public sealed class AccionesArchivo(AvisosModeloVista avisos)
{
    public void Abrir(string ruta) => Intentar(() => Escritorio.Abrir(ruta));

    public void Mostrar(string ruta) => Intentar(() => Escritorio.MostrarEnCarpeta(ruta));

    /// <param name="queEs">Lo que se copió, para el aviso: "la ruta", "el motivo"...</param>
    public void Copiar(string texto, string queEs) => Intentar(() =>
    {
        Clipboard.SetText(texto);
        avisos.Informar($"Se copió {queEs}");
    });

    /// <summary>Preguntarle a Windows puede tardar: se hace fuera de la ventana.</summary>
    public async Task QuienLoUsaAsync(string nombre, params string[] rutas)
    {
        var programas = await Task.Run(() => rutas.SelectMany(DetectorBloqueos.QuienLoUsa).Distinct().ToList());
        if (programas.Count == 0)
        {
            avisos.Informar($"Ningún programa tiene abierto \"{nombre}\" ahora mismo");
            return;
        }

        avisos.Advertir($"\"{nombre}\" está abierto en: {string.Join(", ", programas)}. Ciérralo y vuelve a copiar.");
    }

    private void Intentar(Action accion)
    {
        try
        {
            accion();
        }
        catch (Exception error) when (error is Win32Exception or IOException or ExternalException)
        {
            avisos.Error($"No se pudo: {error.Message}");
        }
    }
}
