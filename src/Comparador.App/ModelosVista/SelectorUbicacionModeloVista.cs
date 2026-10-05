using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Comparador.App.Servicios;
using Comparador.Nucleo.Ubicaciones;
using Wpf.Ui.Controls;

namespace Comparador.App.ModelosVista;

/// <summary>
/// Elegir el origen o el destino: escribir la ruta, buscarla con el diálogo de Windows (que ya incluye USB y red) o
/// tomarla de la lista de dispositivos conectados, teléfonos incluidos. La lista se busca fuera de la ventana:
/// una unidad de red caída tarda en responder y antes eso congelaba todo.
/// </summary>
public sealed partial class SelectorUbicacionModeloVista(string titulo, AvisosModeloVista avisos) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Icono), nameof(DescripcionTipo))]
    private string ruta = string.Empty;

    [ObservableProperty] private bool buscandoDispositivos;

    public string Titulo { get; } = titulo;

    public ObservableCollection<DispositivoDisponible> Dispositivos { get; } = [];

    public SymbolRegular Icono => IconosUbicacion.Para(Tipo);

    public string DescripcionTipo => Ruta.Length == 0 ? "Sin elegir" : CatalogoUbicaciones.NombreTipo(Tipo);

    private TipoUbicacion Tipo => CatalogoUbicaciones.TipoDe(Ruta);

    [RelayCommand]
    private void Examinar()
    {
        var inicial = UbicacionTelefono.EsRutaDeTelefono(Ruta) ? null : Ruta;
        if (Escritorio.ElegirCarpeta($"Elegir {Titulo.ToLowerInvariant()}", inicial) is { } elegida)
        {
            Ruta = elegida;
        }
    }

    [RelayCommand]
    private async Task ActualizarDispositivosAsync()
    {
        BuscandoDispositivos = true;
        try
        {
            var encontrados = await Task.Run(CatalogoUbicaciones.ListarConectados);
            Dispositivos.Clear();
            foreach (var dispositivo in encontrados)
            {
                Dispositivos.Add(dispositivo);
            }
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            avisos.Error($"No se pudo revisar qué hay conectado: {error.Message}");
        }
        finally
        {
            BuscandoDispositivos = false;
        }
    }

    [RelayCommand]
    private void ElegirDispositivo(DispositivoDisponible? dispositivo)
    {
        if (dispositivo is null)
        {
            return;
        }

        var elegida = dispositivo.Tipo == TipoUbicacion.Telefono
            ? Dialogos.ElegirCarpetaDeTelefono(dispositivo)
            : Escritorio.ElegirCarpeta($"Elegir {Titulo.ToLowerInvariant()} en {dispositivo.Nombre}", dispositivo.Ruta);
        if (elegida is not null)
        {
            Ruta = elegida;
        }
    }
}
