using System.Collections.Concurrent;
using MediaDevices;
using Comparador.Nucleo.Servicios;

namespace Comparador.Nucleo.Ubicaciones;

/// <summary>Algo conectado al equipo que se puede elegir como origen o destino.</summary>
public sealed record DispositivoDisponible(string Nombre, string Ruta, TipoUbicacion Tipo, string Detalle);

/// <summary>
/// Abre una ruta como ubicación (sea de disco, USB, red o teléfono) y lista lo que hay conectado. La conexión con cada
/// teléfono se abre una sola vez y se reutiliza: abrirla cuesta segundos y el teléfono puede pedir permiso cada vez.
/// </summary>
public static class CatalogoUbicaciones
{
    private static readonly ConcurrentDictionary<string, (MediaDevice Dispositivo, SemaphoreSlim Turno)> Telefonos =
        new(StringComparer.OrdinalIgnoreCase);

    public static IUbicacion Abrir(string ruta)
    {
        if (!UbicacionTelefono.EsRutaDeTelefono(ruta))
        {
            return new UbicacionDisco(ruta);
        }

        var (nombre, _) = UbicacionTelefono.Partir(ruta);
        var (dispositivo, turno) = Telefonos.GetOrAdd(nombre, Conectar);

        return new UbicacionTelefono(ruta, dispositivo, turno);
    }

    /// <summary>USB, unidades de red y teléfonos conectados. Puede tardar: llamarlo fuera de la ventana.</summary>
    public static IReadOnlyList<DispositivoDisponible> ListarConectados() => [.. ListarUnidades(), .. ListarTelefonos()];

    /// <summary>Las subcarpetas de una carpeta del teléfono, para elegir dónde copiar.</summary>
    public static IReadOnlyList<string> ListarSubcarpetas(string ruta) => Abrir(ruta)
        .ListarCarpeta(string.Empty)
        .Where(entrada => entrada.EsCarpeta)
        .Select(entrada => entrada.RutaRelativa)
        .Order(StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    public static void CerrarTelefonos()
    {
        foreach (var (dispositivo, _) in Telefonos.Values)
        {
            dispositivo.Disconnect();
            dispositivo.Dispose();
        }

        Telefonos.Clear();
    }

    private static IEnumerable<DispositivoDisponible> ListarUnidades() => DriveInfo.GetDrives()
        .Where(unidad => unidad.DriveType is DriveType.Removable or DriveType.Network or DriveType.Fixed && unidad.IsReady)
        .Select(Describir);

    private static DispositivoDisponible Describir(DriveInfo unidad)
    {
        var tipo = TipoDe(unidad.Name);
        var etiqueta = string.IsNullOrWhiteSpace(unidad.VolumeLabel) ? NombreTipo(tipo) : unidad.VolumeLabel;

        return new DispositivoDisponible(
            $"{etiqueta} ({unidad.Name.TrimEnd('\\')})",
            unidad.Name,
            tipo,
            $"{Formatos.Tamano(unidad.AvailableFreeSpace)} libres de {Formatos.Tamano(unidad.TotalSize)}");
    }

    private static IEnumerable<DispositivoDisponible> ListarTelefonos()
    {
        foreach (var dispositivo in DispositivosMtp())
        {
            var nombre = dispositivo.FriendlyName;
            if (string.IsNullOrWhiteSpace(nombre))
            {
                continue;
            }

            yield return new DispositivoDisponible(nombre, UbicacionTelefono.Componer(nombre, @"\"), TipoUbicacion.Telefono, dispositivo.Description ?? string.Empty);
        }
    }

    private static IEnumerable<MediaDevice> DispositivosMtp() => MediaDeviceManager.Instance?.GetDevices() ?? [];

    private static (MediaDevice, SemaphoreSlim) Conectar(string nombre)
    {
        var dispositivo = DispositivosMtp()
            .FirstOrDefault(candidato => string.Equals(candidato.FriendlyName, nombre, StringComparison.OrdinalIgnoreCase))
            ?? throw new DirectoryNotFoundException($"El teléfono \"{nombre}\" no está conectado. Conéctalo por USB y elige \"Transferir archivos\".");
        dispositivo.Connect(MediaDeviceAccess.Default, MediaDeviceShare.Default, enableCache: false);

        return (dispositivo, new SemaphoreSlim(1, 1));
    }

    /// <summary>
    /// El tipo deducido de la ruta sin leer la unidad: a una letra se le pregunta a Windows su tipo (instantáneo,
    /// aunque sea de red y esté caída), \\servidor es red y mtp:\\ es un teléfono.
    /// </summary>
    public static TipoUbicacion TipoDe(string ruta)
    {
        if (UbicacionTelefono.EsRutaDeTelefono(ruta))
        {
            return TipoUbicacion.Telefono;
        }

        if (ruta.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return TipoUbicacion.Red;
        }

        var tieneLetra = ruta.Length >= 2 && ruta[1] == ':' && char.IsAsciiLetter(ruta[0]);

        return !tieneLetra ? TipoUbicacion.Disco : new DriveInfo(ruta[..1]).DriveType switch
        {
            DriveType.Removable => TipoUbicacion.Usb,
            DriveType.Network => TipoUbicacion.Red,
            _ => TipoUbicacion.Disco,
        };
    }

    public static string NombreTipo(TipoUbicacion tipo) => tipo switch
    {
        TipoUbicacion.Usb => "Memoria USB",
        TipoUbicacion.Red => "Unidad de red",
        TipoUbicacion.Telefono => "Teléfono",
        _ => "Disco local",
    };
}
