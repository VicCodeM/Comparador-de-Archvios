using Comparador.Nucleo.Servicios;

namespace Comparador.Nucleo.Clonacion;

public enum EstiloParticiones
{
    Mbr,
    Gpt,
    SinParticiones,
}

/// <summary>
/// Un trozo del disco: dónde empieza, cuánto mide, qué es y, si Windows lo monta, con qué letra. Para rehacerlo en
/// otro disco se guarda su tipo exacto (GUID en GPT, byte en MBR), sus atributos y, si tiene volumen, cuánto ocupa.
/// </summary>
public sealed record Particion(
    int Numero,
    long Inicio,
    long Tamano,
    string Tipo,
    string? Letra,
    Guid TipoGpt = default,
    byte TipoMbr = 0,
    ulong AtributosGpt = 0,
    bool Activa = false,
    string? Volumen = null,
    string? SistemaArchivos = null,
    long? Usado = null,
    string Etiqueta = "")
{
    public static readonly Guid TipoDatosGpt = new("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7");

    /// <summary>
    /// Se puede rehacer de otro tamaño copiando sus archivos: un volumen NTFS de datos (o con Windows) del que se
    /// sabe lo ocupado. EFI, Reservada, Recuperación, Linux y lo desconocido se copian tal cual, sector a sector.
    /// </summary>
    public bool Ajustable => Usado is not null && string.Equals(SistemaArchivos, "NTFS", StringComparison.OrdinalIgnoreCase)
        && (TipoGpt == TipoDatosGpt || TipoMbr is 0x07);

    public string Titulo => $"Partición {Numero}";

    public string Detalle => string.Join(" · ", new[] { Formatos.Tamano(Tamano), Tipo, Letra ?? "sin letra" });
}
