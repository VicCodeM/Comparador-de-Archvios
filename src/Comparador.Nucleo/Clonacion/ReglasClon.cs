using Comparador.Nucleo.Servicios;

namespace Comparador.Nucleo.Clonacion;

/// <summary>
/// Lo que impide destruir algo por error antes de escribir un solo byte. Se revisa en la pantalla y otra vez en el
/// Espejo elevado, con los discos leídos de nuevo: entre una y otro pudieron cambiar de número al conectar una USB.
/// </summary>
public static class ReglasClon
{
    /// <param name="largoOrigen">Null si no se sabe antes de empezar (imagen .xz o .gz): se comprueba al escribir.</param>
    /// <param name="ajustar">Disco completo ajustando el tamaño: lo que cabe lo decide el plan según lo ocupado.</param>
    public static IReadOnlyList<string> Revisar(ExtremoClon origen, ExtremoClon destino, IReadOnlyList<DiscoFisico> discos, long? largoOrigen, bool ajustar = false)
    {
        var problemas = new List<string>();
        var tramoOrigen = origen.Tramo(discos);
        var tramoDestino = destino.Tramo(discos);
        if (origen.NumeroDisco is not null && tramoOrigen is null || destino.NumeroDisco is not null && tramoDestino is null)
        {
            return ["Uno de los discos ya no está conectado o cambió. Vuelve a elegirlo."];
        }

        if (tramoOrigen is { Disco.TieneMedio: false } || tramoDestino is { Disco.TieneMedio: false })
        {
            problemas.Add("El lector no tiene tarjeta (o el disco no tiene medio).");
        }

        if (tramoDestino is { Disco.EsDeWindows: true })
        {
            problemas.Add("El destino es el disco donde está Windows: se borraría el sistema. Elige otro.");
        }

        if (origen.NumeroDisco is { } discoOrigen && discoOrigen == destino.NumeroDisco)
        {
            problemas.Add("El origen y el destino están en el mismo disco.");
        }

        if (origen is ExtremoClon.DeImagen imagenOrigen && destino is ExtremoClon.DeImagen imagenDestino
            && string.Equals(Path.GetFullPath(imagenOrigen.Ruta), Path.GetFullPath(imagenDestino.Ruta), StringComparison.OrdinalIgnoreCase))
        {
            problemas.Add("La imagen de origen y la de destino son el mismo archivo.");
        }

        if (destino is ExtremoClon.DeImagen imagen && tramoOrigen is { } leido && ImagenDentroDe(imagen.Ruta, leido.Disco))
        {
            problemas.Add("La imagen se guardaría dentro del mismo disco que se está leyendo. Guárdala en otro disco.");
        }

        if (ajustar && (origen is not ExtremoClon.DeDisco || destino is not ExtremoClon.DeDisco))
        {
            problemas.Add("Ajustar el tamaño solo sirve de un disco entero a otro disco entero.");
        }

        var largo = tramoOrigen?.Largo ?? largoOrigen;
        if (!ajustar && largo is { } necesario && tramoDestino is { } espacio && necesario > espacio.Largo)
        {
            problemas.Add($"No cabe: el origen mide {Formatos.Tamano(necesario)} y el destino {Formatos.Tamano(espacio.Largo)}.");
        }

        return problemas;
    }

    private static bool ImagenDentroDe(string ruta, DiscoFisico disco) =>
        Path.GetPathRoot(Path.GetFullPath(ruta))?.TrimEnd('\\') is { } letra && disco.Letras.Contains(letra, StringComparer.OrdinalIgnoreCase);
}
