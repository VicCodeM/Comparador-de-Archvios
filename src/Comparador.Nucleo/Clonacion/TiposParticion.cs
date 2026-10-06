namespace Comparador.Nucleo.Clonacion;

/// <summary>El nombre de lo que guarda cada partición, según su código de tipo. Null: entrada vacía o contenedora.</summary>
public static class TiposParticion
{
    private static readonly Dictionary<Guid, string> Gpt = new()
    {
        [new Guid("C12A7328-F81F-11D2-BA4B-00A0C93EC93B")] = "Arranque EFI",
        [new Guid("E3C9E316-0B5C-4DB8-817D-F92DF00215AE")] = "Reservada de Microsoft",
        [new Guid("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7")] = "Datos",
        [new Guid("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC")] = "Recuperación de Windows",
        [new Guid("0FC63DAF-8483-4772-8E79-3D69D8477DE4")] = "Linux",
        [new Guid("0657FD6D-A4AB-43C4-84E5-0933C84B4F4F")] = "Linux swap",
        [new Guid("E6D6D379-F507-44C2-A23C-238F2A3DF928")] = "Linux LVM",
        [new Guid("21686148-6449-6E6F-744E-656564454649")] = "Arranque BIOS",
    };

    public static string? DeGpt(Guid tipo) => tipo == Guid.Empty ? null : Gpt.GetValueOrDefault(tipo, "Otra");

    public static string? DeMbr(byte tipo) => tipo switch
    {
        0x00 or 0x05 or 0x0F or 0x85 => null,
        0x07 => "Datos (NTFS/exFAT)",
        0x0B or 0x0C => "FAT32",
        0x04 or 0x06 or 0x0E => "FAT16",
        0x27 => "Recuperación de Windows",
        0x82 => "Linux swap",
        0x83 => "Linux",
        0x8E => "Linux LVM",
        0xEE => "Protección GPT",
        0xEF => "Arranque EFI",
        _ => $"Otra (0x{tipo:X2})",
    };
}
