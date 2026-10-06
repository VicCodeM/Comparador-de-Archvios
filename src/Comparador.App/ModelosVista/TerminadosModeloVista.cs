using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Comparador.App.Servicios;
using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;

namespace Comparador.App.ModelosVista;

public enum FiltroTerminados
{
    Todos,
    Copiados,
    Saltados,
    ConError,
}

/// <summary>Un archivo terminado, como se ve en la lista.</summary>
public sealed record FilaTerminada(
    string Nombre, string Carpeta, string RutaOrigen, string RutaDestino, string Tamano, ResultadoArchivo Resultado, string Mensaje, string Hora)
{
    public static FilaTerminada Desde(ArchivoTerminado terminado) => new(
        Path.GetFileName(terminado.RutaRelativa),
        Path.GetDirectoryName(terminado.RutaRelativa) ?? string.Empty,
        terminado.RutaOrigen,
        terminado.RutaDestino,
        terminado.Tamano > 0 ? Formatos.Tamano(terminado.Tamano) : string.Empty,
        terminado.Resultado,
        terminado.Mensaje,
        terminado.Hora.ToString("HH:mm:ss"));
}

/// <summary>
/// Todo lo que terminó en una copia, con filtro, búsqueda y acciones por archivo. Mientras se copia solo se pintan
/// los últimos (rehacer una lista de cien mil filas cuatro veces por segundo congelaría la ventana); al terminar se
/// muestra completa.
/// </summary>
public sealed partial class TerminadosModeloVista(AccionesArchivo acciones) : ObservableObject
{
    private const int MaximoEnVivo = 200;
    private readonly List<FilaTerminada> todos = [];
    private bool enVivo;

    [ObservableProperty] private IReadOnlyList<FilaTerminada> lista = [];
    [ObservableProperty] private FiltroTerminados filtro = FiltroTerminados.Todos;
    [ObservableProperty] private string busqueda = string.Empty;
    [ObservableProperty] private int correctos;
    [ObservableProperty] private int saltados;
    [ObservableProperty] private int fallidos;

    public int Total => Correctos + Saltados + Fallidos;

    public bool EnVivo => enVivo;

    public void Empezar()
    {
        todos.Clear();
        Correctos = 0;
        Saltados = 0;
        Fallidos = 0;
        Filtro = FiltroTerminados.Todos;
        Busqueda = string.Empty;
        enVivo = true;
        Refrescar();
    }

    public void Agregar(IReadOnlyList<ArchivoTerminado> lote)
    {
        if (lote.Count == 0)
        {
            return;
        }

        todos.AddRange(lote.Select(FilaTerminada.Desde));
        Correctos += lote.Count(terminado => terminado.Resultado == ResultadoArchivo.Copiado);
        Saltados += lote.Count(terminado => terminado.Resultado == ResultadoArchivo.Saltado);
        Fallidos += lote.Count(terminado => terminado.Resultado == ResultadoArchivo.Fallido);
        Refrescar();
    }

    public void Terminar()
    {
        enVivo = false;
        Refrescar();
    }

    partial void OnCorrectosChanged(int value) => OnPropertyChanged(nameof(Total));

    partial void OnFallidosChanged(int value) => OnPropertyChanged(nameof(Total));

    partial void OnSaltadosChanged(int value) => OnPropertyChanged(nameof(Total));

    partial void OnFiltroChanged(FiltroTerminados value) => Refrescar();

    partial void OnBusquedaChanged(string value) => Refrescar();

    /// <summary>Lo más reciente arriba.</summary>
    private void Refrescar()
    {
        var texto = Busqueda.Trim();
        var coinciden = Enumerable.Range(0, todos.Count)
            .Select(i => todos[todos.Count - 1 - i])
            .Where(CumpleFiltro)
            .Where(fila => texto.Length == 0 || fila.Nombre.Contains(texto, StringComparison.CurrentCultureIgnoreCase)
                || fila.Carpeta.Contains(texto, StringComparison.CurrentCultureIgnoreCase));
        Lista = (enVivo ? coinciden.Take(MaximoEnVivo) : coinciden).ToList();
        OnPropertyChanged(nameof(EnVivo));
    }

    private bool CumpleFiltro(FilaTerminada fila) => Filtro switch
    {
        FiltroTerminados.Copiados => fila.Resultado == ResultadoArchivo.Copiado,
        FiltroTerminados.Saltados => fila.Resultado == ResultadoArchivo.Saltado,
        FiltroTerminados.ConError => fila.Resultado == ResultadoArchivo.Fallido,
        _ => true,
    };

    [RelayCommand]
    private void AbrirCopia(FilaTerminada? fila) => Con(fila, f => acciones.Abrir(f.RutaDestino));

    [RelayCommand]
    private void AbrirOrigen(FilaTerminada? fila) => Con(fila, f => acciones.Abrir(f.RutaOrigen));

    [RelayCommand]
    private void MostrarDestino(FilaTerminada? fila) => Con(fila, f => acciones.Mostrar(f.RutaDestino));

    [RelayCommand]
    private void MostrarOrigen(FilaTerminada? fila) => Con(fila, f => acciones.Mostrar(f.RutaOrigen));

    [RelayCommand]
    private void CopiarRuta(FilaTerminada? fila) => Con(fila, f => acciones.Copiar(f.RutaDestino, "la ruta"));

    [RelayCommand]
    private void CopiarMotivo(FilaTerminada? fila) => Con(fila, f => acciones.Copiar(f.Mensaje, "el motivo"));

    [RelayCommand]
    private Task QuienLoUsaAsync(FilaTerminada? fila) =>
        fila is null ? Task.CompletedTask : acciones.QuienLoUsaAsync(fila.Nombre, fila.RutaOrigen, fila.RutaDestino);

    private static void Con(FilaTerminada? fila, Action<FilaTerminada> accion)
    {
        if (fila is not null)
        {
            accion(fila);
        }
    }
}
