using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;

namespace Comparador.Pruebas;

/// <summary>Copiar o mover lo elegido en el Explorador, sin pasar por la comparación.</summary>
public sealed class TrabajoCopiaPruebas : IDisposable
{
    private readonly CarpetasDePrueba carpetas = new();

    public void Dispose() => carpetas.Dispose();

    private Task<ResumenSincronizacion> Ejecutar(IReadOnlyList<string> rutas, bool mover = false) =>
        TrabajoCopia.EjecutarAsync(new PedidoCopia(rutas, carpetas.Destino, mover), new OpcionesCopia(), new ProgresoOperacion(), CancellationToken.None);

    [Fact]
    public async Task Copia_archivos_y_carpetas_sueltos_dentro_del_destino()
    {
        var archivo = CarpetasDePrueba.Escribir(carpetas.Origen, "suelto.txt", "suelto");
        CarpetasDePrueba.Escribir(carpetas.Origen, Path.Combine("Fotos", "Viaje", "playa.jpg"), "playa");

        var resumen = await Ejecutar([archivo, Path.Combine(carpetas.Origen, "Fotos")]);

        Assert.Empty(resumen.Fallidos);
        Assert.Equal("suelto", File.ReadAllText(Path.Combine(carpetas.Destino, "suelto.txt")));
        Assert.Equal("playa", File.ReadAllText(Path.Combine(carpetas.Destino, "Fotos", "Viaje", "playa.jpg")));
        Assert.True(File.Exists(archivo));
    }

    [Fact]
    public async Task Lo_que_ya_esta_igual_no_se_vuelve_a_copiar()
    {
        var archivo = CarpetasDePrueba.Escribir(carpetas.Origen, "igual.txt", "mismo");
        CarpetasDePrueba.Escribir(carpetas.Destino, "igual.txt", "mismo");

        var resumen = await Ejecutar([archivo]);

        Assert.Equal(0, resumen.Copiados);
    }

    [Fact]
    public async Task Mover_borra_los_originales_y_las_carpetas_que_quedan_vacias()
    {
        var archivo = CarpetasDePrueba.Escribir(carpetas.Origen, "a.txt", "a");
        var carpeta = Path.Combine(carpetas.Origen, "Carpeta");
        CarpetasDePrueba.Escribir(carpeta, Path.Combine("sub", "b.txt"), "b");

        await Ejecutar([archivo, carpeta], mover: true);

        Assert.False(File.Exists(archivo));
        Assert.False(Directory.Exists(carpeta));
        Assert.Equal("b", File.ReadAllText(Path.Combine(carpetas.Destino, "Carpeta", "sub", "b.txt")));
    }

    [Fact]
    public async Task Mover_no_borra_el_original_si_el_destino_tiene_igual_tamano_y_fecha_pero_otro_contenido()
    {
        var original = CarpetasDePrueba.Escribir(carpetas.Origen, "foto.jpg", "AAAA");
        CarpetasDePrueba.Escribir(carpetas.Destino, "foto.jpg", "BBBB");

        await Ejecutar([original], mover: true);

        Assert.Equal("AAAA", File.ReadAllText(original));
    }

    [Fact]
    public async Task Mover_deja_en_el_origen_lo_que_no_se_pudo_copiar()
    {
        var bloqueado = CarpetasDePrueba.Escribir(carpetas.Origen, "bloqueado.txt", "contenido nuevo");
        var destino = CarpetasDePrueba.Escribir(carpetas.Destino, "bloqueado.txt", "viejo");

        ResumenSincronizacion resumen;
        using (new FileStream(destino, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            resumen = await Ejecutar([bloqueado], mover: true);
        }

        Assert.Single(resumen.Fallidos);
        Assert.Equal("contenido nuevo", File.ReadAllText(bloqueado));
    }
}
