using Comparador.Nucleo.Servicios;
using Comparador.Nucleo.Ubicaciones;

namespace Comparador.Nucleo.Clonacion;

/// <summary>Un disco entero tal como lo ve Windows (\\.\PhysicalDriveN): SSD, disco, memoria USB o tarjeta SD.</summary>
public sealed record DiscoFisico(
    int Numero,
    string Modelo,
    string Serie,
    BusDisco Bus,
    long Tamano,
    int TamanoSector,
    bool Extraible,
    bool EsDeWindows,
    EstiloParticiones Estilo,
    IReadOnlyList<Particion> Particiones)
{
    public IReadOnlyList<string> Letras => Particiones.Where(particion => particion.Letra is not null).Select(particion => particion.Letra!).ToList();

    public string Ruta => $@"\\.\PhysicalDrive{Numero}";

    /// <summary>Un lector de tarjetas vacío aparece como disco de 0 bytes: no se puede clonar ni desde ni hacia él.</summary>
    public bool TieneMedio => Tamano > 0;

    public string Titulo => Modelo.Length > 0 ? Modelo : $"Disco {Numero}";

    public string Detalle => string.Join(" · ", new[]
    {
        $"Disco {Numero}",
        Formatos.Tamano(Tamano),
        NombreBus,
        Estilo == EstiloParticiones.SinParticiones ? "sin particiones" : Estilo.ToString().ToUpperInvariant(),
        Letras.Count > 0 ? string.Join(" ", Letras) : "sin letra",
        Serie.Length > 0 ? $"serie {Serie}" : string.Empty,
    }.Where(parte => parte.Length > 0));

    private string NombreBus => Bus switch
    {
        BusDisco.Usb => "USB",
        BusDisco.Sata => "SATA",
        BusDisco.Nvme => "NVMe",
        BusDisco.Sas => "SAS",
        BusDisco.Tarjeta => "Tarjeta SD",
        BusDisco.Virtual => "Virtual",
        _ => Extraible ? "Extraíble" : string.Empty,
    };
}
