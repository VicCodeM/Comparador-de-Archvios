using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace Comparador.App.ModelosVista;

/// <summary>Un aviso abajo de la ventana (estilo InfoBar de Windows 11) que se cierra solo, con una acción opcional.</summary>
public sealed partial class AvisosModeloVista : ObservableObject
{
    private readonly DispatcherTimer cierre = new() { Interval = TimeSpan.FromSeconds(7) };
    private Action? accion;

    [ObservableProperty] private string mensaje = string.Empty;
    [ObservableProperty] private bool visible;
    [ObservableProperty] private InfoBarSeverity severidad = InfoBarSeverity.Informational;
    [ObservableProperty] private string textoAccion = string.Empty;

    public AvisosModeloVista() => cierre.Tick += (_, _) => Cerrar();

    public bool TieneAccion => accion is not null;

    public void Informar(string texto) => Mostrar(texto, InfoBarSeverity.Informational);

    public void Exito(string texto, string? textoAccion = null, Action? alPulsar = null) => Mostrar(texto, InfoBarSeverity.Success, textoAccion, alPulsar);

    public void Advertir(string texto) => Mostrar(texto, InfoBarSeverity.Warning);

    public void Error(string texto) => Mostrar(texto, InfoBarSeverity.Error);

    private void Mostrar(string texto, InfoBarSeverity nivel, string? textoAccion = null, Action? alPulsar = null)
    {
        Mensaje = texto;
        Severidad = nivel;
        TextoAccion = textoAccion ?? string.Empty;
        accion = alPulsar;
        OnPropertyChanged(nameof(TieneAccion));
        Visible = true;
        cierre.Stop();
        cierre.Start();
    }

    [RelayCommand]
    private void Cerrar()
    {
        cierre.Stop();
        Visible = false;
    }

    [RelayCommand]
    private void Ejecutar()
    {
        accion?.Invoke();
        Cerrar();
    }
}
