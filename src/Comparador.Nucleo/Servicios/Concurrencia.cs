using Comparador.Nucleo.Ubicaciones;

namespace Comparador.Nucleo.Servicios;

/// <summary>Cuántos archivos se copian a la vez, por qué, y si el número se ajusta solo midiendo la velocidad.</summary>
public sealed record DecisionHilos(int Hilos, string Motivo, bool SeAjusta = false);

/// <summary>
/// El punto de partida de los hilos. No es un valor por tipo de disco: si se ajusta, <see cref="AjustadorHilos"/> lo
/// corrige midiendo en este equipo. Solo dos casos son fijos: el teléfono (no atiende dos cosas a la vez) y lo que el
/// usuario eligió a mano en Configuración.
/// </summary>
public static class Concurrencia
{
    public const int Minimo = 1;
    public const int Maximo = 32;
    private const int PartidaSinCabezal = 4;

    /// <param name="manual">Lo que eligió el usuario en Configuración, o null para que se ajuste solo.</param>
    public static DecisionHilos Decidir(int? manual, IEnumerable<IUbicacion> ubicaciones)
    {
        var lista = ubicaciones.ToList();
        if (lista.Any(ubicacion => ubicacion.Tipo == TipoUbicacion.Telefono))
        {
            return new DecisionHilos(1, "un teléfono solo atiende una copia a la vez");
        }

        if (manual is { } elegido)
        {
            return new DecisionHilos(Math.Clamp(elegido, Minimo, Maximo), "elegido en Configuración");
        }

        // Con cabezal, leer varios archivos a la vez lo hace saltar: se empieza con uno y se sube si la medición lo pide.
        var conCabezal = lista.OfType<UbicacionDisco>().Any(disco => disco.Perfil.Medio == MedioDisco.Mecanico);

        return new DecisionHilos(conCabezal ? Minimo : PartidaSinCabezal, "automático: se ajusta midiendo la velocidad", SeAjusta: true);
    }
}
