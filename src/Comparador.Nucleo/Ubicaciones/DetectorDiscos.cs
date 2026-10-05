using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Comparador.Nucleo.Ubicaciones;

public enum MedioDisco
{
    Desconocido,
    Ssd,
    Mecanico,
}

public enum BusDisco
{
    Desconocido,
    Usb,
    Sata,
    Nvme,
    Sas,
    Tarjeta,
    Virtual,
    Otro,
}

/// <summary>
/// Cómo es de verdad el disco detrás de una ruta, preguntado a Windows y no supuesto: si tiene cabezal (mecánico) o
/// no (SSD), por qué bus va y qué disco físico es. Dos letras en el mismo disco físico comparten velocidad.
/// </summary>
public sealed record PerfilDisco(TipoUbicacion Tipo, MedioDisco Medio, BusDisco Bus, int? NumeroDisco, string Modelo)
{
    public static PerfilDisco DeRed { get; } = new(TipoUbicacion.Red, MedioDisco.Desconocido, BusDisco.Desconocido, null, string.Empty);

    public static PerfilDisco DeTelefono { get; } = new(TipoUbicacion.Telefono, MedioDisco.Desconocido, BusDisco.Usb, null, string.Empty);

    public string Descripcion => Tipo switch
    {
        TipoUbicacion.Red => "Unidad de red",
        TipoUbicacion.Telefono => "Teléfono (MTP)",
        _ => string.Join(" ", new[] { NombreMedio, NombreBus, Modelo }.Where(parte => parte.Length > 0)),
    };

    private string NombreMedio => Medio switch
    {
        MedioDisco.Ssd => "SSD",
        MedioDisco.Mecanico => "Disco mecánico",
        _ => "Disco",
    };

    private string NombreBus => Bus switch
    {
        BusDisco.Usb => "USB",
        BusDisco.Sata => "SATA",
        BusDisco.Nvme => "NVMe",
        BusDisco.Sas => "SAS",
        BusDisco.Tarjeta => "tarjeta SD",
        _ => string.Empty,
    };
}

/// <summary>
/// Detecta el perfil del disco con las consultas de almacenamiento de Windows (las mismas que usa el desfragmentador
/// para saber si un disco es SSD). No necesita permisos de administrador. Si Windows no responde, devuelve
/// "desconocido" y el motor se guía solo por la velocidad que mida.
/// </summary>
public static class DetectorDiscos
{
    private const uint ConsultarPropiedad = 0x2D1400;
    private const uint ExtensionesDeVolumen = 0x560000;
    private const int PropiedadDispositivo = 0;
    private const int PropiedadPenalizacionBusqueda = 7;
    private const uint CompartirLecturaEscritura = 0x3;
    private const uint AbrirExistente = 3;
    private const int TamanoRespuesta = 1024;

    public static PerfilDisco Detectar(string ruta)
    {
        var tipo = CatalogoUbicaciones.TipoDe(ruta);
        if (tipo == TipoUbicacion.Telefono)
        {
            return PerfilDisco.DeTelefono;
        }

        var unidad = Path.GetPathRoot(Path.GetFullPath(ruta));
        if (tipo == TipoUbicacion.Red || string.IsNullOrEmpty(unidad) || unidad.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return PerfilDisco.DeRed;
        }

        using var volumen = CreateFile($@"\\.\{unidad.TrimEnd('\\')}", 0, CompartirLecturaEscritura, IntPtr.Zero, AbrirExistente, 0, IntPtr.Zero);
        if (volumen.IsInvalid)
        {
            return new PerfilDisco(tipo, MedioDisco.Desconocido, BusDisco.Desconocido, null, string.Empty);
        }

        var (bus, modelo) = LeerDispositivo(volumen);

        return new PerfilDisco(tipo, LeerMedio(volumen), bus, LeerNumeroDisco(volumen), modelo);
    }

    private static MedioDisco LeerMedio(SafeFileHandle volumen)
    {
        var respuesta = Consultar(volumen, PropiedadPenalizacionBusqueda);
        if (respuesta is null || respuesta.Length < 9)
        {
            return MedioDisco.Desconocido;
        }

        // DEVICE_SEEK_PENALTY_DESCRIPTOR: Version (4), Size (4), IncursSeekPenalty (1).
        return respuesta[8] != 0 ? MedioDisco.Mecanico : MedioDisco.Ssd;
    }

    private static (BusDisco Bus, string Modelo) LeerDispositivo(SafeFileHandle volumen)
    {
        var respuesta = Consultar(volumen, PropiedadDispositivo);
        if (respuesta is null || respuesta.Length < 32)
        {
            return (BusDisco.Desconocido, string.Empty);
        }

        // STORAGE_DEVICE_DESCRIPTOR: ProductIdOffset en el byte 16, BusType en el 28.
        var bus = BitConverter.ToUInt32(respuesta, 28) switch
        {
            7 => BusDisco.Usb,
            0xB => BusDisco.Sata,
            0x11 => BusDisco.Nvme,
            0xA => BusDisco.Sas,
            0xC or 0xD => BusDisco.Tarjeta,
            0xE or 0xF => BusDisco.Virtual,
            0 => BusDisco.Desconocido,
            _ => BusDisco.Otro,
        };

        return (bus, LeerTexto(respuesta, BitConverter.ToInt32(respuesta, 16)));
    }

    private static string LeerTexto(byte[] respuesta, int inicio)
    {
        if (inicio <= 0 || inicio >= respuesta.Length)
        {
            return string.Empty;
        }

        var fin = Array.IndexOf(respuesta, (byte)0, inicio);

        return System.Text.Encoding.ASCII.GetString(respuesta, inicio, (fin < 0 ? respuesta.Length : fin) - inicio).Trim();
    }

    /// <summary>El número del disco físico, o null si el volumen ocupa varios discos (RAID por software).</summary>
    private static int? LeerNumeroDisco(SafeFileHandle volumen)
    {
        var respuesta = new byte[TamanoRespuesta];
        if (!DeviceIoControl(volumen, ExtensionesDeVolumen, null, 0, respuesta, respuesta.Length, out _, IntPtr.Zero))
        {
            return null;
        }

        // VOLUME_DISK_EXTENTS: NumberOfDiskExtents (4) + relleno (4) + DISK_EXTENT { DiskNumber (4) ... }.
        return BitConverter.ToUInt32(respuesta, 0) == 1 ? BitConverter.ToInt32(respuesta, 8) : null;
    }

    private static byte[]? Consultar(SafeFileHandle volumen, int propiedad)
    {
        // STORAGE_PROPERTY_QUERY: PropertyId (4), QueryType (4) = estándar, AdditionalParameters (4).
        var consulta = new byte[12];
        BitConverter.GetBytes(propiedad).CopyTo(consulta, 0);
        var respuesta = new byte[TamanoRespuesta];

        return DeviceIoControl(volumen, ConsultarPropiedad, consulta, consulta.Length, respuesta, respuesta.Length, out var leidos, IntPtr.Zero)
            ? respuesta[..leidos]
            : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string nombre, uint acceso, uint compartir, IntPtr seguridad, uint creacion, uint atributos, IntPtr plantilla);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle dispositivo, uint codigo, byte[]? entrada, int tamanoEntrada, byte[] salida, int tamanoSalida, out int devueltos, IntPtr superpuesto);
}
