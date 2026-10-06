using System.Collections.Concurrent;
using System.Diagnostics;
using Comparador.Nucleo.Servicios;

namespace Comparador.Nucleo.Modelos;

/// <summary>Foto del progreso en un momento dado, para pintarla en la ventana.</summary>
public sealed record InstantaneaProgreso(
    string Fase,
    long Total,
    long Procesados,
    long BytesTotal,
    long BytesProcesados,
    string Actual,
    TimeSpan Transcurrido)
{
    public double Porcentaje => BytesTotal > 0 ? 100.0 * BytesProcesados / BytesTotal
        : Total > 0 ? 100.0 * Procesados / Total
        : 0;

    public bool TieneTotal => Total > 0 || BytesTotal > 0;
}

/// <summary>
/// Un archivo que se está copiando o leyendo ahora mismo. Los hilos solo suman bytes; la ventana lo lee. Tiene su
/// propia cancelación para poder saltarlo sin detener los demás.
/// </summary>
public sealed class ArchivoEnCurso(string rutaRelativa, long tamano, string rutaOrigen, string rutaDestino, CancellationToken operacion)
{
    private readonly CancellationTokenSource propia = CancellationTokenSource.CreateLinkedTokenSource(operacion);
    private long bytes;
    private long acumulado;

    /// <summary>Se cancela si se cancela toda la operación o si se salta este archivo.</summary>
    public CancellationToken Cancelacion => propia.Token;

    /// <summary>El usuario pidió saltarlo (no es lo mismo que cancelar toda la copia).</summary>
    public bool Saltado { get; private set; }

    public void Saltar()
    {
        Saltado = true;
        propia.Cancel();
    }

    public string RutaRelativa { get; } = rutaRelativa;

    public long Tamano { get; } = tamano;

    /// <summary>Rutas completas, para abrir o mostrar el archivo desde la lista de terminados.</summary>
    public string RutaOrigen { get; } = rutaOrigen;

    public string RutaDestino { get; } = rutaDestino;

    /// <summary>Para mostrarlos en el orden en que empezaron, sin que salten de lugar.</summary>
    public long Inicio { get; } = Stopwatch.GetTimestamp();

    /// <summary>"Copiando", "Verificando"... lo que está haciendo con el archivo.</summary>
    public string Etapa { get; set; } = "Copiando";

    public long Bytes => Interlocked.Read(ref bytes);

    public void Reiniciar(string etapa)
    {
        Etapa = etapa;
        Interlocked.Exchange(ref bytes, 0);
    }

    /// <summary>Todo lo que este archivo ha sumado al total, en todas sus etapas y reintentos.</summary>
    public long Acumulado => Interlocked.Read(ref acumulado);

    internal void Sumar(long cantidad)
    {
        Interlocked.Add(ref bytes, cantidad);
        Interlocked.Add(ref acumulado, cantidad);
    }
}

public enum ResultadoArchivo
{
    Copiado,
    Fallido,

    /// <summary>No se copió a propósito: lo saltó el usuario, o ya existía y la regla decía no tocarlo.</summary>
    Saltado,
}

/// <summary>Un archivo que ya terminó, con lo que pasó.</summary>
public sealed record ArchivoTerminado(
    string RutaRelativa, string RutaOrigen, string RutaDestino, long Tamano, ResultadoArchivo Resultado, string Mensaje, DateTime Hora);

/// <summary>
/// Progreso compartido entre el trabajo (que solo suma contadores, sin avisar a nadie) y la ventana (que lo lee
/// unas pocas veces por segundo). Antes se avisaba a la ventana por CADA archivo y con miles se congelaba.
/// </summary>
public sealed class ProgresoOperacion
{
    private readonly Stopwatch reloj = new();
    private readonly ConcurrentDictionary<ArchivoEnCurso, byte> enCurso = new();
    private readonly ConcurrentQueue<ArchivoTerminado> terminados = new();
    private readonly ManualResetEventSlim sinPausa = new(initialState: true);
    private long total;
    private long procesados;
    private long bytesTotal;
    private long bytesProcesados;
    private string fase = "Preparando";
    private string actual = string.Empty;

    /// <summary>Cuántos archivos se trabajan a la vez en esta operación, y por qué.</summary>
    public DecisionHilos Hilos { get; set; } = new(1, string.Empty);

    public bool Pausado => !sinPausa.IsSet;

    /// <summary>
    /// Los hilos se detienen en el siguiente bloque que copien o lean (en cualquier camino: disco, red o teléfono), sin
    /// soltar el archivo a medias. El reloj también se para, para que el tiempo transcurrido no cuente la pausa.
    /// </summary>
    public void Pausar()
    {
        sinPausa.Reset();
        reloj.Stop();
    }

    public void Reanudar()
    {
        reloj.Start();
        sinPausa.Set();
    }

    /// <summary>Antes de empezar cada archivo: si está en pausa, espera aquí.</summary>
    public async Task EsperarSiPausadoAsync(CancellationToken cancelacion)
    {
        while (Pausado)
        {
            await Task.Delay(EsperaEnPausa, cancelacion);
        }
    }

    private static readonly TimeSpan EsperaEnPausa = TimeSpan.FromMilliseconds(200);

    public void IniciarFase(string nombre, long totalElementos = 0, long totalBytes = 0)
    {
        fase = nombre;
        actual = string.Empty;
        Interlocked.Exchange(ref total, totalElementos);
        Interlocked.Exchange(ref procesados, 0);
        Interlocked.Exchange(ref bytesTotal, totalBytes);
        Interlocked.Exchange(ref bytesProcesados, 0);
        reloj.Restart();
    }

    public void Avanzar(long elementos = 1) => Interlocked.Add(ref procesados, elementos);

    public void SumarBytes(long bytes) => Interlocked.Add(ref bytesProcesados, bytes);

    public void MarcarActual(string descripcion) => actual = descripcion;

    public ArchivoEnCurso Empezar(
        string rutaRelativa, long tamano, string etapa, string rutaOrigen = "", string rutaDestino = "", CancellationToken cancelacion = default)
    {
        var archivo = new ArchivoEnCurso(rutaRelativa, tamano, rutaOrigen, rutaDestino, cancelacion) { Etapa = etapa };
        enCurso.TryAdd(archivo, 0);
        actual = rutaRelativa;

        return archivo;
    }

    /// <summary>
    /// Suma bytes al archivo y al total. Se llama después de cada bloque en todos los caminos de copia y lectura, así
    /// que es aquí donde la pausa detiene el trabajo y donde un archivo saltado deja de copiarse.
    /// </summary>
    public void SumarBytes(ArchivoEnCurso archivo, long bytes)
    {
        Contar(archivo, bytes);
        sinPausa.Wait(archivo.Cancelacion);
        archivo.Cancelacion.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Deja la aportación del archivo al total en lo que se esperaba de él. Un reintento o un fallo a la mitad no
    /// deben dejar la barra pasada del 100 % ni corta para siempre.
    /// </summary>
    public void AjustarBytes(ArchivoEnCurso archivo, long esperados) => Contar(archivo, esperados - archivo.Acumulado);

    private void Contar(ArchivoEnCurso archivo, long bytes)
    {
        archivo.Sumar(bytes);
        SumarBytes(bytes);
    }

    public void Terminar(ArchivoEnCurso archivo, ResultadoArchivo resultado, string mensaje)
    {
        enCurso.TryRemove(archivo, out _);
        terminados.Enqueue(new ArchivoTerminado(archivo.RutaRelativa, archivo.RutaOrigen, archivo.RutaDestino, archivo.Tamano, resultado, mensaje, DateTime.Now));
        Avanzar();
    }

    public IReadOnlyList<ArchivoEnCurso> EnCurso() => enCurso.Keys.OrderBy(archivo => archivo.Inicio).ToList();

    /// <summary>Saca los terminados desde la última vez (la ventana los va agregando a su lista).</summary>
    public IReadOnlyList<ArchivoTerminado> TomarTerminados()
    {
        var lote = new List<ArchivoTerminado>();
        while (terminados.TryDequeue(out var terminado))
        {
            lote.Add(terminado);
        }

        return lote;
    }

    public InstantaneaProgreso Instantanea() => new(
        fase,
        Interlocked.Read(ref total),
        Interlocked.Read(ref procesados),
        Interlocked.Read(ref bytesTotal),
        Interlocked.Read(ref bytesProcesados),
        actual,
        reloj.Elapsed);
}
