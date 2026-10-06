using Comparador.Nucleo.Servicios;

namespace Comparador.Nucleo.Clonacion;

public enum ModoParticion
{
    /// <summary>Copia sector a sector, mismo tamaño (EFI, Reservada, Recuperación, Linux...).</summary>
    Exacta,

    /// <summary>Copia sector a sector y luego se estira el NTFS hasta llenar la partición, más grande.</summary>
    Agrandar,

    /// <summary>La partición es más pequeña que la original: se rehace y se copian sus archivos (DISM).</summary>
    Archivos,
}

/// <summary>Cómo queda una partición del origen en el disco destino.</summary>
public sealed record ParticionPlaneada(Particion Origen, long Tamano, ModoParticion Modo);

/// <summary>
/// Reparte el disco destino: las particiones fijas conservan su tamaño y las ajustables (NTFS de Windows o de datos)
/// se reparten todo el resto en proporción a su tamaño original, nunca por debajo de lo ocupado más un margen. Así un
/// SSD de 500 GB con 100 ocupados cabe en uno de 256, y uno de 256 clonado a 500 usa los 500.
/// </summary>
public static class PlanParticiones
{
    public const long Mega = 1024 * 1024;

    /// <summary>1 MB al principio (alineación) y 1 MB al final (la copia de la tabla GPT).</summary>
    private const long Reservado = 2 * Mega;

    private const long MargenMinimo = 64 * Mega;

    public static IReadOnlyList<ParticionPlaneada> Planear(IReadOnlyList<Particion> particiones, long tamanoDestino)
    {
        var fijas = particiones.Where(particion => !particion.Ajustable).Sum(particion => EnMegas(particion.Tamano));
        var ajustables = particiones.Where(particion => particion.Ajustable).ToList();
        var disponible = (tamanoDestino - Reservado) / Mega * Mega - fijas;
        var minimos = ajustables.ToDictionary(particion => particion, Minimo);
        var necesario = fijas + minimos.Values.Sum() + Reservado;
        if (disponible < minimos.Values.Sum())
        {
            throw new InvalidOperationException(
                $"No cabe: con lo que ocupan las particiones hace falta un disco de al menos {Formatos.Tamano(necesario)} y el destino mide {Formatos.Tamano(tamanoDestino)}.");
        }

        var tamanos = Repartir(ajustables, minimos, disponible);

        return particiones.Select(particion => particion.Ajustable
                ? new ParticionPlaneada(particion, tamanos[particion], tamanos[particion] >= particion.Tamano ? ModoParticion.Agrandar : ModoParticion.Archivos)
                : new ParticionPlaneada(particion, EnMegas(particion.Tamano), ModoParticion.Exacta))
            .ToList();
    }

    /// <summary>Lo ocupado más un 10 % (y al menos 64 MB): Windows necesita aire para arrancar y actualizarse.</summary>
    private static long Minimo(Particion particion) => EnMegas(particion.Usado!.Value + Math.Max(particion.Usado.Value / 10, MargenMinimo));

    /// <summary>
    /// En proporción al tamaño original; la que quedaría por debajo de su mínimo se queda con el mínimo y el resto se
    /// vuelve a repartir entre las demás. La última se lleva lo que sobre al redondear, para no dejar huecos.
    /// </summary>
    private static Dictionary<Particion, long> Repartir(List<Particion> ajustables, Dictionary<Particion, long> minimos, long disponible)
    {
        var tamanos = new Dictionary<Particion, long>();
        var pendientes = ajustables.ToList();
        while (pendientes.Count > 0)
        {
            var restante = disponible - tamanos.Values.Sum();
            var total = pendientes.Sum(particion => (double)particion.Tamano);
            var chica = pendientes.FirstOrDefault(particion => restante * (particion.Tamano / total) < minimos[particion]);
            if (chica is null)
            {
                foreach (var particion in pendientes)
                {
                    tamanos[particion] = (long)(restante * (particion.Tamano / total)) / Mega * Mega;
                }

                break;
            }

            tamanos[chica] = minimos[chica];
            pendientes.Remove(chica);
        }

        if (ajustables.Count > 0)
        {
            tamanos[ajustables[^1]] += disponible - tamanos.Values.Sum();
        }

        return tamanos;
    }

    private static long EnMegas(long bytes) => (bytes + Mega - 1) / Mega * Mega;
}
