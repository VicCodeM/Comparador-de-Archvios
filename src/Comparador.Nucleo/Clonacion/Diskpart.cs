using System.Text;

namespace Comparador.Nucleo.Clonacion;

/// <summary>
/// Las particiones del disco destino, creadas con diskpart (la herramienta de Windows): borra el disco, pone la tabla
/// (GPT o MBR) y crea cada partición del plan con su tipo, atributos y tamaño. Exige administrador.
/// </summary>
internal static class Diskpart
{
    private static readonly Guid Efi = new("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");
    private static readonly Guid Reservada = new("E3C9E316-0B5C-4DB8-817D-F92DF00215AE");

    /// <summary>
    /// "convert gpt" crea por su cuenta una partición Reservada (MSR) de 16 MB al principio: se borra todo lo que haya
    /// dejado antes de crear las del plan, para que el disco quede exactamente como el origen.
    /// </summary>
    public static void CrearParticiones(int disco, EstiloParticiones estilo, IReadOnlyList<ParticionPlaneada> plan)
    {
        Ejecutar(new StringBuilder()
            .AppendLine($"select disk {disco}")
            .AppendLine("clean")
            .AppendLine(estilo == EstiloParticiones.Gpt ? "convert gpt" : "convert mbr"));
        var sobrantes = ListadoDiscos.Leer().Single(leido => leido.Numero == disco).Particiones;
        var guion = new StringBuilder().AppendLine($"select disk {disco}");
        foreach (var sobrante in sobrantes.OrderByDescending(particion => particion.Numero))
        {
            guion.AppendLine($"select partition {sobrante.Numero}").AppendLine("delete partition override");
        }

        foreach (var planeada in plan)
        {
            Crear(guion, estilo == EstiloParticiones.Gpt, planeada.Origen, planeada.Tamano / PlanParticiones.Mega);
        }

        Ejecutar(guion);
    }

    private static void Crear(StringBuilder guion, bool gpt, Particion origen, long megas)
    {
        if (gpt && origen.TipoGpt == Efi)
        {
            guion.AppendLine($"create partition efi size={megas}");
        }
        else if (gpt && origen.TipoGpt == Reservada)
        {
            guion.AppendLine($"create partition msr size={megas}");
        }
        else
        {
            guion.AppendLine($"create partition primary size={megas}");
            if (gpt && origen.TipoGpt != Particion.TipoDatosGpt)
            {
                guion.AppendLine($"set id={origen.TipoGpt} override");
            }
            else if (!gpt)
            {
                guion.AppendLine($"set id={origen.TipoMbr:X2} override");
            }
        }

        if (gpt && origen.AtributosGpt != 0)
        {
            guion.AppendLine($"gpt attributes=0x{origen.AtributosGpt:X16}");
        }

        if (!gpt && origen.Activa)
        {
            guion.AppendLine("active");
        }
    }

    public static void Formatear(int disco, int particion, string etiqueta) =>
        EnParticion(disco, particion, $"format fs=ntfs quick label=\"{etiqueta.Replace("\"", string.Empty)}\"");

    /// <summary>Estira el NTFS (copiado sector a sector) hasta llenar su partición, más grande que la original.</summary>
    public static void Extender(int disco, int particion) => EnParticion(disco, particion, "extend filesystem");

    public static void AsignarLetra(int disco, int particion, char letra) => EnParticion(disco, particion, $"assign letter={letra}");

    public static void QuitarLetra(int disco, int particion, char letra) => EnParticion(disco, particion, $"remove letter={letra}");

    private static void EnParticion(int disco, int particion, string orden) =>
        Ejecutar(new StringBuilder().AppendLine($"select disk {disco}").AppendLine($"select partition {particion}").AppendLine(orden));

    private static void Ejecutar(StringBuilder guion)
    {
        var archivo = Path.Combine(Path.GetTempPath(), $"espejo-diskpart-{Guid.NewGuid():N}.txt");
        File.WriteAllText(archivo, guion.ToString(), Encoding.ASCII);
        try
        {
            HerramientaWindows.Ejecutar("diskpart.exe", $"/s \"{archivo}\"");
        }
        finally
        {
            File.Delete(archivo);
        }
    }
}
