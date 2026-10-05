using Comparador.Nucleo.Ubicaciones;

namespace Comparador.Nucleo.Modelos;

/// <summary>Una carpeta de origen y la carpeta de destino con la que se compara (de disco, USB, red o teléfono).</summary>
public sealed record ParRutas(string Origen, string Destino)
{
    public bool EsLaMismaCarpeta => string.Equals(Normalizar(Origen), Normalizar(Destino), StringComparison.OrdinalIgnoreCase);

    public bool EntreTelefonos => UbicacionTelefono.EsRutaDeTelefono(Origen) && UbicacionTelefono.EsRutaDeTelefono(Destino);

    public override string ToString() => $"{Origen} -> {Destino}";

    /// <summary>Solo texto: no toca el disco, así no se congela con una carpeta de red caída.</summary>
    private static string Normalizar(string ruta) => ruta.Trim().TrimEnd('\\', '/');
}
