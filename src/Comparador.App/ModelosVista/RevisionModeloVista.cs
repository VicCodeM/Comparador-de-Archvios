using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Comparador.App.Servicios;
using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;

namespace Comparador.App.ModelosVista;

public enum FiltroRevision
{
    Pendientes,
    Faltan,
    Diferentes,
    Sobran,
    Problemas,
    Iguales,
    Todos,
}

/// <summary>
/// La lista de resultados. Filtra y busca sobre una lista normal (sin colecciones que avisen por cada fila) y lleva
/// la cuenta de lo seleccionado sumando y restando: "seleccionar todo" con 100.000 filas no recorre la lista 100.000 veces.
/// </summary>
public sealed partial class RevisionModeloVista : ObservableObject
{
    private readonly DispatcherTimer esperaBusqueda = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly ISnackbarMessageQueue avisos;
    private IReadOnlyList<ElementoComparado> todos = [];
    private long seleccionados;
    private long bytesSeleccionados;
    private bool enLote;

    [ObservableProperty] private IReadOnlyList<ElementoComparado> visibles = [];
    [ObservableProperty] private FiltroRevision filtro = FiltroRevision.Pendientes;
    [ObservableProperty] private string busqueda = string.Empty;
    [ObservableProperty] private string duracion = string.Empty;
    [ObservableProperty] private int faltan;
    [ObservableProperty] private int diferentes;
    [ObservableProperty] private int sobran;
    [ObservableProperty] private int problemas;
    [ObservableProperty] private int iguales;

    public RevisionModeloVista(ISnackbarMessageQueue avisos)
    {
        this.avisos = avisos;
        esperaBusqueda.Tick += (_, _) => AplicarFiltro();
    }

    public IReadOnlyList<ElementoComparado> Todos => todos;

    public int Pendientes => Faltan + Diferentes;

    public long Seleccionados => Interlocked.Read(ref seleccionados);

    public string ResumenSeleccion => $"{Seleccionados:N0} seleccionados para sincronizar  ·  {Formatos.Tamano(Interlocked.Read(ref bytesSeleccionados))}";

    public bool TodoSincronizado => Pendientes == 0;

    public void Cargar(ResultadoComparacion resultado)
    {
        foreach (var elemento in todos)
        {
            elemento.PropertyChanged -= AlCambiarElemento;
        }

        todos = resultado.Elementos;
        foreach (var elemento in todos)
        {
            elemento.PropertyChanged += AlCambiarElemento;
        }

        Duracion = Formatos.Duracion(resultado.Duracion);
        Recontar();
        Filtro = Pendientes > 0 ? FiltroRevision.Pendientes : FiltroRevision.Todos;
        AplicarFiltro();
    }

    /// <summary>Vuelve a contar todo (al cargar y al terminar una sincronización, no por cada fila).</summary>
    public void Recontar()
    {
        Faltan = todos.Count(e => e.Estado == EstadoElemento.Falta);
        Diferentes = todos.Count(e => e.Estado == EstadoElemento.Diferente);
        Sobran = todos.Count(e => e.Estado == EstadoElemento.Sobra);
        Problemas = todos.Count(e => e.Estado is EstadoElemento.SinAcceso or EstadoElemento.Error);
        Iguales = todos.Count(e => e.Estado == EstadoElemento.Coincide);
        Interlocked.Exchange(ref seleccionados, todos.Count(e => e.Seleccionado));
        Interlocked.Exchange(ref bytesSeleccionados, todos.Where(e => e.Seleccionado).Sum(e => e.TamanoACopiar));
        OnPropertyChanged(nameof(Pendientes));
        OnPropertyChanged(nameof(TodoSincronizado));
        AvisarSeleccion();
    }

    partial void OnFiltroChanged(FiltroRevision value) => AplicarFiltro();

    [RelayCommand]
    public void FijarFiltro(FiltroRevision nuevo) => Filtro = nuevo;

    partial void OnBusquedaChanged(string value)
    {
        esperaBusqueda.Stop();
        esperaBusqueda.Start();
    }

    private void AplicarFiltro()
    {
        esperaBusqueda.Stop();
        var texto = Busqueda.Trim();
        Visibles = todos
            .Where(CumpleFiltro)
            .Where(e => texto.Length == 0 || e.RutaRelativa.Contains(texto, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    private bool CumpleFiltro(ElementoComparado elemento) => Filtro switch
    {
        FiltroRevision.Pendientes => elemento.SePuedeSincronizar,
        FiltroRevision.Faltan => elemento.Estado == EstadoElemento.Falta,
        FiltroRevision.Diferentes => elemento.Estado == EstadoElemento.Diferente,
        FiltroRevision.Sobran => elemento.Estado == EstadoElemento.Sobra,
        FiltroRevision.Problemas => elemento.Estado is EstadoElemento.SinAcceso or EstadoElemento.Error,
        FiltroRevision.Iguales => elemento.Estado == EstadoElemento.Coincide,
        _ => true,
    };

    private void AlCambiarElemento(object? origen, PropertyChangedEventArgs cambio)
    {
        if (cambio.PropertyName != nameof(ElementoComparado.Seleccionado) || origen is not ElementoComparado elemento)
        {
            return;
        }

        var signo = elemento.Seleccionado ? 1 : -1;
        Interlocked.Add(ref seleccionados, signo);
        Interlocked.Add(ref bytesSeleccionados, signo * elemento.TamanoACopiar);
        if (!enLote)
        {
            AvisarSeleccion();
        }
    }

    private void AvisarSeleccion()
    {
        OnPropertyChanged(nameof(Seleccionados));
        OnPropertyChanged(nameof(ResumenSeleccion));
    }

    [RelayCommand]
    private void SeleccionarVisibles() => MarcarVisibles(true);

    [RelayCommand]
    private void QuitarSeleccionVisibles() => MarcarVisibles(false);

    /// <summary>Marca todas las filas visibles y avisa a la pantalla UNA vez al final, no una por fila.</summary>
    private void MarcarVisibles(bool valor)
    {
        enLote = true;
        try
        {
            foreach (var elemento in Visibles)
            {
                elemento.Seleccionado = valor;
            }
        }
        finally
        {
            enLote = false;
            AvisarSeleccion();
        }
    }

    [RelayCommand]
    private async Task ExportarAsync()
    {
        var archivo = Escritorio.ElegirDondeGuardar("Guardar reporte", $"comparacion-{DateTime.Now:yyyy-MM-dd-HHmm}.csv", "Hoja de cálculo CSV|*.csv");
        if (archivo is null)
        {
            return;
        }

        await ExportadorReporte.GuardarCsvAsync(archivo, Visibles, CancellationToken.None);
        avisos.Enqueue($"Reporte guardado con {Visibles.Count:N0} filas", "ABRIR", () => Escritorio.Abrir(archivo));
    }

    [RelayCommand]
    private void AbrirOrigen(ElementoComparado? elemento) => Intentar(() => Escritorio.Abrir(elemento!.RutaOrigen), elemento);

    [RelayCommand]
    private void MostrarOrigen(ElementoComparado? elemento) => Intentar(() => Escritorio.MostrarEnCarpeta(elemento!.RutaOrigen), elemento);

    [RelayCommand]
    private void MostrarDestino(ElementoComparado? elemento) => Intentar(() => Escritorio.MostrarEnCarpeta(elemento!.RutaDestino), elemento);

    [RelayCommand]
    private void CopiarRuta(ElementoComparado? elemento) => Intentar(() => Clipboard.SetText(elemento!.RutaOrigen), elemento);

    [RelayCommand]
    private void QuienLoUsa(ElementoComparado? elemento)
    {
        if (elemento is null)
        {
            return;
        }

        var programas = DetectorBloqueos.QuienLoUsa(elemento.RutaOrigen).Concat(DetectorBloqueos.QuienLoUsa(elemento.RutaDestino)).Distinct().ToList();
        avisos.Enqueue(programas.Count == 0
            ? $"Ningún programa tiene abierto \"{elemento.Nombre}\" ahora mismo"
            : $"\"{elemento.Nombre}\" está abierto en: {string.Join(", ", programas)}. Ciérralo y vuelve a sincronizar.");
    }

    private void Intentar(Action accion, ElementoComparado? elemento)
    {
        if (elemento is null)
        {
            return;
        }

        try
        {
            accion();
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or System.IO.IOException or System.Runtime.InteropServices.ExternalException)
        {
            avisos.Enqueue($"No se pudo: {error.Message}");
        }
    }
}
