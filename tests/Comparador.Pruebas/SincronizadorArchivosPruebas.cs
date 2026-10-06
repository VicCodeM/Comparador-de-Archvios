using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;

namespace Comparador.Pruebas;

public sealed class SincronizadorArchivosPruebas : IDisposable
{
    private readonly CarpetasDePrueba carpetas = new();

    public void Dispose() => carpetas.Dispose();

    private async Task<(ResultadoComparacion, ResumenSincronizacion)> CompararYSincronizar(bool verificar)
    {
        var resultado = await new ComparadorCarpetas().CompararAsync(
            [new ParRutas(carpetas.Origen, carpetas.Destino)], new OpcionesComparacion(), new ProgresoOperacion(), CancellationToken.None);
        var resumen = await new SincronizadorArchivos().SincronizarAsync(resultado.Elementos, new OpcionesCopia { Verificar = verificar }, new ProgresoOperacion(), CancellationToken.None);

        return (resultado, resumen);
    }

    [Fact]
    public async Task Copia_lo_que_falta_y_reemplaza_lo_diferente_conservando_la_fecha()
    {
        var fecha = new DateTime(2025, 6, 1, 8, 30, 0);
        CarpetasDePrueba.Escribir(carpetas.Origen, Path.Combine("sub", "nuevo.txt"), "nuevo", fecha);
        CarpetasDePrueba.Escribir(carpetas.Origen, "cambiado.txt", "version nueva", fecha);
        CarpetasDePrueba.Escribir(carpetas.Destino, "cambiado.txt", "vieja");

        var (resultado, resumen) = await CompararYSincronizar(verificar: true);

        Assert.Equal("nuevo", File.ReadAllText(Path.Combine(carpetas.Destino, "sub", "nuevo.txt")));
        Assert.Equal("version nueva", File.ReadAllText(Path.Combine(carpetas.Destino, "cambiado.txt")));
        Assert.Equal(fecha, File.GetLastWriteTime(Path.Combine(carpetas.Destino, "cambiado.txt")));
        Assert.Empty(resumen.Fallidos);
        Assert.All(resultado.Elementos, e => Assert.Equal(EstadoElemento.Coincide, e.Estado));
        Assert.Empty(Directory.GetFiles(carpetas.Destino, "*" + CopiaSegura.ExtensionTemporal, SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Reemplaza_un_destino_de_solo_lectura_y_le_devuelve_sus_atributos()
    {
        CarpetasDePrueba.Escribir(carpetas.Origen, "protegido.txt", "version nueva");
        var destino = CarpetasDePrueba.Escribir(carpetas.Destino, "protegido.txt", "vieja");
        File.SetAttributes(destino, FileAttributes.ReadOnly);

        var (_, resumen) = await CompararYSincronizar(verificar: true);

        Assert.Empty(resumen.Fallidos);
        File.SetAttributes(destino, FileAttributes.Normal);
        Assert.Equal("version nueva", File.ReadAllText(destino));
    }

    [Fact]
    public async Task Lo_no_seleccionado_no_se_toca()
    {
        CarpetasDePrueba.Escribir(carpetas.Origen, "a.txt", "a");
        CarpetasDePrueba.Escribir(carpetas.Origen, "b.txt", "b");
        var resultado = await new ComparadorCarpetas().CompararAsync(
            [new ParRutas(carpetas.Origen, carpetas.Destino)], new OpcionesComparacion(), new ProgresoOperacion(), CancellationToken.None);
        resultado.Elementos.Single(e => e.RutaRelativa == "b.txt").Seleccionado = false;

        await new SincronizadorArchivos().SincronizarAsync(resultado.Elementos, new OpcionesCopia { Verificar = false }, new ProgresoOperacion(), CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(carpetas.Destino, "a.txt")));
        Assert.False(File.Exists(Path.Combine(carpetas.Destino, "b.txt")));
    }

    [Fact]
    public async Task Un_archivo_abierto_falla_diciendo_que_esta_en_uso_y_el_destino_queda_intacto()
    {
        CarpetasDePrueba.Escribir(carpetas.Origen, "bloqueado.txt", "contenido nuevo");
        var destino = CarpetasDePrueba.Escribir(carpetas.Destino, "bloqueado.txt", "viejo");

        ResumenSincronizacion resumen;
        using (new FileStream(destino, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            (_, resumen) = await CompararYSincronizar(verificar: false);
        }

        var fallido = Assert.Single(resumen.Fallidos);
        Assert.Contains("abierto", fallido.Motivo);
        Assert.Equal(EstadoElemento.Diferente, fallido.Estado);
        Assert.Equal("viejo", File.ReadAllText(destino));
    }

    [Fact]
    public async Task Copia_rutas_de_mas_de_260_caracteres()
    {
        var profunda = string.Join('\\', Enumerable.Repeat("carpeta-con-nombre-largo", 12));
        CarpetasDePrueba.Escribir(Rutas.ParaIO(carpetas.Origen), Path.Combine(profunda, "archivo.txt"), "lejos");

        var (_, resumen) = await CompararYSincronizar(verificar: true);

        var copiado = Rutas.ParaIO(Path.Combine(carpetas.Destino, profunda, "archivo.txt"));
        Assert.True(copiado.Length > 260);
        Assert.Empty(resumen.Fallidos);
        Assert.Equal("lejos", File.ReadAllText(copiado));
    }
}
