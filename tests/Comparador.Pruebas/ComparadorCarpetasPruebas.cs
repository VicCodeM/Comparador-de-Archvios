using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;

namespace Comparador.Pruebas;

public sealed class ComparadorCarpetasPruebas : IDisposable
{
    private readonly CarpetasDePrueba carpetas = new();

    public void Dispose() => carpetas.Dispose();

    private async Task<Dictionary<string, ElementoComparado>> Comparar(OpcionesComparacion? opciones = null)
    {
        var resultado = await new ComparadorCarpetas().CompararAsync(
            [new ParRutas(carpetas.Origen, carpetas.Destino)], opciones ?? new OpcionesComparacion(), new ProgresoOperacion(), CancellationToken.None);

        return resultado.Elementos.ToDictionary(e => e.RutaRelativa);
    }

    [Fact]
    public async Task Clasifica_faltantes_diferentes_iguales_y_sobrantes()
    {
        CarpetasDePrueba.Escribir(carpetas.Origen, "igual.txt", "hola");
        CarpetasDePrueba.Escribir(carpetas.Destino, "igual.txt", "hola");
        CarpetasDePrueba.Escribir(carpetas.Origen, "nuevo.txt", "solo en origen");
        CarpetasDePrueba.Escribir(carpetas.Origen, "cambiado.txt", "version larga");
        CarpetasDePrueba.Escribir(carpetas.Destino, "cambiado.txt", "corta");
        CarpetasDePrueba.Escribir(carpetas.Destino, "viejo.txt", "solo en destino");
        CarpetasDePrueba.Escribir(carpetas.Origen, Path.Combine("sub", "dentro.txt"), "x");

        var elementos = await Comparar();

        Assert.Equal(EstadoElemento.Coincide, elementos["igual.txt"].Estado);
        Assert.Equal(EstadoElemento.Falta, elementos["nuevo.txt"].Estado);
        Assert.Equal(EstadoElemento.Diferente, elementos["cambiado.txt"].Estado);
        Assert.Contains("Tamaño distinto", elementos["cambiado.txt"].Motivo);
        Assert.Equal(EstadoElemento.Sobra, elementos["viejo.txt"].Estado);
        Assert.Equal(EstadoElemento.Falta, elementos["sub"].Estado);
        Assert.True(elementos["sub"].EsCarpeta);
        Assert.True(elementos["nuevo.txt"].Seleccionado);
        Assert.False(elementos["igual.txt"].Seleccionado);
    }

    [Fact]
    public async Task Sin_detectar_sobrantes_no_los_muestra()
    {
        CarpetasDePrueba.Escribir(carpetas.Destino, "viejo.txt", "solo en destino");

        var elementos = await Comparar(new OpcionesComparacion { DetectarSobrantes = false });

        Assert.Empty(elementos);
    }

    [Fact]
    public async Task Las_exclusiones_ignoran_carpetas_y_comodines()
    {
        CarpetasDePrueba.Escribir(carpetas.Origen, Path.Combine("node_modules", "a.js"), "x");
        CarpetasDePrueba.Escribir(carpetas.Origen, "borrador.tmp", "x");
        CarpetasDePrueba.Escribir(carpetas.Origen, "valido.txt", "x");

        var elementos = await Comparar(new OpcionesComparacion { Exclusiones = FiltroExclusiones.DesdeTexto("node_modules; *.tmp") });

        Assert.Equal(["valido.txt"], elementos.Keys);
    }

    [Fact]
    public async Task Modo_rapido_usa_la_fecha_con_tolerancia_de_dos_segundos()
    {
        var fecha = new DateTime(2026, 1, 15, 10, 0, 0);
        CarpetasDePrueba.Escribir(carpetas.Origen, "casi.txt", "abc", fecha);
        CarpetasDePrueba.Escribir(carpetas.Destino, "casi.txt", "abc", fecha.AddSeconds(1));
        CarpetasDePrueba.Escribir(carpetas.Origen, "otra.txt", "abc", fecha);
        CarpetasDePrueba.Escribir(carpetas.Destino, "otra.txt", "abc", fecha.AddHours(1));

        var elementos = await Comparar();

        Assert.Equal(EstadoElemento.Coincide, elementos["casi.txt"].Estado);
        Assert.Equal(EstadoElemento.Diferente, elementos["otra.txt"].Estado);
    }

    [Fact]
    public async Task Modo_exacto_compara_el_contenido_y_no_la_fecha()
    {
        var fecha = new DateTime(2026, 1, 15, 10, 0, 0);
        CarpetasDePrueba.Escribir(carpetas.Origen, "mismo.txt", "abc", fecha);
        CarpetasDePrueba.Escribir(carpetas.Destino, "mismo.txt", "abc", fecha.AddDays(3));
        CarpetasDePrueba.Escribir(carpetas.Origen, "distinto.txt", "abc", fecha);
        CarpetasDePrueba.Escribir(carpetas.Destino, "distinto.txt", "abX", fecha);

        var elementos = await Comparar(new OpcionesComparacion { Modo = ModoComparacion.Exacto });

        Assert.Equal(EstadoElemento.Coincide, elementos["mismo.txt"].Estado);
        Assert.Equal(EstadoElemento.Diferente, elementos["distinto.txt"].Estado);
        Assert.Contains("SHA-256", elementos["distinto.txt"].Motivo);
    }

    [Fact]
    public async Task Varios_pares_se_comparan_cada_uno_con_su_destino()
    {
        using var otras = new CarpetasDePrueba();
        CarpetasDePrueba.Escribir(carpetas.Origen, "a.txt", "1");
        CarpetasDePrueba.Escribir(otras.Origen, "b.txt", "2");
        var pares = GeneradorPares.Generar([carpetas.Origen, otras.Origen], [carpetas.Destino, otras.Destino], ModoEmparejado.UnoAUno);

        var resultado = await new ComparadorCarpetas().CompararAsync(pares, new OpcionesComparacion(), new ProgresoOperacion(), CancellationToken.None);

        Assert.Equal(2, resultado.Contar(EstadoElemento.Falta));
        Assert.Equal(otras.Destino, resultado.Elementos.Single(e => e.RutaRelativa == "b.txt").Par.Destino);
    }

    [Fact]
    public async Task Cancelar_detiene_la_comparacion()
    {
        CarpetasDePrueba.Escribir(carpetas.Origen, "a.txt", "1");
        using var cancelacion = new CancellationTokenSource();
        await cancelacion.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ComparadorCarpetas().CompararAsync(
            [new ParRutas(carpetas.Origen, carpetas.Destino)], new OpcionesComparacion(), new ProgresoOperacion(), cancelacion.Token));
    }
}
