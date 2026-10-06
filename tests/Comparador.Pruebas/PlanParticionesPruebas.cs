using Comparador.Nucleo.Clonacion;

namespace Comparador.Pruebas;

public sealed class PlanParticionesPruebas
{
    private const long Mega = PlanParticiones.Mega;
    private const long Giga = 1024 * Mega;
    private static readonly Guid Efi = new("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");
    private static readonly Guid Reservada = new("E3C9E316-0B5C-4DB8-817D-F92DF00215AE");
    private static readonly Guid Recuperacion = new("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC");

    /// <summary>Un SSD de Windows típico de 500 GB: EFI, Reservada, Windows (con lo ocupado que se pida) y Recuperación.</summary>
    private static List<Particion> SsdDeWindows(long usadoWindows) =>
    [
        new(1, Mega, 100 * Mega, "EFI", null, Efi, SistemaArchivos: "FAT32", Usado: 30 * Mega),
        new(2, 101 * Mega, 16 * Mega, "Reservada", null, Reservada),
        new(3, 117 * Mega, 499 * Giga, "Datos", "C:", Particion.TipoDatosGpt, SistemaArchivos: "NTFS", Usado: usadoWindows),
        new(4, 500 * Giga, 800 * Mega, "Recuperación", null, Recuperacion, SistemaArchivos: "NTFS", Usado: 500 * Mega),
    ];

    [Fact]
    public void Un_ssd_de_500_con_100_ocupados_cabe_en_uno_de_256_y_lo_usa_entero()
    {
        var plan = PlanParticiones.Planear(SsdDeWindows(100 * Giga), 256 * Giga);

        Assert.Equal([100 * Mega, 16 * Mega], plan.Take(2).Select(parte => parte.Tamano));
        Assert.Equal(800 * Mega, plan[3].Tamano);
        Assert.Equal(ModoParticion.Archivos, plan[2].Modo);
        Assert.Equal(256 * Giga - 2 * Mega, plan.Sum(parte => parte.Tamano));
    }

    [Fact]
    public void Clonado_a_un_disco_mas_grande_la_particion_de_Windows_crece_hasta_llenarlo()
    {
        var plan = PlanParticiones.Planear(SsdDeWindows(100 * Giga), 1000 * Giga);

        Assert.Equal(ModoParticion.Agrandar, plan[2].Modo);
        Assert.True(plan[2].Tamano > 990 * Giga);
        Assert.Equal(1000 * Giga - 2 * Mega, plan.Sum(parte => parte.Tamano));
    }

    [Fact]
    public void Las_particiones_de_arranque_y_recuperacion_se_copian_tal_cual() =>
        Assert.All(PlanParticiones.Planear(SsdDeWindows(100 * Giga), 256 * Giga).Where(parte => parte.Origen.Numero != 3),
            parte => Assert.Equal(ModoParticion.Exacta, parte.Modo));

    [Fact]
    public void Si_lo_ocupado_no_cabe_lo_dice_con_cuanto_hace_falta()
    {
        var error = Assert.Throws<InvalidOperationException>(() => PlanParticiones.Planear(SsdDeWindows(300 * Giga), 256 * Giga));

        Assert.StartsWith("No cabe", error.Message);
    }

    [Fact]
    public void Con_varias_particiones_de_datos_se_reparte_en_proporcion_sin_bajar_de_lo_ocupado()
    {
        List<Particion> particiones =
        [
            new(1, Mega, 400 * Giga, "Datos", "C:", Particion.TipoDatosGpt, SistemaArchivos: "NTFS", Usado: 50 * Giga),
            new(2, 400 * Giga, 100 * Giga, "Datos", "D:", Particion.TipoDatosGpt, SistemaArchivos: "NTFS", Usado: 90 * Giga),
        ];

        var plan = PlanParticiones.Planear(particiones, 250 * Giga);

        Assert.True(plan[1].Tamano >= 99 * Giga, "D: no puede bajar de lo ocupado más el margen");
        Assert.True(plan[0].Tamano >= 55 * Giga);
        Assert.Equal(250 * Giga - 2 * Mega, plan.Sum(parte => parte.Tamano));
    }

    [Fact]
    public void Al_repartir_no_deja_huecos_aunque_la_division_no_sea_exacta()
    {
        var particiones = Enumerable.Range(1, 3)
            .Select(numero => new Particion(numero, numero * 10 * Giga, 10 * Giga, "Datos", null, Particion.TipoDatosGpt, SistemaArchivos: "NTFS", Usado: Giga))
            .ToList();

        var plan = PlanParticiones.Planear(particiones, 100 * Giga + 6 * Mega);

        Assert.Equal(100 * Giga + 4 * Mega, plan.Sum(parte => parte.Tamano));
    }

    [Fact]
    public void Una_particion_de_Linux_no_se_encoge_aunque_tenga_espacio_libre()
    {
        List<Particion> raspberry =
        [
            new(1, 4 * Mega, 512 * Mega, "FAT32", "E:", TipoMbr: 0x0C, SistemaArchivos: "FAT32", Usado: 60 * Mega),
            new(2, 516 * Mega, 30 * Giga, "Linux", null, TipoMbr: 0x83),
        ];

        Assert.Throws<InvalidOperationException>(() => PlanParticiones.Planear(raspberry, 16 * Giga));
        Assert.All(PlanParticiones.Planear(raspberry, 32 * Giga), parte => Assert.Equal(ModoParticion.Exacta, parte.Modo));
    }
}
