using System.IO.Compression;
using Comparador.Nucleo.Clonacion;
using Comparador.Nucleo.Servicios;
using Comparador.Nucleo.Ubicaciones;

namespace Comparador.Pruebas;

/// <summary>Un destino en memoria; si se pide, estropea un byte al leerlo de vuelta (simula una tarjeta SD falsa o dañada).</summary>
internal sealed class DestinoEnMemoria(long? capacidad = null, bool corromperAlLeer = false) : IDestinoClon
{
    private readonly MemoryStream memoria = new();

    public bool Terminado { get; private set; }

    public long? Capacidad => capacidad;

    public byte[] Contenido => memoria.ToArray();

    public void Escribir(ReadOnlySpan<byte> datos, long posicion)
    {
        memoria.Position = posicion;
        memoria.Write(datos);
    }

    public int Leer(Span<byte> destino, long posicion)
    {
        memoria.Position = posicion;
        var leidos = memoria.Read(destino);
        if (corromperAlLeer && leidos > 0)
        {
            destino[leidos / 2] ^= 0xFF;
        }

        return leidos;
    }

    public void Terminar() => Terminado = true;

    public void Dispose() => memoria.Dispose();
}

public sealed class ClonacionPruebas : IDisposable
{
    /// <summary>3 MB y 7 bytes: más de un bloque del motor y sin llenar el último sector, como muchas imágenes reales.</summary>
    private const int LargoPatron = 3 * 1024 * 1024 + 7;

    private readonly CarpetasDePrueba carpetas = new();

    private static byte[] Patron() => Enumerable.Range(0, LargoPatron).Select(indice => (byte)(indice % 251)).ToArray();

    private string Imagen(string nombre) => Path.Combine(carpetas.Origen, nombre);

    [Fact]
    public async Task Clona_una_imagen_a_otra_identica_y_sin_dejar_el_parcial()
    {
        var origen = Imagen("sd.img");
        File.WriteAllBytes(origen, Patron());
        var destino = Path.Combine(carpetas.Destino, "copia.img");

        var resultado = await MotorClon.ClonarAsync(new ExtremoClon.DeImagen(origen), new ExtremoClon.DeImagen(destino), verificar: true, null, CancellationToken.None);

        Assert.Equal(Patron(), File.ReadAllBytes(destino));
        Assert.Equal(LargoPatron, resultado.Bytes);
        Assert.True(resultado.Verificado);
        Assert.False(File.Exists(destino + ".parcial"));
    }

    [Theory]
    [InlineData(".img.gz")]
    [InlineData(".zip")]
    [InlineData(".img.xz")]
    public void Lee_las_imagenes_comprimidas_como_vienen_las_de_Raspberry_Pi(string extension)
    {
        var ruta = Comprimir(extension);
        using var origen = OrigenClon.DeImagen(ruta);
        using var destino = new DestinoEnMemoria();

        MotorClon.Copiar(origen, destino, verificar: true, null, CancellationToken.None);

        Assert.Equal(Patron(), destino.Contenido);
        Assert.True(destino.Terminado);
    }

    [Fact]
    public void Si_lo_escrito_no_es_identico_falla_y_no_lo_da_por_terminado()
    {
        File.WriteAllBytes(Imagen("sd.img"), Patron());
        using var origen = OrigenClon.DeImagen(Imagen("sd.img"));
        using var destino = new DestinoEnMemoria(corromperAlLeer: true);

        Assert.Throws<CopiaNoIdenticaException>(() => MotorClon.Copiar(origen, destino, verificar: true, null, CancellationToken.None));
        Assert.False(destino.Terminado);
    }

    [Fact]
    public void Si_no_cabe_se_detiene_aunque_el_tamano_no_se_supiera_antes()
    {
        using var origen = OrigenClon.DeImagen(Comprimir(".img.xz"));
        using var destino = new DestinoEnMemoria(capacidad: 1024 * 1024);

        Assert.Throws<InvalidOperationException>(() => MotorClon.Copiar(origen, destino, verificar: true, null, CancellationToken.None));
        Assert.False(destino.Terminado);
    }

    [Fact]
    public async Task Al_cancelar_no_queda_una_imagen_a_medias()
    {
        var origen = Imagen("sd.img");
        File.WriteAllBytes(origen, Patron());
        var destino = Path.Combine(carpetas.Destino, "copia.img");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MotorClon.ClonarAsync(
            new ExtremoClon.DeImagen(origen), new ExtremoClon.DeImagen(destino), verificar: true, null, new CancellationToken(canceled: true)));

        Assert.False(File.Exists(destino));
        Assert.False(File.Exists(destino + ".parcial"));
    }

    [Theory]
    [InlineData("disco:3")]
    [InlineData("particion:2:1")]
    [InlineData(@"D:\imagenes\raspberry.img.xz")]
    public void El_extremo_se_escribe_y_se_lee_igual(string texto) =>
        Assert.Equal(texto, ExtremoClon.Interpretar(texto).Texto);

    private static DiscoFisico Disco(int numero, long tamano, bool deWindows = false, string? letra = null) => new(
        numero, $"Disco de prueba {numero}", "SERIE", BusDisco.Usb, tamano, 512, Extraible: true, deWindows, EstiloParticiones.Mbr,
        [new Particion(1, 1024 * 1024, tamano - 1024 * 1024, "FAT32", letra)]);

    private static readonly IReadOnlyList<DiscoFisico> Discos =
    [
        Disco(0, 500_000_000_000, deWindows: true, letra: "C:"),
        Disco(1, 32_000_000_000, letra: "E:"),
        Disco(2, 64_000_000_000, letra: "F:"),
        Disco(3, 0),
    ];

    [Fact]
    public void Nunca_deja_escribir_sobre_el_disco_de_Windows() =>
        Assert.Contains(ReglasClon.Revisar(new ExtremoClon.DeDisco(1), new ExtremoClon.DeDisco(0), Discos, null), problema => problema.Contains("Windows"));

    [Fact]
    public void No_deja_clonar_un_disco_en_si_mismo() =>
        Assert.NotEmpty(ReglasClon.Revisar(new ExtremoClon.DeDisco(1), new ExtremoClon.DeParticion(1, 1), Discos, null));

    [Fact]
    public void No_deja_clonar_a_un_disco_mas_pequeno() =>
        Assert.Contains(ReglasClon.Revisar(new ExtremoClon.DeDisco(2), new ExtremoClon.DeDisco(1), Discos, null), problema => problema.StartsWith("No cabe"));

    [Fact]
    public void No_deja_guardar_la_imagen_dentro_del_disco_que_se_lee() =>
        Assert.NotEmpty(ReglasClon.Revisar(new ExtremoClon.DeDisco(1), new ExtremoClon.DeImagen(@"E:\copia.img"), Discos, null));

    [Fact]
    public void No_deja_usar_un_lector_sin_tarjeta() =>
        Assert.NotEmpty(ReglasClon.Revisar(new ExtremoClon.DeImagen(@"D:\raspberry.img"), new ExtremoClon.DeDisco(3), Discos, 100));

    [Fact]
    public void Si_el_disco_cambio_de_numero_pide_elegirlo_otra_vez() =>
        Assert.NotEmpty(ReglasClon.Revisar(new ExtremoClon.DeDisco(9), new ExtremoClon.DeDisco(2), Discos, null));

    [Fact]
    public void Un_clonado_correcto_no_tiene_problemas()
    {
        Assert.Empty(ReglasClon.Revisar(new ExtremoClon.DeDisco(1), new ExtremoClon.DeDisco(2), Discos, null));
        Assert.Empty(ReglasClon.Revisar(new ExtremoClon.DeImagen(@"D:\raspberry.img.xz"), new ExtremoClon.DeDisco(1), Discos, null));
        Assert.Empty(ReglasClon.Revisar(new ExtremoClon.DeDisco(1), new ExtremoClon.DeImagen(@"F:\sd.img"), Discos, null));
    }

    private string Comprimir(string extension)
    {
        var ruta = Imagen("patron" + extension);
        switch (extension)
        {
            case ".img.xz":
                // Hecha con "xz -T2 --block-size=1MiB": varios bloques, como las imágenes oficiales de Raspberry Pi.
                File.Copy(Path.Combine(AppContext.BaseDirectory, "Recursos", "patron.img.xz"), ruta);
                break;
            case ".img.gz":
                using (var gz = new GZipStream(File.Create(ruta), CompressionLevel.Fastest))
                {
                    gz.Write(Patron());
                }

                break;
            default:
                using (var zip = ZipFile.Open(ruta, ZipArchiveMode.Create))
                using (var entrada = zip.CreateEntry("raspios.img").Open())
                {
                    entrada.Write(Patron());
                }

                break;
        }

        return ruta;
    }

    public void Dispose() => carpetas.Dispose();
}
