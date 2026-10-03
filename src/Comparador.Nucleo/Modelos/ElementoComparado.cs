using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Comparador.Nucleo.Modelos;

public enum EstadoElemento
{
    Coincide,
    Falta,
    Diferente,
    Sobra,
    SinAcceso,
    Error,
}

/// <summary>
/// Un archivo o carpeta y cómo está en el destino. Avisa de sus cambios (estado, selección, motivo) para que la
/// lista se repinte sola, sin recorrerla entera: así una sincronización de 100.000 archivos no congela la ventana.
/// </summary>
public sealed class ElementoComparado : INotifyPropertyChanged
{
    private EstadoElemento estado;
    private string motivo = string.Empty;
    private bool seleccionado;

    public required ParRutas Par { get; init; }

    public required string RutaRelativa { get; init; }

    public required bool EsCarpeta { get; init; }

    public long? TamanoOrigen { get; init; }

    public long? TamanoDestino { get; set; }

    public DateTime? FechaOrigen { get; init; }

    public DateTime? FechaDestino { get; set; }

    public string Nombre => Path.GetFileName(RutaRelativa);

    public string Extension => EsCarpeta ? string.Empty : Path.GetExtension(RutaRelativa);

    public string RutaOrigen => Path.Combine(Par.Origen, RutaRelativa);

    public string RutaDestino => Path.Combine(Par.Destino, RutaRelativa);

    /// <summary>Lo que se va a copiar: el tamaño del origen (en un sobrante, el del destino).</summary>
    public long TamanoACopiar => EsCarpeta ? 0 : TamanoOrigen ?? 0;

    public bool SePuedeSincronizar => estado is EstadoElemento.Falta or EstadoElemento.Diferente;

    public EstadoElemento Estado
    {
        get => estado;
        set => Cambiar(ref estado, value);
    }

    public string Motivo
    {
        get => motivo;
        set => Cambiar(ref motivo, value);
    }

    public bool Seleccionado
    {
        get => seleccionado;
        set => Cambiar(ref seleccionado, value && SePuedeSincronizar);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Cambiar<T>(ref T campo, T valor, [CallerMemberName] string propiedad = "")
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor))
        {
            return;
        }

        campo = valor;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
    }
}
