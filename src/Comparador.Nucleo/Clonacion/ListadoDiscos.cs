using Comparador.Nucleo.Ubicaciones;
using Microsoft.Win32.SafeHandles;

namespace Comparador.Nucleo.Clonacion;

/// <summary>
/// Los discos físicos conectados y sus particiones, preguntados a Windows sin permisos de administrador: abrir un
/// disco "sin acceso" basta para leer su modelo, serie, bus, tamaño y tabla de particiones. Leer o escribir sus
/// datos sí exige administrador.
/// </summary>
public static class ListadoDiscos
{
    private const int MaximoDiscos = 64;
    private const uint GeometriaExtendida = 0x700A0;
    private const uint DisposicionExtendida = 0x70050;
    private const int InicioEntradas = 48;
    private const int TamanoEntrada = 144;

    private sealed record Montaje(int Disco, long Inicio, string Letra);

    public static IReadOnlyList<DiscoFisico> Leer()
    {
        var montajes = Montajes();
        var volumenes = VolumenesWindows.Leer();
        var deWindows = MontajeDe(Path.GetPathRoot(Environment.SystemDirectory)!)?.Disco;
        var discos = new List<DiscoFisico>();
        for (var numero = 0; numero < MaximoDiscos; numero++)
        {
            using var disco = Abrir(numero);
            if (!disco.IsInvalid)
            {
                discos.Add(Describir(disco, numero, numero == deWindows,
                    montajes.Where(montaje => montaje.Disco == numero).ToList(), volumenes.Where(volumen => volumen.Disco == numero).ToList()));
            }
        }

        return discos;
    }

    internal static SafeFileHandle Abrir(int numero) => AbrirSinAcceso($@"\\.\PhysicalDrive{numero}");

    private static SafeFileHandle AbrirSinAcceso(string ruta) => DetectorDiscos.CreateFile(
        ruta, 0, DetectorDiscos.CompartirLecturaEscritura, IntPtr.Zero, DetectorDiscos.AbrirExistente, 0, IntPtr.Zero);

    private static DiscoFisico Describir(
        SafeFileHandle disco, int numero, bool esDeWindows, IReadOnlyList<Montaje> montajes, IReadOnlyList<VolumenWindows> volumenes)
    {
        var (bus, _) = DetectorDiscos.LeerDispositivo(disco);
        var descriptor = DetectorDiscos.Consultar(disco, DetectorDiscos.PropiedadDispositivo) ?? [];
        var (tamano, sector) = LeerGeometria(disco);
        var (estilo, particiones) = LeerParticiones(disco, montajes, volumenes);

        return new DiscoFisico(
            numero,
            Modelo(descriptor),
            Texto(descriptor, 24).TrimEnd('.'),
            bus,
            tamano,
            sector,
            descriptor.Length > 10 && descriptor[10] != 0,
            esDeWindows,
            estilo,
            particiones);
    }

    /// <summary>STORAGE_DEVICE_DESCRIPTOR: fabricante en el desplazamiento 12, producto en el 16.</summary>
    private static string Modelo(byte[] descriptor)
    {
        var fabricante = Texto(descriptor, 12);
        var producto = Texto(descriptor, 16);

        return producto.StartsWith(fabricante, StringComparison.OrdinalIgnoreCase) ? producto : $"{fabricante} {producto}".Trim();
    }

    private static string Texto(byte[] descriptor, int campo) =>
        descriptor.Length >= campo + 4 ? DetectorDiscos.LeerTexto(descriptor, BitConverter.ToInt32(descriptor, campo)) : string.Empty;

    /// <summary>DISK_GEOMETRY_EX: bytes por sector en el desplazamiento 20 y tamaño total en el 24. Sin medio: 0.</summary>
    private static (long Tamano, int Sector) LeerGeometria(SafeFileHandle disco)
    {
        var respuesta = new byte[256];

        return DetectorDiscos.DeviceIoControl(disco, GeometriaExtendida, null, 0, respuesta, respuesta.Length, out var leidos, IntPtr.Zero) && leidos >= 32
            ? (BitConverter.ToInt64(respuesta, 24), BitConverter.ToInt32(respuesta, 20))
            : (0, 512);
    }

    /// <summary>
    /// DRIVE_LAYOUT_INFORMATION_EX: estilo (0 MBR, 1 GPT, 2 nada) y cantidad; las entradas empiezan en el byte 48 y
    /// miden 144. En cada una: inicio (8), tamaño (16), número (24) y desde el 32 lo propio del estilo: en MBR el tipo
    /// (1 byte) y si es la activa (33); en GPT el tipo (GUID) y los atributos en el 64.
    /// </summary>
    private static (EstiloParticiones, IReadOnlyList<Particion>) LeerParticiones(
        SafeFileHandle disco, IReadOnlyList<Montaje> montajes, IReadOnlyList<VolumenWindows> volumenes)
    {
        var respuesta = new byte[InicioEntradas + TamanoEntrada * 128];
        if (!DetectorDiscos.DeviceIoControl(disco, DisposicionExtendida, null, 0, respuesta, respuesta.Length, out _, IntPtr.Zero))
        {
            return (EstiloParticiones.SinParticiones, []);
        }

        var estilo = BitConverter.ToInt32(respuesta, 0) switch
        {
            0 => EstiloParticiones.Mbr,
            1 => EstiloParticiones.Gpt,
            _ => EstiloParticiones.SinParticiones,
        };
        var particiones = new List<Particion>();
        for (var indice = 0; indice < BitConverter.ToInt32(respuesta, 4); indice++)
        {
            var entrada = InicioEntradas + indice * TamanoEntrada;
            var inicio = BitConverter.ToInt64(respuesta, entrada + 8);
            var tamano = BitConverter.ToInt64(respuesta, entrada + 16);
            var esGpt = estilo == EstiloParticiones.Gpt;
            var tipoGpt = esGpt ? new Guid(respuesta.AsSpan(entrada + 32, 16)) : Guid.Empty;
            var tipoMbr = esGpt ? (byte)0 : respuesta[entrada + 32];
            var tipo = esGpt ? TiposParticion.DeGpt(tipoGpt) : TiposParticion.DeMbr(tipoMbr);
            if (tamano > 0 && tipo is not null)
            {
                var letra = montajes.FirstOrDefault(montaje => montaje.Inicio == inicio)?.Letra;
                var volumen = volumenes.FirstOrDefault(candidato => candidato.Inicio == inicio);
                var contenido = volumen?.Contenido();
                particiones.Add(new Particion(
                    BitConverter.ToInt32(respuesta, entrada + 24), inicio, tamano, ConFormato(tipo, contenido?.SistemaArchivos), letra,
                    tipoGpt, tipoMbr, esGpt ? BitConverter.ToUInt64(respuesta, entrada + 64) : 0, !esGpt && respuesta[entrada + 33] != 0,
                    volumen?.Ruta, contenido?.SistemaArchivos, contenido?.Usado));
            }
        }

        return (estilo, particiones);
    }

    private static string ConFormato(string tipo, string? sistemaArchivos) =>
        sistemaArchivos is { Length: > 0 } && !tipo.Contains(sistemaArchivos, StringComparison.OrdinalIgnoreCase) ? $"{tipo} ({sistemaArchivos})" : tipo;

    private static List<Montaje> Montajes() =>
        Environment.GetLogicalDrives().Select(MontajeDe).OfType<Montaje>().ToList();

    /// <summary>En qué disco está una letra y en qué byte empieza (solo si ocupa un único disco).</summary>
    private static Montaje? MontajeDe(string letra)
    {
        var sinBarra = letra.TrimEnd('\\');
        using var volumen = AbrirSinAcceso($@"\\.\{sinBarra}");
        var extensiones = volumen.IsInvalid ? [] : VolumenesWindows.Extensiones(volumen).ToList();

        return extensiones.Count == 1 ? new Montaje(extensiones[0].Disco, extensiones[0].Inicio, sinBarra) : null;
    }
}
