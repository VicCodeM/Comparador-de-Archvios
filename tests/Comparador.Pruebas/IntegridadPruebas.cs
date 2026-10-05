using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;
using Comparador.Nucleo.Ubicaciones;

namespace Comparador.Pruebas;

/// <summary>Un destino que estropea un byte de lo que escribe: simula un cable USB o una red que corrompen datos.</summary>
internal sealed class DestinoQueCorrompe(string raiz) : IUbicacion
{
    private readonly UbicacionDisco disco = new(raiz);

    public string Raiz => disco.Raiz;

    public TipoUbicacion Tipo => disco.Tipo;

    public bool FechasFiables => true;

    public bool Existe() => disco.Existe();

    public void CrearCarpeta(string relativa) => disco.CrearCarpeta(relativa);

    public IReadOnlyList<EntradaEscaneada> ListarCarpeta(string relativa) => disco.ListarCarpeta(relativa);

    public Stream AbrirLectura(string relativa) => disco.AbrirLectura(relativa);

    public MetadatosArchivo LeerMetadatos(string relativa) => disco.LeerMetadatos(relativa);

    public async Task EscribirAsync(string relativa, Stream contenido, CancellationToken cancelacion)
    {
        using var memoria = new MemoryStream();
        await contenido.CopyToAsync(memoria, cancelacion);
        var bytes = memoria.ToArray();
        bytes[bytes.Length / 2] ^= 0xFF;
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

        return CopiaSegura.CopiarAsync(new UbicacionDisco(carpetas.Origen), destino, relativa, verificar, archivo, progreso, CancellationToken.None);
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

    [Fact]
    public async Task El_progreso_de_bytes_cuadra_al_terminar_la_copia()
    {
        CarpetasDePrueba.Escribir(carpetas.Origen, "uno.txt", new string('1', 5000));
        CarpetasDePrueba.Escribir(carpetas.Origen, "dos.txt", new string('2', 7000));
        var resultado = await new ComparadorCarpetas().CompararAsync(
            [new ParRutas(carpetas.Origen, carpetas.Destino)], new OpcionesComparacion(), new ProgresoOperacion(), CancellationToken.None);
        var progreso = new ProgresoOperacion();

        await new SincronizadorArchivos().SincronizarAsync(resultado.Elementos, verificar: true, hilosManuales: 2, progreso, CancellationToken.None);

        var foto = progreso.Instantanea();
        Assert.Equal(foto.BytesTotal, foto.BytesProcesados);
        Assert.Equal(2, progreso.TomarTerminados().Count(t => t.Exito));
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
        public IReadOnlyList<EntradaEscaneada> ListarCarpeta(string relativa) => throw new NotSupportedException();
        public Stream AbrirLectura(string relativa) => throw new NotSupportedException();
        public MetadatosArchivo LeerMetadatos(string relativa) => throw new NotSupportedException();
        public Task EscribirAsync(string relativa, Stream contenido, CancellationToken cancelacion) => throw new NotSupportedException();
        public void Reemplazar(string temporal, string definitiva, MetadatosArchivo metadatos) => throw new NotSupportedException();
        public void BorrarArchivo(string relativa) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(TipoUbicacion.Disco, TipoUbicacion.Disco, null, 4)]
    [InlineData(TipoUbicacion.Disco, TipoUbicacion.Usb, null, 2)]
    [InlineData(TipoUbicacion.Red, TipoUbicacion.Disco, null, 4)]
    [InlineData(TipoUbicacion.Disco, TipoUbicacion.Disco, 8, 8)]
    [InlineData(TipoUbicacion.Disco, TipoUbicacion.Disco, 99, Concurrencia.Maximo)]
    [InlineData(TipoUbicacion.Telefono, TipoUbicacion.Disco, 8, 1)]
    [InlineData(TipoUbicacion.Disco, TipoUbicacion.Telefono, null, 1)]
    public void Decide_los_hilos_segun_los_dispositivos(TipoUbicacion origen, TipoUbicacion destino, int? manual, int esperado) =>
        Assert.Equal(esperado, Concurrencia.Decidir(manual, [new Ubicacion(origen), new Ubicacion(destino)]).Hilos);

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
