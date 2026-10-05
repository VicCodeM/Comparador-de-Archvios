using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Comparador.App.ModelosVista;

/// <summary>Un archivo que se está copiando ahora, como se ve en la lista.</summary>
public sealed partial class FilaEnCurso(ArchivoEnCurso archivo) : ObservableObject
{
    [ObservableProperty] private string etapa = string.Empty;
    [ObservableProperty] private string avance = string.Empty;
    [ObservableProperty] private double porcentaje;

    public ArchivoEnCurso Archivo { get; } = archivo;

    public string Nombre { get; } = Path.GetFileName(archivo.RutaRelativa);

    public string Carpeta { get; } = Path.GetDirectoryName(archivo.RutaRelativa) ?? string.Empty;

    public void Actualizar()
    {
        Etapa = Archivo.Etapa;
        Porcentaje = Archivo.Tamano > 0 ? 100.0 * Archivo.Bytes / Archivo.Tamano : 0;
        Avance = Archivo.Tamano > 0 ? $"{Formatos.Tamano(Archivo.Bytes)} de {Formatos.Tamano(Archivo.Tamano)}" : string.Empty;
    }
}

/// <summary>Un archivo terminado, como se ve en la lista.</summary>
public sealed record FilaTerminada(string Nombre, string Carpeta, string Tamano, bool Exito, string Mensaje, string Hora)
{
    public static FilaTerminada Desde(ArchivoTerminado terminado) => new(
        Path.GetFileName(terminado.RutaRelativa),
        Path.GetDirectoryName(terminado.RutaRelativa) ?? string.Empty,
        terminado.Tamano > 0 ? Formatos.Tamano(terminado.Tamano) : string.Empty,
        terminado.Exito,
        terminado.Mensaje,
        terminado.Hora.ToString("HH:mm:ss"));
}

/// <summary>
/// Pinta el progreso leyéndolo 4 veces por segundo. El trabajo nunca le avisa a la ventana por cada archivo:
/// así la ventana responde igual con 10 archivos que con 10 millones. Los terminados se guardan solo los últimos
/// (los errores, todos): una lista que crece sin fin con cada copia era otra forma de congelar la ventana.
/// </summary>
public sealed partial class ProgresoModeloVista : ObservableObject
{
    private const int MaximoRecientes = 200;
    private readonly DispatcherTimer reloj = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private ProgresoOperacion? seguido;
    private long bytesAnteriores;
    private TimeSpan momentoAnterior;
    private double velocidadSuavizada;

    [ObservableProperty] private string fase = string.Empty;
    [ObservableProperty] private string detalle = string.Empty;
    [ObservableProperty] private string velocidad = string.Empty;
    [ObservableProperty] private string restante = string.Empty;
    [ObservableProperty] private string transcurrido = string.Empty;
    [ObservableProperty] private string hilos = string.Empty;
    [ObservableProperty] private double porcentaje;
    [ObservableProperty] private bool indeterminado = true;
    [ObservableProperty] private int correctos;
    [ObservableProperty] private int fallidos;

    public ProgresoModeloVista() => reloj.Tick += (_, _) => Actualizar();

    public ObservableCollection<FilaEnCurso> EnCurso { get; } = [];

    public ObservableCollection<FilaTerminada> Recientes { get; } = [];

    public ObservableCollection<FilaTerminada> Errores { get; } = [];

    public void Seguir(ProgresoOperacion progreso)
    {
        seguido = progreso;
        Fase = string.Empty;
        Hilos = string.Empty;
        Velocidad = string.Empty;
        Restante = string.Empty;
        Transcurrido = string.Empty;
        Correctos = 0;
        Fallidos = 0;
        bytesAnteriores = 0;
        momentoAnterior = TimeSpan.Zero;
        velocidadSuavizada = 0;
        EnCurso.Clear();
        Recientes.Clear();
        Errores.Clear();
        reloj.Start();
        Actualizar();
    }

    public void Detener()
    {
        Actualizar();
        reloj.Stop();
        EnCurso.Clear();
        Velocidad = string.Empty;
        Restante = string.Empty;
        seguido = null;
    }

    private void Actualizar()
    {
        if (seguido is null)
        {
            return;
        }

        var foto = seguido.Instantanea();
        if (foto.Fase != Fase)
        {
            bytesAnteriores = 0;
            momentoAnterior = TimeSpan.Zero;
            velocidadSuavizada = 0;
        }

        Fase = foto.Fase;
        Indeterminado = !foto.TieneTotal;
        Porcentaje = foto.Porcentaje;
        Transcurrido = "Lleva " + Formatos.Duracion(foto.Transcurrido);
        Detalle = DescribirConteo(foto);
        Hilos = DescribirHilos(seguido.Hilos);
        ActualizarVelocidad(foto);
        ActualizarEnCurso(seguido.EnCurso());
        AgregarTerminados(seguido.TomarTerminados());
    }

    private static string DescribirHilos(DecisionHilos decision) => decision.Motivo.Length == 0
        ? string.Empty
        : $"{decision.Hilos} {(decision.Hilos == 1 ? "hilo" : "hilos")} · {decision.Motivo}";

    /// <summary>Reutiliza las filas que siguen en curso: así no parpadean ni pierden su barra en cada repintado.</summary>
    private void ActualizarEnCurso(IReadOnlyList<ArchivoEnCurso> actuales)
    {
        var vigentes = actuales.ToHashSet();
        for (var i = EnCurso.Count - 1; i >= 0; i--)
        {
            if (!vigentes.Contains(EnCurso[i].Archivo))
            {
                EnCurso.RemoveAt(i);
            }
        }

        var mostrados = EnCurso.Select(fila => fila.Archivo).ToHashSet();
        foreach (var archivo in actuales.Where(archivo => !mostrados.Contains(archivo)))
        {
            EnCurso.Add(new FilaEnCurso(archivo));
        }

        foreach (var fila in EnCurso)
        {
            fila.Actualizar();
        }
    }

    private void AgregarTerminados(IReadOnlyList<ArchivoTerminado> lote)
    {
        Correctos += lote.Count(terminado => terminado.Exito);
        Fallidos += lote.Count(terminado => !terminado.Exito);
        foreach (var terminado in lote.Where(terminado => !terminado.Exito))
        {
            Errores.Add(FilaTerminada.Desde(terminado));
        }

        // Con miles de archivos pequeños llegan miles por repintado: solo los últimos llegan a verse.
        foreach (var terminado in lote.TakeLast(MaximoRecientes))
        {
            Recientes.Insert(0, FilaTerminada.Desde(terminado));
        }

        while (Recientes.Count > MaximoRecientes)
        {
            Recientes.RemoveAt(Recientes.Count - 1);
        }
    }

    private static string DescribirConteo(InstantaneaProgreso foto) => foto.Total > 0
        ? $"{foto.Procesados:N0} de {foto.Total:N0}" + (foto.BytesTotal > 0 ? $"  ·  {Formatos.Tamano(foto.BytesProcesados)} de {Formatos.Tamano(foto.BytesTotal)}" : string.Empty)
        : $"{foto.Procesados:N0} encontrados  ·  {Formatos.Tamano(foto.BytesProcesados)}";

    private void ActualizarVelocidad(InstantaneaProgreso foto)
    {
        var segundos = (foto.Transcurrido - momentoAnterior).TotalSeconds;
        if (foto.BytesTotal == 0)
        {
            Velocidad = string.Empty;
            Restante = string.Empty;
            return;
        }

        if (segundos < 0.5)
        {
            Restante = Restante.Length == 0 ? "Calculando el tiempo restante..." : Restante;
            return;
        }

        var instantanea = (foto.BytesProcesados - bytesAnteriores) / segundos;
        velocidadSuavizada = velocidadSuavizada == 0 ? instantanea : velocidadSuavizada * 0.7 + instantanea * 0.3;
        bytesAnteriores = foto.BytesProcesados;
        momentoAnterior = foto.Transcurrido;
        Velocidad = $"{Formatos.Tamano((long)Math.Max(velocidadSuavizada, 0))}/s";
        Restante = velocidadSuavizada > 0
            ? "Faltan unos " + Formatos.Duracion(TimeSpan.FromSeconds((foto.BytesTotal - foto.BytesProcesados) / velocidadSuavizada))
            : "Calculando el tiempo restante...";
    }
}
