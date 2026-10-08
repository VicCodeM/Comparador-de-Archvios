using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;

namespace Comparador.Pruebas;

/// <summary>Qué hacer si ya existe, pausar y saltar: lo que convierte la copia en algo que se controla.</summary>
public sealed class ControlCopiaPruebas : IDisposable
{
    private static readonly DateTime Antes = new(2025, 1, 1, 10, 0, 0);
    private static readonly DateTime Despues = new(2026, 1, 1, 10, 0, 0);
    private readonly CarpetasDePrueba carpetas = new();

    public void Dispose() => carpetas.Dispose();

    /// <param name="mientras">Se ejecuta con la copia ya lanzada y en pausa desde el principio (si se pide), sin carreras de tiempo.</param>
    private async Task<(ResumenSincronizacion Resumen, ProgresoOperacion Progreso)> Copiar(OpcionesCopia opciones, Action<ProgresoOperacion>? mientras = null)
    {
        var resultado = await new ComparadorCarpetas().CompararAsync(
            [new ParRutas(carpetas.Origen, carpetas.Destino)], new OpcionesComparacion(), new ProgresoOperacion(), CancellationToken.None);
        var progreso = new ProgresoOperacion();
        if (mientras is not null)
        {
            progreso.Pausar();
        }

        var copia = new SincronizadorArchivos().SincronizarAsync(resultado.Elementos, opciones, progreso, CancellationToken.None);
        mientras?.Invoke(progreso);

        return (await copia, progreso);
    }

    private string Destino(string nombre) => Path.Combine(carpetas.Destino, nombre);

    [Theory]
    [InlineData(ReglaConflicto.Reemplazar, "nuevo")]
    [InlineData(ReglaConflicto.Saltar, "viejo")]
    public async Task Respeta_si_reemplazar_o_no_tocar_lo_que_ya_existe(ReglaConflicto regla, string esperado)
    {
        CarpetasDePrueba.Escribir(carpetas.Origen, "a.txt", "nuevo", Despues);
        CarpetasDePrueba.Escribir(carpetas.Destino, "a.txt", "viejo", Antes);

        await Copiar(new OpcionesCopia { SiYaExiste = regla });

        Assert.Equal(esperado, File.ReadAllText(Destino("a.txt")));
    }

    [Fact]
    public async Task Solo_si_es_mas_nuevo_no_pisa_un_destino_mas_reciente()
    {
        CarpetasDePrueba.Escribir(carpetas.Origen, "viejo-en-origen.txt", "origen", Antes);
        CarpetasDePrueba.Escribir(carpetas.Destino, "viejo-en-origen.txt", "destino reciente", Despues);
        CarpetasDePrueba.Escribir(carpetas.Origen, "nuevo-en-origen.txt", "origen reciente", Despues);
        CarpetasDePrueba.Escribir(carpetas.Destino, "nuevo-en-origen.txt", "destino", Antes);

        var (resumen, _) = await Copiar(new OpcionesCopia { SiYaExiste = ReglaConflicto.SoloSiEsMasNuevo });

        Assert.Equal("destino reciente", File.ReadAllText(Destino("viejo-en-origen.txt")));
        Assert.Equal("origen reciente", File.ReadAllText(Destino("nuevo-en-origen.txt")));
        Assert.Equal((1, 1), (resumen.Copiados, resumen.Saltados));
    }

    [Fact]
    public async Task Conservar_ambos_guarda_el_nuevo_con_otro_nombre()
    {
        CarpetasDePrueba.Escribir(carpetas.Origen, "foto.jpg", "nueva");
        CarpetasDePrueba.Escribir(carpetas.Destino, "foto.jpg", "la de antes");
        CarpetasDePrueba.Escribir(carpetas.Destino, "foto (2).jpg", "otra que ya estaba");

        await Copiar(new OpcionesCopia { SiYaExiste = ReglaConflicto.ConservarAmbos });

        Assert.Equal("la de antes", File.ReadAllText(Destino("foto.jpg")));
        Assert.Equal("otra que ya estaba", File.ReadAllText(Destino("foto (2).jpg")));
        Assert.Equal("nueva", File.ReadAllText(Destino("foto (3).jpg")));
    }

    /// <summary>Un archivo grande para que la copia dure lo suficiente como para pausarla o saltarla a la mitad.</summary>
    private void EscribirGrande(string nombre)
    {
        var datos = new byte[64 * 1024 * 1024];
        Random.Shared.NextBytes(datos);
        File.WriteAllBytes(Path.Combine(carpetas.Origen, nombre), datos);
    }

    private static ArchivoEnCurso EsperarQueEmpiece(ProgresoOperacion progreso, string nombre = "grande.bin")
    {
        for (var intento = 0; intento < 500; intento++)
        {
            if (progreso.EnCurso().FirstOrDefault(archivo => archivo.RutaRelativa == nombre) is { } archivo)
            {
                return archivo;
            }

            Thread.Sleep(10);
        }

        throw new TimeoutException("La copia no empezó");
    }

    [Fact]
    public async Task Saltar_un_archivo_lo_deja_como_estaba_y_sigue_con_los_demas()
    {
        EscribirGrande("grande.bin");
        CarpetasDePrueba.Escribir(carpetas.Origen, "chico.txt", "chico");
        var anterior = CarpetasDePrueba.Escribir(carpetas.Destino, "grande.bin", "version anterior");

        var (resumen, _) = await Copiar(new OpcionesCopia { HilosManuales = 2 }, progreso =>
        {
            var grande = EsperarQueEmpiece(progreso);
            grande.Saltar();
            progreso.Reanudar();
        });

        Assert.Equal("version anterior", File.ReadAllText(anterior));
        Assert.Equal("chico", File.ReadAllText(Destino("chico.txt")));
        Assert.Equal(1, resumen.Saltados);
        Assert.Empty(Directory.GetFiles(carpetas.Destino, "*" + CopiaSegura.ExtensionTemporal));
    }

    [Fact]
    public async Task En_pausa_no_avanza_y_al_reanudar_termina_bien()
    {
        EscribirGrande("grande.bin");
        long durantePausa = 0, trasEsperar = 0;

        var (resumen, _) = await Copiar(new OpcionesCopia { HilosManuales = 1 }, progreso =>
        {
            EsperarQueEmpiece(progreso);
            Thread.Sleep(300);
            durantePausa = progreso.Instantanea().BytesProcesados;
            Thread.Sleep(500);
            trasEsperar = progreso.Instantanea().BytesProcesados;
            progreso.Reanudar();
        });

        Assert.Equal(durantePausa, trasEsperar);
        Assert.Equal(1, resumen.Copiados);
        Assert.Equal(new FileInfo(Path.Combine(carpetas.Origen, "grande.bin")).Length, new FileInfo(Destino("grande.bin")).Length);
    }

    /// <summary>
    /// Victor apagaba "Verificar" con la copia en marcha y seguía verificando (2026-10-08): lo que aún no empezó debe
    /// tomar el valor nuevo, y la barra debe acabar justo en el 100 % aunque el total se calculó con el valor viejo.
    /// </summary>
    [Fact]
    public async Task Apagar_la_verificacion_en_marcha_vale_para_lo_que_falta_y_la_barra_cierra_en_100()
    {
        CarpetasDePrueba.Escribir(carpetas.Origen, "a.txt", "uno");
        CarpetasDePrueba.Escribir(carpetas.Origen, "b.txt", "dos");
        var opciones = new OpcionesCopia { Verificar = true, HilosManuales = 1 };

        var (_, progreso) = await Copiar(opciones, enPausa =>
        {
            EsperarQueEmpiece(enPausa, "a.txt");
            opciones.Verificar = false;
            enPausa.Reanudar();
        });

        Assert.All(progreso.TomarTerminados(), terminado => Assert.Equal("Copiado", terminado.Mensaje));
        var final = progreso.Instantanea();
        Assert.Equal(final.BytesTotal, final.BytesProcesados);
    }
}
