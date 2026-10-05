namespace Comparador.Nucleo.Modelos;

public enum ModoComparacion
{
    /// <summary>Tamaño y fecha de modificación. Rápido: no lee el contenido.</summary>
    Rapido,

    /// <summary>Además lee el contenido y compara su huella SHA-256. Lento, pero seguro.</summary>
    Exacto,
}

public sealed record OpcionesComparacion
{
    public ModoComparacion Modo { get; init; } = ModoComparacion.Rapido;

    /// <summary>Mostrar también lo que está en el destino y no en el origen.</summary>
    public bool DetectarSobrantes { get; init; } = true;

    /// <summary>Margen para las fechas: FAT32, USB y muchos NAS guardan la hora con 2 segundos de precisión.</summary>
    public TimeSpan ToleranciaFecha { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Nombres o comodines a ignorar, por ejemplo "node_modules", ".git", "*.tmp".</summary>
    public IReadOnlyList<string> Exclusiones { get; init; } = [];

    /// <summary>Lecturas simultáneas al comparar contenido; null = según los dispositivos (ver <c>Concurrencia</c>).</summary>
    public int? HilosManuales { get; init; }
}
