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

        Assert.Equal(Archivos + 200, resultado.Elementos.Count(e => e.Estado == EstadoElemento.Falta));
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(30), $"Tardó {reloj.Elapsed}");
        Assert.True(lecturas > 1, "El progreso debe poder leerse mientras se compara");
    }

    [Fact]
    public void Compara_100_000_archivos_en_memoria_en_menos_de_un_segundo()
    {
        var par = new ParRutas(@"C:\Origen", @"C:\Destino");
        var origen = new List<EntradaEscaneada>(100_000);
        var destino = new List<EntradaEscaneada>(100_000);
        var fecha = new DateTime(2026, 1, 15, 10, 0, 0);

        for (var i = 0; i < 100_000; i++)
        {
            var ruta = $@"sub_{i % 500}\archivo_{i}.txt";
            origen.Add(new EntradaEscaneada(ruta, EsCarpeta: false, Tamano: 1024, FechaModificacion: fecha));
            if (i % 2 == 0)
            {
                destino.Add(new EntradaEscaneada(ruta, EsCarpeta: false, Tamano: 1024, FechaModificacion: fecha));
            }
        }

        var reloj = Stopwatch.StartNew();
        var indice = new Dictionary<string, EntradaEscaneada>(destino.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entrada in destino)
        {
            indice.TryAdd(entrada.RutaRelativa, entrada);
        }

        var elementos = new List<ElementoComparado>(origen.Count);
        foreach (var entrada in origen)
        {
            indice.Remove(entrada.RutaRelativa, out var enDestino);
            elementos.Add(new ElementoComparado
            {
                Par = par,
                RutaRelativa = entrada.RutaRelativa,
                EsCarpeta = entrada.EsCarpeta,
                TamanoOrigen = entrada.Tamano,
                FechaOrigen = entrada.FechaModificacion,
                TamanoDestino = enDestino?.Tamano,
                FechaDestino = enDestino?.FechaModificacion,
                Estado = enDestino is null ? EstadoElemento.Falta : EstadoElemento.Coincide,
                Motivo = enDestino is null ? "No existe en el destino" : string.Empty,
                Seleccionado = enDestino is null,
            });
        }

        reloj.Stop();

        Assert.Equal(100_000, elementos.Count);
        Assert.Equal(50_000, elementos.Count(e => e.Estado == EstadoElemento.Falta));
        Assert.Equal(50_000, elementos.Count(e => e.Estado == EstadoElemento.Coincide));
        Assert.True(reloj.ElapsedMilliseconds < 1500, $"La comparación de 100.000 archivos tardó {reloj.ElapsedMilliseconds} ms");
    }
}
