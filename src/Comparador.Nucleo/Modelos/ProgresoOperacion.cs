using System.Diagnostics;

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
/// Progreso compartido entre el trabajo (que solo suma contadores, sin avisar a nadie) y la ventana (que lo lee
/// unas pocas veces por segundo). Antes se avisaba a la ventana por CADA archivo y con miles se congelaba.
/// </summary>
public sealed class ProgresoOperacion
{
    private readonly Stopwatch reloj = new();
    private long total;
    private long procesados;
    private long bytesTotal;
    private long bytesProcesados;
    private string fase = "Preparando";
    private string actual = string.Empty;

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

    public void SumarTotal(long elementos) => Interlocked.Add(ref total, elementos);

    public void Avanzar(long elementos = 1) => Interlocked.Add(ref procesados, elementos);

    public void SumarBytes(long bytes) => Interlocked.Add(ref bytesProcesados, bytes);

    public void MarcarActual(string descripcion) => actual = descripcion;

    public InstantaneaProgreso Instantanea() => new(
        fase,
        Interlocked.Read(ref total),
        Interlocked.Read(ref procesados),
        Interlocked.Read(ref bytesTotal),
        Interlocked.Read(ref bytesProcesados),
        actual,
        reloj.Elapsed);
}
