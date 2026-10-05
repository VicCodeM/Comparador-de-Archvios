using System.Diagnostics;
using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;
using Comparador.Nucleo.Ubicaciones;

// Mide la velocidad de copia real en los discos de este equipo: el motor de Windows (CopyFile, el que usa el
// Explorador) contra el motor del Comparador.
// Uso: Comparador.Medicion <carpeta origen> <carpeta destino vacía> [--rapida]
//      --rapida: una sola pasada de cada uno (por red, cada pasada puede tardar minutos).
if (args.Length < 2)
{
    Console.WriteLine("Uso: Comparador.Medicion <carpeta origen> <carpeta destino>");
    return 1;
}

var (origen, destino) = (args[0], args[1]);
var rapida = args.Contains("--rapida");
var repeticiones = rapida ? 1 : 3;
if (Directory.Exists(destino) && Directory.EnumerateFileSystemEntries(destino).Any())
{
    Console.WriteLine("El destino debe estar vacío o no existir: entre pruebas se vacía y no debe borrar nada tuyo.");
    return 1;
}

Console.WriteLine($"Origen : {origen}\n         {DetectorDiscos.Detectar(origen).Descripcion} (disco físico {DetectorDiscos.Detectar(origen).NumeroDisco?.ToString() ?? "?"})");
Console.WriteLine($"Destino: {destino}\n         {DetectorDiscos.Detectar(destino).Descripcion} (disco físico {DetectorDiscos.Detectar(destino).NumeroDisco?.ToString() ?? "?"})");

var archivos = Directory.EnumerateFiles(origen, "*", SearchOption.AllDirectories).Select(ruta => new FileInfo(ruta)).ToList();
var bytes = archivos.Sum(archivo => archivo.Length);
Console.WriteLine($"Datos  : {archivos.Count:N0} archivos, {Formatos.Tamano(bytes)}\n");
Console.WriteLine("Aviso: la primera lectura del origen puede quedar en la memoria de Windows y acelerar las siguientes.");
Console.WriteLine("Por eso cada prueba se repite y se toma la mejor; para discos lentos de escritura (USB, mecánicos) pesa poco.\n");

await Medir("Windows CopyFile (Explorador)", () =>
{
    foreach (var archivo in archivos)
    {
        var copia = Path.Combine(destino, Path.GetRelativePath(origen, archivo.FullName));
        Directory.CreateDirectory(Path.GetDirectoryName(copia)!);
        File.Copy(archivo.FullName, copia, overwrite: true);
    }

    return Task.CompletedTask;
});

await Medir("Comparador, sin verificar", () => CopiarConMotor(verificar: false));
await Medir("Comparador, verificando SHA-256", () => CopiarConMotor(verificar: true));

return 0;

async Task CopiarConMotor(bool verificar)
{
    var resultado = await new ComparadorCarpetas().CompararAsync(
        [new ParRutas(origen, destino)], new OpcionesComparacion(), new ProgresoOperacion(), CancellationToken.None);
    var progreso = new ProgresoOperacion();
    using var fin = new CancellationTokenSource();
    var vigilancia = VigilarHilosAsync(progreso, fin.Token);
    var resumen = await new SincronizadorArchivos().SincronizarAsync(resultado.Elementos, verificar, null, progreso, CancellationToken.None);
    await fin.CancelAsync();
    await vigilancia;
    foreach (var fallido in resumen.Fallidos.Take(10))
    {
        Console.WriteLine($"   FALLÓ {fallido.RutaRelativa}: {fallido.Motivo}");
    }
}

// Muestra cómo el ajustador va moviendo los hilos durante la copia.
async Task VigilarHilosAsync(ProgresoOperacion progreso, CancellationToken fin)
{
    var vistos = new List<int>();
    try
    {
        using var reloj = new PeriodicTimer(TimeSpan.FromSeconds(1.5));
        while (await reloj.WaitForNextTickAsync(fin))
        {
            vistos.Add(progreso.Hilos.Hilos);
        }
    }
    catch (OperationCanceledException)
    {
    }

    Console.WriteLine($"   hilos en el tiempo: {string.Join(" ", vistos)}");
}

async Task Medir(string nombre, Func<Task> copiar)
{
    var mejor = TimeSpan.MaxValue;
    for (var i = 0; i < repeticiones; i++)
    {
        Vaciar();
        var reloj = Stopwatch.StartNew();
        await copiar();
        reloj.Stop();
        mejor = reloj.Elapsed < mejor ? reloj.Elapsed : mejor;
    }

    Vaciar();
    Console.WriteLine($"{nombre,-34} {mejor.TotalSeconds,7:F2} s   {bytes / 1048576.0 / mejor.TotalSeconds,8:F0} MB/s");
}

void Vaciar()
{
    if (Directory.Exists(destino))
    {
        Directory.Delete(destino, recursive: true);
    }

    Directory.CreateDirectory(destino);
}
