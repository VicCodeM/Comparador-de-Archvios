using Comparador.Nucleo.Ubicaciones;

namespace Comparador.Nucleo.Servicios;

/// <summary>Cuántos archivos se copian a la vez y por qué, para decirlo en pantalla.</summary>
public sealed record DecisionHilos(int Hilos, string Motivo);

/// <summary>
/// Decide cuántas copias simultáneas usar. Más hilos no siempre es más rápido: en una memoria USB o un disco
/// mecánico, leer varios archivos a la vez hace saltar el cabezal y va más lento; en red y SSD sí ayuda.
/// </summary>
public static class Concurrencia
{
    public const int Minimo = 1;
    public const int Maximo = 16;
    private const int ParaUsb = 2;
    private const int ParaRedODisco = 4;

    /// <param name="manual">Lo que eligió el usuario en Configuración, o null para decidir según los dispositivos.</param>
    public static DecisionHilos Decidir(int? manual, IEnumerable<IUbicacion> ubicaciones)
    {
        var tipos = ubicaciones.Select(ubicacion => ubicacion.Tipo).ToHashSet();
        if (tipos.Contains(TipoUbicacion.Telefono))
        {
            return new DecisionHilos(1, "un teléfono solo atiende una copia a la vez");
        }

        if (manual is { } elegido)
        {
            return new DecisionHilos(Math.Clamp(elegido, Minimo, Maximo), "elegido en Configuración");
        }

        return tipos.Contains(TipoUbicacion.Usb)
            ? new DecisionHilos(ParaUsb, "automático para memoria USB")
            : new DecisionHilos(ParaRedODisco, tipos.Contains(TipoUbicacion.Red) ? "automático para red" : "automático para disco");
    }
}
