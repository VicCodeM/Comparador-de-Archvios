using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Comparador.Nucleo.Clonacion;
using Comparador.Nucleo.Servicios;
using Wpf.Ui.Controls;

namespace Comparador.App.ModelosVista;

/// <summary>
/// La ventanita del Espejo que se abre como administrador para clonar ("Espejo.exe clonar origen destino"). Vuelve a
/// leer los discos y a revisar las reglas antes de tocar nada: entre la pantalla y aquí pudo cambiar algo.
/// </summary>
public sealed partial class ClonadoModeloVista : ObservableObject
{
    private readonly ExtremoClon origen;
    private readonly ExtremoClon destino;
    private readonly bool verificar;
    private readonly bool ajustar;
    private readonly CancellationTokenSource cancelacion = new();

    [ObservableProperty] private string fase = "Preparando...";
    [ObservableProperty] private double porcentaje;
    [ObservableProperty] private bool indeterminado = true;
    [ObservableProperty] private string detalle = string.Empty;
    [ObservableProperty] private string velocidad = string.Empty;
    [ObservableProperty] private string restante = string.Empty;
    [ObservableProperty] private bool terminado;
    [ObservableProperty] private string resumen = string.Empty;
    [ObservableProperty] private InfoBarSeverity severidad = InfoBarSeverity.Success;

    /// <param name="ajustar">Disco completo ajustando el tamaño y dejándolo arrancable, en vez de la copia exacta.</param>
    public ClonadoModeloVista(ExtremoClon origen, ExtremoClon destino, bool verificar, bool ajustar)
    {
        this.origen = origen;
        this.destino = destino;
        this.verificar = verificar;
        this.ajustar = ajustar;
        var discos = ListadoDiscos.Leer();
        DeDonde = origen.Describir(discos);
        HaciaDonde = destino.Describir(discos);
    }

    public string Titulo => "Clonando";

    public string DeDonde { get; }

    public string HaciaDonde { get; }

    public async Task EjecutarAsync()
    {
        var progreso = new Progress<AvanceClon>(Mostrar);
        try
        {
            using var suspension = PrevencionSuspension.Activar();
            if (ajustar && origen.NumeroDisco is { } deDisco && destino.NumeroDisco is { } aDisco)
            {
                var duracion = await ClonadoInteligente.ClonarAsync(deDisco, aDisco, verificar, new Progress<AvanceClonado>(MostrarPaso), cancelacion.Token);
                Terminar(InfoBarSeverity.Success, $"Listo en {Formatos.Duracion(duracion)}: el disco está clonado con todas sus particiones. Ya puedes conectarlo en la otra PC.");
                return;
            }

            var resultado = await MotorClon.ClonarAsync(origen, destino, verificar, progreso, cancelacion.Token);
            Terminar(InfoBarSeverity.Success, $"Listo: {Formatos.Tamano(resultado.Bytes)} en {Formatos.Duracion(resultado.Duracion)}"
                + (resultado.Verificado ? ", verificado con SHA-256." : " (sin verificar)."));
        }
        catch (OperationCanceledException)
        {
            Terminar(InfoBarSeverity.Warning, "Cancelado. Si el destino era un disco, quedó a medias: vuelve a clonarlo o formatéalo.");
        }
        catch (CopiaNoIdenticaException)
        {
            Terminar(InfoBarSeverity.Error, "La verificación falló: lo escrito no es igual al origen. La tarjeta o el disco pueden estar dañados (o ser falsos).");
        }
        catch (Exception error)
        {
            // Esta ventanita corre sola como administrador: un error inesperado se muestra en vez de cerrarla de golpe.
            Terminar(InfoBarSeverity.Error, error.Message);
        }
    }

    private void Mostrar(AvanceClon avance)
    {
        Fase = avance.Fase == FaseClon.Copiando ? "Copiando" : "Verificando";
        var fraccion = avance.Total is > 0 ? (double)avance.Hechos / avance.Total.Value : avance.Fraccion;
        Indeterminado = fraccion is null;
        Porcentaje = (fraccion ?? 0) * 100;
        Detalle = avance.Total is { } total ? $"{Formatos.Tamano(avance.Hechos)} de {Formatos.Tamano(total)}" : Formatos.Tamano(avance.Hechos);
        Velocidad = $"{Formatos.Tamano((long)avance.BytesPorSegundo)}/s";
        Restante = avance.Total is { } todo && avance.BytesPorSegundo > 0
            ? $"Faltan {Formatos.Duracion(TimeSpan.FromSeconds((todo - avance.Hechos) / avance.BytesPorSegundo))}"
            : string.Empty;
    }

    private void MostrarPaso(AvanceClonado avance)
    {
        Fase = avance.Paso;
        Indeterminado = avance.Fraccion is null;
        Porcentaje = (avance.Fraccion ?? 0) * 100;
        Detalle = avance.Detalle;
        Velocidad = string.Empty;
        Restante = string.Empty;
    }

    /// <summary>Cada clonado deja su resultado en %LocalAppData%\VMSofts\Espejo\clonados.log: si algo falla, ahí está el porqué.</summary>
    private static readonly string Registro = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VMSofts", "Espejo", "clonados.log");

    private void Terminar(InfoBarSeverity severidad, string texto)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Registro)!);
            File.AppendAllText(Registro, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{severidad}] {DeDonde} -> {HaciaDonde}{(ajustar ? " (ajustando)" : string.Empty)}: {texto}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Sin registro se pierde el detalle, no el clonado: el resultado se ve igual en la ventanita.
        }

        Severidad = severidad;
        Resumen = texto;
        Terminado = true;
    }

    [RelayCommand]
    private void Cancelar() => cancelacion.Cancel();
}
