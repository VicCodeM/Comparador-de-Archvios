using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Comparador.App.Servicios;
using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
    private readonly AvisosModeloVista avisos;
    private IReadOnlyList<ElementoComparado> todos = [];
    private long seleccionados;
    private long bytesSeleccionados;
    private bool enLote;
    private bool pausado;

    [ObservableProperty] private IReadOnlyList<ElementoComparado> visibles = [];
    [ObservableProperty] private FiltroRevision filtro = FiltroRevision.Pendientes;
    [ObservableProperty] private string busqueda = string.Empty;
    [ObservableProperty] private string duracion = string.Empty;
    [ObservableProperty] private int faltan;
    [ObservableProperty] private int diferentes;
    [ObservableProperty] private int sobran;
    [ObservableProperty] private int problemas;
    [ObservableProperty] private int iguales;

    public RevisionModeloVista(AvisosModeloVista avisos)
    {
        this.avisos = avisos;
        esperaBusqueda.Tick += (_, _) => AplicarFiltro();
    }

    public IReadOnlyList<ElementoComparado> Todos => todos;

    public int Pendientes => Faltan + Diferentes;

    public long Seleccionados => Interlocked.Read(ref seleccionados);

    public string ResumenSeleccion => $"{Seleccionados:N0} seleccionados para copiar  ·  {Formatos.Tamano(Interlocked.Read(ref bytesSeleccionados))}";

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
        OnPropertyChanged(nameof(HayResultado));
        Recontar();
        Filtro = Pendientes > 0 ? FiltroRevision.Pendientes : FiltroRevision.Todos;
        AplicarFiltro();
    }

    public bool HayResultado => todos.Count > 0;

    /// <summary>
    /// Mientras se copia, cada archivo terminado cambia su selección desde otro hilo. Avisar a la pantalla por cada
    /// uno llenaba la cola de la ventana con cientos de miles de repintados y la congelaba: se pausa y al final se
    /// recuenta una sola vez.
    /// </summary>
    public void PausarAvisos() => pausado = true;

    public void ReanudarAvisos()
    {
        pausado = false;
        Recontar();
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
        if (!enLote && !pausado)
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
        avisos.Exito($"Reporte guardado con {Visibles.Count:N0} filas", "Abrir", () => Escritorio.Abrir(archivo));
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
    private async Task QuienLoUsaAsync(ElementoComparado? elemento)
    {
        if (elemento is null)
        {
            return;
        }

        var programas = await Task.Run(() => DetectorBloqueos.QuienLoUsa(elemento.RutaOrigen).Concat(DetectorBloqueos.QuienLoUsa(elemento.RutaDestino)).Distinct().ToList());
        if (programas.Count == 0)
        {
            avisos.Informar($"Ningún programa tiene abierto \"{elemento.Nombre}\" ahora mismo");
        }
        else
        {
            avisos.Advertir($"\"{elemento.Nombre}\" está abierto en: {string.Join(", ", programas)}. Ciérralo y vuelve a copiar.");
        }
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
            avisos.Error($"No se pudo: {error.Message}");
        }
    }
}
