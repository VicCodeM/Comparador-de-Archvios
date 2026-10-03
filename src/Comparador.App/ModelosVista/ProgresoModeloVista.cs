using System.Windows.Threading;
using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Comparador.App.ModelosVista;

/// <summary>
/// Pinta el progreso leyéndolo 5 veces por segundo. El trabajo nunca le avisa a la ventana por cada archivo:
/// así la ventana responde igual con 10 archivos que con 10 millones.
/// </summary>
public sealed partial class ProgresoModeloVista : ObservableObject
{
    private readonly DispatcherTimer reloj = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private ProgresoOperacion? seguido;
    private long bytesAnteriores;
    private TimeSpan momentoAnterior;
    private double velocidadSuavizada;

    [ObservableProperty] private string fase = string.Empty;
    [ObservableProperty] private string detalle = string.Empty;
    [ObservableProperty] private string actual = string.Empty;
    [ObservableProperty] private string velocidad = string.Empty;
    [ObservableProperty] private string restante = string.Empty;
    [ObservableProperty] private double porcentaje;
    [ObservableProperty] private bool indeterminado = true;

    public ProgresoModeloVista() => reloj.Tick += (_, _) => Actualizar();

    public void Seguir(ProgresoOperacion progreso)
    {
        seguido = progreso;
        Fase = string.Empty;
        reloj.Start();
        Actualizar();
    }

    public void Detener()
    {
        Actualizar();
        reloj.Stop();
        seguido = null;
    }

    private void Actualizar()
    {
        if (seguido?.Instantanea() is not { } foto)
        {
            return;
        }

        if (foto.Fase != Fase)
        {
            bytesAnteriores = 0;
            momentoAnterior = TimeSpan.Zero;
            velocidadSuavizada = 0;
        }

        Fase = foto.Fase;
        Actual = foto.Actual;
        Indeterminado = !foto.TieneTotal;
        Porcentaje = foto.Porcentaje;
        Detalle = DescribirConteo(foto);
        ActualizarVelocidad(foto);
    }

    private static string DescribirConteo(InstantaneaProgreso foto) => foto.Total > 0
        ? $"{foto.Procesados:N0} de {foto.Total:N0}" + (foto.BytesTotal > 0 ? $"  ·  {Formatos.Tamano(foto.BytesProcesados)} de {Formatos.Tamano(foto.BytesTotal)}" : string.Empty)
        : $"{foto.Procesados:N0} encontrados  ·  {Formatos.Tamano(foto.BytesProcesados)}";

    private void ActualizarVelocidad(InstantaneaProgreso foto)
    {
        var segundos = (foto.Transcurrido - momentoAnterior).TotalSeconds;
        if (segundos < 0.5 || foto.BytesTotal == 0)
        {
            Velocidad = foto.BytesTotal == 0 ? string.Empty : Velocidad;
            Restante = foto.BytesTotal == 0 ? string.Empty : Restante;
            return;
        }

        var instantanea = (foto.BytesProcesados - bytesAnteriores) / segundos;
        velocidadSuavizada = velocidadSuavizada == 0 ? instantanea : velocidadSuavizada * 0.7 + instantanea * 0.3;
        bytesAnteriores = foto.BytesProcesados;
        momentoAnterior = foto.Transcurrido;
        Velocidad = $"{Formatos.Tamano((long)velocidadSuavizada)}/s";
        Restante = velocidadSuavizada > 0
            ? "Quedan unos " + Formatos.Duracion(TimeSpan.FromSeconds((foto.BytesTotal - foto.BytesProcesados) / velocidadSuavizada))
            : string.Empty;
    }
}
