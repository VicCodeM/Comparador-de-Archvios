namespace Comparador.Nucleo.Modelos;

/// <summary>Una carpeta de origen y la carpeta de destino con la que se compara.</summary>
public sealed record ParRutas(string Origen, string Destino)
{
    public bool OrigenExiste => Directory.Exists(Origen);

    public bool DestinoExiste => Directory.Exists(Destino);

    public bool EsValido => OrigenExiste && DestinoExiste && !EsLaMismaCarpeta;

    public bool EsLaMismaCarpeta =>
        string.Equals(Path.GetFullPath(Origen).TrimEnd('\\'), Path.GetFullPath(Destino).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    public ParRutas Intercambiado() => new(Destino, Origen);

    public override string ToString() => $"{Origen} -> {Destino}";
}
