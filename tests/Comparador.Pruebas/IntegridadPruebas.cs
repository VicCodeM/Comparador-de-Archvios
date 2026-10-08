using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;
using Comparador.Nucleo.Ubicaciones;

namespace Comparador.Pruebas;

/// <summary>
/// Un destino que estropea un byte de lo que escribe: simula un cable USB o una red que corrompen datos. Con
/// <paramref name="veces"/> solo falla las primeras escrituras, como un tropiezo pasajero.
/// </summary>
internal sealed class DestinoQueCorrompe(string raiz, int veces = int.MaxValue) : IUbicacion
{
    private readonly UbicacionDisco disco = new(raiz);

    public int Escrituras { get; private set; }

    public string Raiz => disco.Raiz;

    public TipoUbicacion Tipo => disco.Tipo;

    public bool FechasFiables => true;

    public bool Existe() => disco.Existe();

    public void CrearCarpeta(string relativa) => disco.CrearCarpeta(relativa);

    public bool ExisteArchivo(string relativa) => disco.ExisteArchivo(relativa);

    public IReadOnlyList<EntradaEscaneada> ListarCarpeta(string relativa) => disco.ListarCarpeta(relativa);

    public Stream AbrirLectura(string relativa) => disco.AbrirLectura(relativa);

    public MetadatosArchivo LeerMetadatos(string relativa) => disco.LeerMetadatos(relativa);

    public async Task EscribirAsync(string relativa, Stream contenido, CancellationToken cancelacion)
    {
        using var memoria = new MemoryStream();
        await contenido.CopyToAsync(memoria, cancelacion);
        var bytes = memoria.ToArray();
        if (++Escrituras <= veces)
        {
            bytes[bytes.Length / 2] ^= 0xFF;
        }

        await disco.EscribirAsync(relativa, new MemoryStream(bytes), cancelacion);
    }

    public void Reemplazar(string temporal, string definitiva, MetadatosArchivo metadatos) => disco.Reemplazar(temporal, definitiva, metadatos);

    public void BorrarArchivo(string relativa) => disco.BorrarArchivo(relativa);
}

public sealed class IntegridadPruebas : IDisposable
{
    private readonly CarpetasDePrueba carpetas = new();

    public void Dispose() => carpetas.Dispose();

    private Task Copiar(IUbicacion destino, string relativa, bool verificar)
    {
        var progreso = new ProgresoOperacion();
        var archivo = progreso.Empezar(relativa, 0, "Copiando");

        return CopiaSegura.CopiarAsync(new UbicacionDisco(carpetas.Origen), destino, relativa, relativa, () => verificar, archivo, progreso, CancellationToken.None);
    }

    [Fact]
    public async Task Si_la_copia_sale_corrupta_falla_y_el_destino_bueno_no_se_toca()
    {
        CarpetasDePrueba.Escribir(carpetas.Origen, "foto.jpg", new string('x', 10_000));
        var destino = CarpetasDePrueba.Escribir(carpetas.Destino, "foto.jpg", "la version anterior, intacta");

        await Assert.ThrowsAsync<CopiaNoIdenticaException>(() => Copiar(new DestinoQueCorrompe(carpetas.Destino), "foto.jpg", verificar: true));

        Assert.Equal("la version anterior, intacta", File.ReadAllText(destino));
        Assert.Empty(Directory.GetFiles(carpetas.Destino, "*" + CopiaSegura.ExtensionTemporal));
    }

    [Fact]
    public async Task Si_la_copia_sale_corrupta_lo_intenta_tres_veces_antes_de_rendirse()
    {
        CarpetasDePrueba.Escribir(carpetas.Origen, "foto.jpg", new string('x', 10_000));
        var destino = new DestinoQueCorrompe(carpetas.Destino);

        await Assert.ThrowsAsync<CopiaNoIdenticaException>(() => Copiar(destino, "foto.jpg", verificar: true));

        Assert.Equal(3, destino.Escrituras);
    }

    [Fact]
    public async Task Un_tropiezo_pasajero_se_arregla_reintentando_y_la_copia_queda_identica()
    {
        var origen = CarpetasDePrueba.Escribir(carpetas.Origen, "foto.jpg", new string('x', 10_000));

        await Copiar(new DestinoQueCorrompe(carpetas.Destino, veces: 2), "foto.jpg", verificar: true);

        Assert.Equal(File.ReadAllBytes(origen), File.ReadAllBytes(Path.Combine(carpetas.Destino, "foto.jpg")));
        Assert.Empty(Directory.GetFiles(carpetas.Destino, "*" + CopiaSegura.ExtensionTemporal));
    }

    [Fact]
    public async Task La_copia_correcta_es_identica_byte_a_byte()
    {
        var datos = new byte[3 * 1024 * 1024 + 17];
        Random.Shared.NextBytes(datos);
        File.WriteAllBytes(Path.Combine(carpetas.Origen, "datos.bin"), datos);

        await Copiar(new UbicacionDisco(carpetas.Destino), "datos.bin", verificar: true);

        Assert.Equal(datos, File.ReadAllBytes(Path.Combine(carpetas.Destino, "datos.bin")));
    }

    [Fact]
    public async Task Un_temporal_de_una_copia_interrumpida_no_aparece_como_archivo_del_usuario()
    {
        CarpetasDePrueba.Escribir(carpetas.Origen, "a.txt", "a");
        CarpetasDePrueba.Escribir(carpetas.Origen, "b.txt" + CopiaSegura.ExtensionTemporal, "resto de un apagón");

        var resultado = await new ComparadorCarpetas().CompararAsync(
            [new ParRutas(carpetas.Origen, carpetas.Destino)], new OpcionesComparacion(), new ProgresoOperacion(), CancellationToken.None);

        Assert.Equal(["a.txt"], resultado.Elementos.Select(e => e.RutaRelativa));
    }

    /// <summary>Antes solo se saltaba y quedaba oculto para siempre en el destino (Victor, 2026-10-08).</summary>
    [Fact]
    public async Task El_temporal_de_una_copia_interrumpida_se_borra_del_destino()
    {
        CarpetasDePrueba.Escribir(carpetas.Origen, "a.txt", "a");
        var resto = CarpetasDePrueba.Escribir(carpetas.Destino, "a.txt" + CopiaSegura.ExtensionTemporal, "resto de un cierre de golpe");

        await new ComparadorCarpetas().CompararAsync(
            [new ParRutas(carpetas.Origen, carpetas.Destino)], new OpcionesComparacion(), new ProgresoOperacion(), CancellationToken.None);

        Assert.False(File.Exists(resto));
    }

    /// <summary>Apagar la verificación a mitad de un archivo la corta ahí mismo y la copia queda bien puesta.</summary>
    [Fact]
    public async Task Apagar_la_verificacion_a_mitad_de_un_archivo_la_corta_y_deja_la_copia()
    {
        var datos = new byte[8 * 1024 * 1024];
        Random.Shared.NextBytes(datos);
        File.WriteAllBytes(Path.Combine(carpetas.Origen, "datos.bin"), datos);
        var consultas = 0;
        var progreso = new ProgresoOperacion();
        var archivo = progreso.Empezar("datos.bin", datos.Length, "Copiando");

        var verificada = await CopiaSegura.CopiarAsync(
            new UbicacionDisco(carpetas.Origen), new UbicacionDisco(carpetas.Destino), "datos.bin", "datos.bin",
            () => ++consultas <= 2, archivo, progreso, CancellationToken.None);

        Assert.False(verificada);
        Assert.Equal(datos, File.ReadAllBytes(Path.Combine(carpetas.Destino, "datos.bin")));
        Assert.Empty(Directory.GetFiles(carpetas.Destino, "*" + CopiaSegura.ExtensionTemporal));
    }

    [Fact]
    public async Task El_progreso_de_bytes_cuadra_al_terminar_la_copia()
    {
        CarpetasDePrueba.Escribir(carpetas.Origen, "uno.txt", new string('1', 5000));
        CarpetasDePrueba.Escribir(carpetas.Origen, "dos.txt", new string('2', 7000));
        var resultado = await new ComparadorCarpetas().CompararAsync(
            [new ParRutas(carpetas.Origen, carpetas.Destino)], new OpcionesComparacion(), new ProgresoOperacion(), CancellationToken.None);
        var progreso = new ProgresoOperacion();

        await new SincronizadorArchivos().SincronizarAsync(resultado.Elementos, new OpcionesCopia { HilosManuales = 2 }, progreso, CancellationToken.None);

        var foto = progreso.Instantanea();
        Assert.Equal(foto.BytesTotal, foto.BytesProcesados);
        Assert.Equal(2, progreso.TomarTerminados().Count(t => t.Resultado == ResultadoArchivo.Copiado));
        Assert.Empty(progreso.EnCurso());
    }
}

public sealed class ConcurrenciaPruebas
{
    private sealed class Ubicacion(TipoUbicacion tipo) : IUbicacion
    {
        public string Raiz => string.Empty;
        public TipoUbicacion Tipo => tipo;
        public bool FechasFiables => true;
        public bool Existe() => true;
        public void CrearCarpeta(string relativa) => throw new NotSupportedException();
        public bool ExisteArchivo(string relativa) => throw new NotSupportedException();
        public IReadOnlyList<EntradaEscaneada> ListarCarpeta(string relativa) => throw new NotSupportedException();
        public Stream AbrirLectura(string relativa) => throw new NotSupportedException();
        public MetadatosArchivo LeerMetadatos(string relativa) => throw new NotSupportedException();
        public Task EscribirAsync(string relativa, Stream contenido, CancellationToken cancelacion) => throw new NotSupportedException();
        public void Reemplazar(string temporal, string definitiva, MetadatosArchivo metadatos) => throw new NotSupportedException();
        public void BorrarArchivo(string relativa) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(TipoUbicacion.Disco, TipoUbicacion.Disco, null, 4, true)]
    [InlineData(TipoUbicacion.Disco, TipoUbicacion.Usb, null, 4, true)]
    [InlineData(TipoUbicacion.Red, TipoUbicacion.Disco, null, 4, true)]
    [InlineData(TipoUbicacion.Disco, TipoUbicacion.Disco, 8, 8, false)]
    [InlineData(TipoUbicacion.Disco, TipoUbicacion.Disco, 99, Concurrencia.Maximo, false)]
    [InlineData(TipoUbicacion.Telefono, TipoUbicacion.Disco, 8, 1, false)]
    [InlineData(TipoUbicacion.Disco, TipoUbicacion.Telefono, null, 1, false)]
    public void Decide_el_punto_de_partida_y_si_se_ajusta(TipoUbicacion origen, TipoUbicacion destino, int? manual, int esperado, bool seAjusta)
    {
        var decision = Concurrencia.Decidir(manual, [new Ubicacion(origen), new Ubicacion(destino)]);

        Assert.Equal((esperado, seAjusta), (decision.Hilos, decision.SeAjusta));
    }

    /// <summary>Un equipo imaginario que rinde más con 6 copias a la vez y empeora con más.</summary>
    private static long RendimientoSimulado(int hilos) => 100_000_000L * Math.Min(hilos, 6) - 30_000_000L * Math.Max(0, hilos - 6);

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(20)]
    public void El_ajustador_encuentra_el_punto_optimo_desde_cualquier_partida(int partida)
    {
        var limite = new LimiteDinamico(partida);
        var ajustador = new AjustadorHilos(limite, Concurrencia.Minimo, Concurrencia.Maximo);

        for (var tramo = 0; tramo < 120; tramo++)
        {
            ajustador.Medir(RendimientoSimulado(limite.Actual), 0, TimeSpan.FromSeconds(1));
        }

        Assert.InRange(limite.Actual, 5, 7);
    }

    [Fact]
    public void Con_el_ruido_de_una_red_no_se_hunde_a_un_hilo()
    {
        var azar = new Random(1234);
        var limite = new LimiteDinamico(4);
        var ajustador = new AjustadorHilos(limite, Concurrencia.Minimo, Concurrencia.Maximo);
        var vistos = new List<int>();

        for (var tramo = 0; tramo < 400; tramo++)
        {
            var ruido = 1 + (azar.NextDouble() - 0.5) * 0.16;
            ajustador.Medir((long)(RendimientoSimulado(limite.Actual) * ruido), 0, TimeSpan.FromSeconds(1));
            vistos.Add(limite.Actual);
        }

        // Tras encontrarlo, pasa casi todo el tiempo cerca del óptimo (6).
        Assert.True(vistos.Skip(100).Average() is > 4.5 and < 7.5, $"Promedio {vistos.Skip(100).Average():F1}");
        Assert.True(vistos.Skip(100).Min() >= 3, $"Bajó hasta {vistos.Skip(100).Min()}");
    }

    [Fact]
    public async Task Bajar_el_limite_hace_esperar_a_las_siguientes_sin_cortar_las_que_corren()
    {
        var limite = new LimiteDinamico(2);
        await limite.EsperarAsync(CancellationToken.None);
        await limite.EsperarAsync(CancellationToken.None);
        limite.Cambiar(1);
        limite.Liberar();
        limite.Liberar();

        await limite.EsperarAsync(CancellationToken.None);
        using var espera = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => limite.EsperarAsync(espera.Token));
    }

    [Theory]
    [InlineData(@"mtp:\\Pixel 8\Almacenamiento interno\DCIM", "Pixel 8", @"\Almacenamiento interno\DCIM")]
    [InlineData(@"mtp:\\Pixel 8", "Pixel 8", @"\")]
    [InlineData(@"mtp:\\Pixel 8\", "Pixel 8", @"\")]
    public void Parte_una_ruta_de_telefono(string ruta, string dispositivo, string carpeta)
    {
        Assert.Equal((dispositivo, carpeta), UbicacionTelefono.Partir(ruta));
        Assert.Equal(TipoUbicacion.Telefono, CatalogoUbicaciones.TipoDe(ruta));
    }

    [Fact]
    public void Una_ruta_de_red_se_reconoce_sin_tocar_la_red() =>
        Assert.Equal(TipoUbicacion.Red, CatalogoUbicaciones.TipoDe(@"\\servidor-que-no-existe\compartida"));
}
