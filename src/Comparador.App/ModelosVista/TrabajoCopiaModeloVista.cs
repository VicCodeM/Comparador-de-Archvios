using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Comparador.App.Servicios;
using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;
using Wpf.Ui.Controls;

namespace Comparador.App.ModelosVista;

public enum EstadoTrabajo
{
    EnCola,
    Copiando,
    Terminado,
}

/// <summary>
/// Una copia lanzada desde el Explorador o la línea de comandos, con su ventanita (como TeraCopy): avance, pausar,
/// saltar, cancelar y el menú "⋯" con las opciones que de verdad se usan.
/// </summary>
public sealed partial class TrabajoCopiaModeloVista : ObservableObject
{
    private readonly CancellationTokenSource cancelacion = new();
    private ProgresoOperacion? enMarcha;
    private OpcionesCopia? opcionesEnCurso;

    [ObservableProperty] private EstadoTrabajo estado = EstadoTrabajo.EnCola;
    [ObservableProperty] private bool enPausa;
    [ObservableProperty] private bool verDetalles;
    [ObservableProperty] private bool verificar;
    [ObservableProperty] private ReglaConflicto siYaExiste;
    [ObservableProperty] private AlTerminar alTerminar = AlTerminar.Nada;
    [ObservableProperty] private string resumen = string.Empty;
    [ObservableProperty] private InfoBarSeverity severidad = InfoBarSeverity.Success;

    public TrabajoCopiaModeloVista(PedidoCopia pedido, Configuracion config, AccionesArchivo acciones)
    {
        Pedido = pedido;
        Verificar = config.VerificarCopias;
        SiYaExiste = config.SiYaExiste;
        HilosManuales = config.HilosAutomaticos ? null : config.Hilos;
        Terminados = new TerminadosModeloVista(acciones);
        Acciones = acciones;
    }

    public PedidoCopia Pedido { get; }

    public ProgresoModeloVista Progreso { get; } = new();

    public TerminadosModeloVista Terminados { get; }

    private AccionesArchivo Acciones { get; }

    private int? HilosManuales { get; }

    /// <summary>Se dispara al terminar, para que la cola siga con el siguiente y la ventana decida si cerrarse.</summary>
    public event Action<TrabajoCopiaModeloVista>? Termino;

    public string Titulo => (Pedido.Mover ? "Mover " : "Copiar ") + Pedido.Que;

    public string HaciaDonde => "a " + Pedido.Destino;

    /// <summary>Lo elegido en "⋯", a la vista: así no hay que abrir el menú para saber qué va a pasar.</summary>
    public string DescripcionOpciones => string.Join("  ·  ", new[]
    {
        Verificar ? "verifica SHA-256" : "sin verificar",
        "si existe: " + Convertidores.ReglaTextoConverter.Texto(SiYaExiste).ToLowerInvariant(),
        AlTerminar == AlTerminar.Nada ? string.Empty : "al terminar: " + AccionAlTerminar.Nombre(AlTerminar).ToLowerInvariant(),
    }.Where(parte => parte.Length > 0));

    /// <summary>Con la copia en marcha vale para los archivos que todavía no empezaron.</summary>
    partial void OnVerificarChanged(bool value)
    {
        if (opcionesEnCurso is not null)
        {
            opcionesEnCurso.Verificar = value;
        }

        OnPropertyChanged(nameof(DescripcionOpciones));
    }

    partial void OnSiYaExisteChanged(ReglaConflicto value) => OnPropertyChanged(nameof(DescripcionOpciones));

    partial void OnAlTerminarChanged(AlTerminar value) => OnPropertyChanged(nameof(DescripcionOpciones));

    public bool Copiando => Estado == EstadoTrabajo.Copiando;

    public bool EnCola => Estado == EstadoTrabajo.EnCola;

    public bool Terminado => Estado == EstadoTrabajo.Terminado;

    public IReadOnlyList<ReglaConflicto> Reglas { get; } = Enum.GetValues<ReglaConflicto>();

    public IReadOnlyList<AlTerminar> OpcionesAlTerminar { get; } = Enum.GetValues<AlTerminar>();

    partial void OnEstadoChanged(EstadoTrabajo value)
    {
        OnPropertyChanged(nameof(Copiando));
        OnPropertyChanged(nameof(EnCola));
        OnPropertyChanged(nameof(Terminado));
    }

    public async Task EjecutarAsync()
    {
        var progreso = new ProgresoOperacion();
        enMarcha = progreso;
        Progreso.Seguir(progreso, Terminados);
        Estado = EstadoTrabajo.Copiando;
        var reloj = Stopwatch.StartNew();
        using var suspension = PrevencionSuspension.Activar();
        try
        {
            var opciones = opcionesEnCurso = new OpcionesCopia { Verificar = Verificar, HilosManuales = HilosManuales, SiYaExiste = SiYaExiste };
            var resultado = await TrabajoCopia.EjecutarAsync(Pedido, opciones, progreso, cancelacion.Token);
            Severidad = resultado.Fallidos.IsEmpty ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            Resumen = DescribirResumen(resultado, reloj.Elapsed);
        }
        catch (OperationCanceledException)
        {
            Severidad = InfoBarSeverity.Informational;
            Resumen = "Cancelado. Lo que no terminó quedó como estaba: no hay archivos a medias.";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Severidad = InfoBarSeverity.Error;
            Resumen = $"Se detuvo: {error.Message}";
        }
        finally
        {
            Progreso.Detener();
            enMarcha = null;
            EnPausa = false;
            Estado = EstadoTrabajo.Terminado;
            Termino?.Invoke(this);
        }
    }

    private string DescribirResumen(ResumenSincronizacion resultado, TimeSpan duracion)
    {
        var hecho = Pedido.Mover ? ("movido", "movidos") : ("copiado", "copiados");
        var partes = new List<string> { Formatos.Cantidad(resultado.Copiados, hecho.Item1, hecho.Item2) };
        if (resultado.Saltados > 0)
        {
            partes.Add(Formatos.Cantidad(resultado.Saltados, "saltado", "saltados"));
        }

        if (!resultado.Fallidos.IsEmpty)
        {
            partes.Add(Formatos.Cantidad(resultado.Fallidos.Count, "con error", "con error"));
        }

        return $"{string.Join(", ", partes)} en {Formatos.Duracion(duracion)}.";
    }

    [RelayCommand]
    private void PausarOReanudar()
    {
        if (enMarcha is null)
        {
            return;
        }

        if (enMarcha.Pausado)
        {
            enMarcha.Reanudar();
        }
        else
        {
            enMarcha.Pausar();
        }

        EnPausa = enMarcha.Pausado;
    }

    /// <summary>Salta lo que se está copiando ahora (el destino queda como estaba) y sigue con lo demás.</summary>
    [RelayCommand]
    private void SaltarActual()
    {
        foreach (var fila in Progreso.EnCurso)
        {
            fila.Archivo.Saltar();
        }
    }

    [RelayCommand]
    private void SaltarArchivo(FilaEnCurso? fila) => fila?.Archivo.Saltar();

    [RelayCommand]
    private void Cancelar() => cancelacion.Cancel();

    [RelayCommand]
    private void AbrirDestino() => Acciones.Abrir(Pedido.Destino);

    [RelayCommand]
    private void AlternarDetalles() => VerDetalles = !VerDetalles;

    [RelayCommand]
    private void FijarRegla(ReglaConflicto regla) => SiYaExiste = regla;

    [RelayCommand]
    private void FijarAlTerminar(AlTerminar accion) => AlTerminar = accion;
}
