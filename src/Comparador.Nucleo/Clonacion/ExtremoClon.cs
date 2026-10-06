namespace Comparador.Nucleo.Clonacion;

/// <summary>
/// Un extremo de la clonación: un disco entero, una partición de un disco o un archivo de imagen (.img, y como
/// origen también comprimido: .img.xz, .img.gz o .zip, como vienen las de Raspberry Pi). Se escribe como texto para
/// pasárselo al Espejo elevado: "disco:2", "particion:2:1" o la ruta del archivo.
/// </summary>
public abstract record ExtremoClon
{
    private const string PrefijoDisco = "disco:";
    private const string PrefijoParticion = "particion:";

    public sealed record DeDisco(int Disco) : ExtremoClon;

    public sealed record DeParticion(int Disco, int Particion) : ExtremoClon;

    public sealed record DeImagen(string Ruta) : ExtremoClon;

    public int? NumeroDisco => this switch
    {
        DeDisco disco => disco.Disco,
        DeParticion particion => particion.Disco,
        _ => null,
    };

    public string Texto => this switch
    {
        DeDisco disco => $"{PrefijoDisco}{disco.Disco}",
        DeParticion particion => $"{PrefijoParticion}{particion.Disco}:{particion.Particion}",
        DeImagen imagen => imagen.Ruta,
        _ => throw new InvalidOperationException(),
    };

    public static ExtremoClon Interpretar(string texto)
    {
        if (texto.StartsWith(PrefijoDisco, StringComparison.OrdinalIgnoreCase) && int.TryParse(texto[PrefijoDisco.Length..], out var disco))
        {
            return new DeDisco(disco);
        }

        var partes = texto.StartsWith(PrefijoParticion, StringComparison.OrdinalIgnoreCase) ? texto[PrefijoParticion.Length..].Split(':') : [];

        return partes.Length == 2 && int.TryParse(partes[0], out var enDisco) && int.TryParse(partes[1], out var particion)
            ? new DeParticion(enDisco, particion)
            : new DeImagen(texto);
    }

    /// <summary>Qué trozo de qué disco es, con el estado actual de los discos. Null si es una imagen o ya no existe.</summary>
    public (DiscoFisico Disco, long Inicio, long Largo)? Tramo(IReadOnlyList<DiscoFisico> discos)
    {
        var disco = discos.FirstOrDefault(candidato => candidato.Numero == NumeroDisco);

        return (this, disco) switch
        {
            (DeDisco, not null) => (disco, 0, disco.Tamano),
            (DeParticion particion, not null) when disco.Particiones.FirstOrDefault(p => p.Numero == particion.Particion) is { } trozo
                => (disco, trozo.Inicio, trozo.Tamano),
            _ => null,
        };
    }
}
