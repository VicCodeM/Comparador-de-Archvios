using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Comparador.App.Servicios;
using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;
using Wpf.Ui.Controls;

namespace Comparador.App.ModelosVista;

public enum Pagina
{
    Ubicaciones,
    Comparacion,
    Copia,
    Configuracion,
}

public enum Operacion
{
    Ninguna,
    Comparando,
    Copiando,
}

/// <summary>Una entrada del menú lateral. Se puede pulsar si ya hay algo que ver en esa página.</summary>
public sealed partial class EntradaMenu(Pagina pagina, string titulo, SymbolRegular icono) : ObservableObject
{
    [ObservableProperty] private bool esActual;
    [ObservableProperty] private bool disponible;
    [ObservableProperty] private string estado = string.Empty;

    public Pagina Pagina { get; } = pagina;

    public string Titulo { get; } = titulo;

    public SymbolRegular Icono { get; } = icono;
}

/// <summary>
/// Orquesta la app: qué página se ve, qué operación corre y la configuración. Navegar NUNCA cancela una operación:
/// se puede volver a Ubicaciones o a Configuración mientras se copia, y regresar a ver el avance.
/// </summary>
public sealed partial class PrincipalModeloVista : ObservableObject
{
    private CancellationTokenSource? cancelacion;
    private Configuracion config;
    private bool cargando;

    [ObservableProperty] private Pagina paginaActual = Pagina.Ubicaciones;
    [ObservableProperty] private Operacion operacionActual = Operacion.Ninguna;
    [ObservableProperty] private bool modoExacto;
    [ObservableProperty] private bool detectarSobrantes = true;
    [ObservableProperty] private string exclusiones = string.Empty;
    [ObservableProperty] private bool verificarCopias = true;
    [ObservableProperty] private TemaApp tema;
    [ObservableProperty] private bool hilosAutomaticos = true;
    [ObservableProperty] private double hilos = 4;
    [ObservableProperty] private bool evitarSuspension = true;
    [ObservableProperty] private bool copiaTerminada;
    [ObservableProperty] private string resumenCopia = string.Empty;
    [ObservableProperty] private InfoBarSeverity severidadCopia = InfoBarSeverity.Success;
    [ObservableProperty] private string paresDeLaComparacion = string.Empty;

    public PrincipalModeloVista()
    {
        Avisos = new AvisosModeloVista();
        Origen = new SelectorUbicacionModeloVista("Origen", Avisos);
        Destino = new SelectorUbicacionModeloVista("Destino", Avisos);
        Revision = new RevisionModeloVista(Avisos);
        Menu =
        [
            new EntradaMenu(Pagina.Ubicaciones, "Ubicaciones", SymbolRegular.FolderSwap24),
            new EntradaMenu(Pagina.Comparacion, "Comparación", SymbolRegular.BranchCompare24),
            new EntradaMenu(Pagina.Copia, "Copia", SymbolRegular.DocumentCopy24),
        ];
        MenuInferior = new EntradaMenu(Pagina.Configuracion, "Configuración", SymbolRegular.Settings24) { Disponible = true };
        config = Configuracion.Cargar();
        AplicarConfiguracion();
        ActualizarMenu();
        Progreso.PropertyChanged += (_, cambio) =>
        {
            if (cambio.PropertyName == nameof(ProgresoModeloVista.Restante))
            {
                ActualizarMenu();
            }
            else if (cambio.PropertyName == nameof(ProgresoModeloVista.Porcentaje))
            {
                OnPropertyChanged(nameof(AvanceBarraTareas));
            }
            else if (cambio.PropertyName == nameof(ProgresoModeloVista.Indeterminado))
            {
                OnPropertyChanged(nameof(EstadoBarraTareas));
            }
        };
    }

    public AvisosModeloVista Avisos { get; }

    public SelectorUbicacionModeloVista Origen { get; }

    public SelectorUbicacionModeloVista Destino { get; }

    public RevisionModeloVista Revision { get; }

    public ProgresoModeloVista Progreso { get; } = new();

    public IReadOnlyList<EntradaMenu> Menu { get; }

    public EntradaMenu MenuInferior { get; }

    public ObservableCollection<ParRutas> ParesExtra { get; } = [];

    public ObservableCollection<ParRutas> Recientes { get; } = [];

    public IReadOnlyList<TemaApp> Temas { get; } = Enum.GetValues<TemaApp>();

    public bool Ocupado => OperacionActual != Operacion.Ninguna;

    public bool Comparando => OperacionActual == Operacion.Comparando;

    public bool Copiando => OperacionActual == Operacion.Copiando;

    public bool MostrarTabla => !Comparando && Revision.HayResultado;

    /// <summary>El avance en el botón de la barra de tareas de Windows, para verlo con la ventana minimizada.</summary>
    public double AvanceBarraTareas => Progreso.Porcentaje / 100;

    public System.Windows.Shell.TaskbarItemProgressState EstadoBarraTareas => !Ocupado
        ? System.Windows.Shell.TaskbarItemProgressState.None
        : Progreso.Indeterminado ? System.Windows.Shell.TaskbarItemProgressState.Indeterminate : System.Windows.Shell.TaskbarItemProgressState.Normal;

    public int HilosMinimo => Concurrencia.Minimo;

    public int HilosMaximo => Concurrencia.Maximo;

    public string Version { get; } = $"Versión {typeof(PrincipalModeloVista).Assembly.GetName().Version?.ToString(3)}";

    public bool ModoRapido
    {
        get => !ModoExacto;
        set => ModoExacto = !value;
    }

    public string DescripcionHilos => HilosAutomaticos
        ? "Automático: 2 en memorias USB, 4 en discos y red, 1 con teléfonos"
        : $"{(int)Hilos} copias a la vez (con un teléfono siempre se usa 1)";

    private void AplicarConfiguracion()
    {
        cargando = true;
        ModoExacto = config.ModoExacto;
        DetectarSobrantes = config.DetectarSobrantes;
        Exclusiones = config.Exclusiones;
        VerificarCopias = config.VerificarCopias;
        Tema = config.Tema;
        HilosAutomaticos = config.HilosAutomaticos;
        Hilos = config.Hilos;
        EvitarSuspension = config.EvitarSuspension;
        CargarRecientes();
        if (Recientes.Count > 0)
        {
            UsarReciente(Recientes[0]);
        }

        cargando = false;
    }

    private void CargarRecientes()
    {
        Recientes.Clear();
        foreach (var par in config.Recientes)
        {
            Recientes.Add(par);
        }
    }

    private void Guardar()
    {
        if (cargando)
        {
            return;
        }

        config = config with
        {
            ModoExacto = ModoExacto,
            DetectarSobrantes = DetectarSobrantes,
            Exclusiones = Exclusiones,
            VerificarCopias = VerificarCopias,
            Tema = Tema,
            HilosAutomaticos = HilosAutomaticos,
            Hilos = (int)Hilos,
            EvitarSuspension = EvitarSuspension,
        };
        config.Guardar();
    }

    partial void OnModoExactoChanged(bool value) => OnPropertyChanged(nameof(ModoRapido));

    partial void OnTemaChanged(TemaApp value)
    {
        Apariencia.Aplicar(value);
        Guardar();
    }

    partial void OnVerificarCopiasChanged(bool value) => Guardar();

    partial void OnEvitarSuspensionChanged(bool value) => Guardar();

    partial void OnHilosAutomaticosChanged(bool value)
    {
        OnPropertyChanged(nameof(DescripcionHilos));
        Guardar();
    }

    partial void OnHilosChanged(double value)
    {
        OnPropertyChanged(nameof(DescripcionHilos));
        Guardar();
    }

    partial void OnPaginaActualChanged(Pagina value) => ActualizarMenu();

    partial void OnCopiaTerminadaChanged(bool value) => ActualizarMenu();

    partial void OnOperacionActualChanged(Operacion value)
    {
        OnPropertyChanged(nameof(Ocupado));
        OnPropertyChanged(nameof(Comparando));
        OnPropertyChanged(nameof(Copiando));
        OnPropertyChanged(nameof(MostrarTabla));
        OnPropertyChanged(nameof(EstadoBarraTareas));
        IniciarComparacionCommand.NotifyCanExecuteChanged();
        IniciarCopiaCommand.NotifyCanExecuteChanged();
        ActualizarMenu();
    }

    private void ActualizarMenu()
    {
        foreach (var entrada in Menu.Append(MenuInferior))
        {
            entrada.EsActual = entrada.Pagina == PaginaActual;
        }

        Menu[0].Disponible = true;
        Menu[0].Estado = string.Empty;
        Menu[1].Disponible = Comparando || Revision.HayResultado;
        Menu[1].Estado = Comparando ? EnCurso() : Revision.HayResultado ? $"{Revision.Pendientes:N0} por copiar" : string.Empty;
        Menu[2].Disponible = Copiando || CopiaTerminada;
        Menu[2].Estado = Copiando ? EnCurso() : CopiaTerminada ? "Terminada" : string.Empty;
    }

    private string EnCurso() => Progreso.Restante.StartsWith("Faltan", StringComparison.Ordinal) ? Progreso.Restante : "En curso";

    [RelayCommand]
    private void Navegar(EntradaMenu? entrada)
    {
        if (entrada is { Disponible: true })
        {
            PaginaActual = entrada.Pagina;
        }
    }

    [RelayCommand]
    private void UsarReciente(ParRutas? par)
    {
        if (par is null)
        {
            return;
        }

        Origen.Ruta = par.Origen;
        Destino.Ruta = par.Destino;
    }

    [RelayCommand]
    private void IntercambiarRutas() => (Origen.Ruta, Destino.Ruta) = (Destino.Ruta, Origen.Ruta);

    [RelayCommand]
    private void AgregarPar()
    {
        if (ParActual() is not { } par)
        {
            Avisos.Advertir("Elige un origen y un destino antes de agregarlos a la lista");
            return;
        }

        if (!ParesExtra.Contains(par))
        {
            ParesExtra.Add(par);
        }

        Origen.Ruta = string.Empty;
        Destino.Ruta = string.Empty;
    }

    [RelayCommand]
    private void QuitarPar(ParRutas? par)
    {
        if (par is not null)
        {
            ParesExtra.Remove(par);
        }
    }

    private ParRutas? ParActual() => string.IsNullOrWhiteSpace(Origen.Ruta) || string.IsNullOrWhiteSpace(Destino.Ruta)
        ? null
        : new ParRutas(Origen.Ruta.Trim(), Destino.Ruta.Trim());

    private IReadOnlyList<ParRutas> ParesAComparar() => ParActual() is { } actual && !ParesExtra.Contains(actual)
        ? [.. ParesExtra, actual]
        : [.. ParesExtra];

    private bool PuedeEmpezar() => !Ocupado;

    [RelayCommand(CanExecute = nameof(PuedeEmpezar))]
    private async Task IniciarComparacionAsync()
    {
        var pares = ParesAComparar();
        if (pares.Count == 0)
        {
            Avisos.Advertir("Elige una carpeta de origen y una de destino");
            return;
        }

        config = config.ConRecientes(pares);
        Guardar();
        CargarRecientes();
        ParesDeLaComparacion = string.Join("   ·   ", pares.Select(par => $"{par.Origen}  →  {par.Destino}"));
        var opciones = new OpcionesComparacion
        {
            Modo = ModoExacto ? ModoComparacion.Exacto : ModoComparacion.Rapido,
            DetectarSobrantes = DetectarSobrantes,
            Exclusiones = FiltroExclusiones.DesdeTexto(Exclusiones),
            HilosManuales = HilosAutomaticos ? null : (int)Hilos,
        };

        var progreso = new ProgresoOperacion();
        Progreso.Seguir(progreso);
        OperacionActual = Operacion.Comparando;
        PaginaActual = Pagina.Comparacion;
        cancelacion = new CancellationTokenSource();
        using var suspension = EvitarSuspension ? PrevencionSuspension.Activar() : null;
        try
        {
            var resultado = await new ComparadorCarpetas().CompararAsync(pares, opciones, progreso, cancelacion.Token);
            Revision.Cargar(resultado);
            CopiaTerminada = false;
            if (Revision.TodoSincronizado)
            {
                Avisos.Exito("Todo está igual: no hay nada que copiar");
            }
        }
        catch (OperationCanceledException)
        {
            Avisos.Informar("Comparación cancelada");
            PaginaActual = Revision.HayResultado ? Pagina.Comparacion : Pagina.Ubicaciones;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Avisos.Error(error.Message);
            PaginaActual = Pagina.Ubicaciones;
        }
        finally
        {
            Progreso.Detener();
            Terminar();
        }
    }

    [RelayCommand]
    private void Cancelar() => cancelacion?.Cancel();

    [RelayCommand(CanExecute = nameof(PuedeEmpezar))]
    private async Task IniciarCopiaAsync()
    {
        if (Revision.Seleccionados == 0)
        {
            Avisos.Advertir("No hay nada seleccionado para copiar");
            return;
        }

        var progreso = new ProgresoOperacion();
        Progreso.Seguir(progreso);
        CopiaTerminada = false;
        OperacionActual = Operacion.Copiando;
        PaginaActual = Pagina.Copia;
        Revision.PausarAvisos();
        cancelacion = new CancellationTokenSource();
        var reloj = Stopwatch.StartNew();
        using var suspension = EvitarSuspension ? PrevencionSuspension.Activar() : null;
        try
        {
            var resumen = await new SincronizadorArchivos().SincronizarAsync(
                Revision.Todos, VerificarCopias, HilosAutomaticos ? null : (int)Hilos, progreso, cancelacion.Token);
            SeveridadCopia = resumen.Fallidos.IsEmpty ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            ResumenCopia = resumen.Fallidos.IsEmpty
                ? $"Se copiaron {resumen.Copiados:N0} elementos en {Formatos.Duracion(reloj.Elapsed)}" + (VerificarCopias ? ", todos verificados con SHA-256." : ".")
                : $"Se copiaron {resumen.Copiados:N0} elementos y {resumen.Fallidos.Count:N0} fallaron. Abajo está el motivo de cada uno.";
        }
        catch (OperationCanceledException)
        {
            SeveridadCopia = InfoBarSeverity.Informational;
            ResumenCopia = "Copia cancelada. Lo que no terminó de copiarse quedó como estaba: no hay archivos a medias.";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            SeveridadCopia = InfoBarSeverity.Error;
            ResumenCopia = $"La copia se detuvo: {error.Message}";
        }
        finally
        {
            Progreso.Detener();
            Revision.ReanudarAvisos();
            CopiaTerminada = true;
            Terminar();
        }
    }

    private void Terminar()
    {
        cancelacion?.Dispose();
        cancelacion = null;
        OperacionActual = Operacion.Ninguna;
    }

    [RelayCommand]
    private void IrA(Pagina pagina) => PaginaActual = pagina;
}
