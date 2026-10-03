using System.Diagnostics;
using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;

namespace Comparador.Pruebas;

/// <summary>El caso que congelaba la versión anterior: muchos miles de archivos.</summary>
public sealed class RendimientoPruebas : IDisposable
{
    private const int Archivos = 20_000;
    private readonly CarpetasDePrueba carpetas = new();

    public void Dispose() => carpetas.Dispose();

    [Fact]
    public async Task Compara_miles_de_archivos_rapido_y_el_progreso_se_puede_leer_mientras_trabaja()
    {
        for (var i = 0; i < Archivos; i++)
        {
            var carpeta = Path.Combine(carpetas.Origen, $"c{i % 200}");
            Directory.CreateDirectory(carpeta);
            File.WriteAllText(Path.Combine(carpeta, $"a{i}.txt"), "x");
        }

        var progreso = new ProgresoOperacion();
        var lecturas = 0;
        var reloj = Stopwatch.StartNew();
        var trabajo = new ComparadorCarpetas().CompararAsync(
            [new ParRutas(carpetas.Origen, carpetas.Destino)], new OpcionesComparacion(), progreso, CancellationToken.None);
        while (!trabajo.IsCompleted)
        {
            _ = progreso.Instantanea();
            lecturas++;
            await Task.Delay(20);
        }

        var resultado = await trabajo;

        Assert.Equal(Archivos + 200, resultado.Contar(EstadoElemento.Falta));
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(30), $"Tardó {reloj.Elapsed}");
        Assert.True(lecturas > 1, "El progreso debe poder leerse mientras se compara");
    }
}
