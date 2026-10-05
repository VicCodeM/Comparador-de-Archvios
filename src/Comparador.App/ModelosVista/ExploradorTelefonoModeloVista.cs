using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Comparador.Nucleo.Ubicaciones;

namespace Comparador.App.ModelosVista;

/// <summary>
/// Navegar por las carpetas de un teléfono para elegir una. El diálogo de carpetas de Windows no deja elegir dentro
/// de un teléfono (no tiene letra de unidad), por eso existe esta ventana. Cada carpeta se lee fuera de la ventana.
/// </summary>
public sealed partial class ExploradorTelefonoModeloVista : ObservableObject
{
    private readonly string dispositivo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RutaVisible), nameof(PuedeSubir))]
    [NotifyCanExecuteChangedFor(nameof(SubirCommand))]
    private string carpeta = @"\";

    [ObservableProperty] private bool cargando;
    [ObservableProperty] private string error = string.Empty;

    public ExploradorTelefonoModeloVista(string dispositivo)
    {
        this.dispositivo = dispositivo;
        _ = CargarAsync();
    }

    public string Nombre => dispositivo;

    public ObservableCollection<string> Subcarpetas { get; } = [];

    public string RutaVisible => dispositivo + (Carpeta == @"\" ? string.Empty : Carpeta);

    public bool PuedeSubir => Carpeta != @"\";

    public string RutaElegida => UbicacionTelefono.Componer(dispositivo, Carpeta);

    [RelayCommand]
    private Task EntrarAsync(string? subcarpeta)
    {
        if (subcarpeta is null)
        {
            return Task.CompletedTask;
        }

        Carpeta = Path.Combine(Carpeta, subcarpeta);

        return CargarAsync();
    }

    [RelayCommand(CanExecute = nameof(PuedeSubir))]
    private Task SubirAsync()
    {
        Carpeta = Path.GetDirectoryName(Carpeta) ?? @"\";

        return CargarAsync();
    }

    private async Task CargarAsync()
    {
        Cargando = true;
        Error = string.Empty;
        Subcarpetas.Clear();
        try
        {
            var ruta = RutaElegida;
            foreach (var subcarpeta in await Task.Run(() => CatalogoUbicaciones.ListarSubcarpetas(ruta)))
            {
                Subcarpetas.Add(subcarpeta);
            }

            if (Subcarpetas.Count == 0)
            {
                Error = "Esta carpeta no tiene subcarpetas. Si el teléfono no muestra nada, desbloquéalo y elige \"Transferir archivos\" en la notificación USB.";
            }
        }
        catch (Exception falla) when (falla is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Error = $"No se pudo leer el teléfono: {falla.Message}";
        }
        finally
        {
            Cargando = false;
        }
    }
}
