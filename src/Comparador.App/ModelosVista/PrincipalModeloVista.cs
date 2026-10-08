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
    Clonar,
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
    private ProgresoOperacion? enMarcha;
    private Configuracion config;
    private bool cargando;
    private OpcionesCopia? opcionesEnCurso;

    [ObservableProperty] private Pagina paginaActual = Pagina.Ubicaciones;
    [ObservableProperty] private Operacion operacionActual = Operacion.Ninguna;
    [ObservableProperty] private bool modoExacto;
    [ObservableProperty] private bool detectarSobrantes = true;
    [ObservableProperty] private string exclusiones = string.Empty;
    [ObservableProperty] private bool verificarCopias;
    [ObservableProperty] private TemaApp tema;
    [ObservableProperty] private bool hilosAutomaticos = true;
    [ObservableProperty] private double hilos = 4;
    [ObservableProperty] private ReglaConflicto siYaExiste = ReglaConflicto.Reemplazar;
    [ObservableProperty] private bool enPausa;
    [ObservableProperty] private bool integradoExplorador = IntegracionExplorador.EstaInstalada();
    [ObservableProperty] private bool pegarConEspejo;
    [ObservableProperty] private bool copiaTerminada;
    [ObservableProperty] private string resumenCopia = string.Empty;
    [ObservableProperty] private InfoBarSeverity severidadCopia = InfoBarSeverity.Success;
    [ObservableProperty] private string paresDeLaComparacion = string.Empty;

    public PrincipalModeloVista()
    {
        Avisos = new AvisosModeloVista();
        Clonar = new ClonarModeloVista(Avisos);
        Origen = new SelectorUbicacionModeloVista("Origen", Avisos);
        Destino = new SelectorUbicacionModeloVista("Destino", Avisos);
        var acciones = new AccionesArchivo(Avisos);
        Revision = new RevisionModeloVista(Avisos, acciones);
        Terminados = new TerminadosModeloVista(acciones);
        Menu =
        [
            new EntradaMenu(Pagina.Ubicaciones, "Ubicaciones", SymbolRegular.FolderSwap24),
            new EntradaMenu(Pagina.Comparacion, "Comparación", SymbolRegular.BranchCompare24),
            new EntradaMenu(Pagina.Copia, "Copia", SymbolRegular.DocumentCopy24),
            new EntradaMenu(Pagina.Clonar, "Clonar", SymbolRegular.Storage24),
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

    public ClonarModeloVista Clonar { get; }

    public SelectorUbicacionModeloVista Origen { get; }

    public SelectorUbicacionModeloVista Destino { get; }

    public RevisionModeloVista Revision { get; }

    public ProgresoModeloVista Progreso { get; } = new();

    public TerminadosModeloVista Terminados { get; }

    /// <summary>
    /// Casi nunca hace falta: solo para carpetas que Windows protege (de otros usuarios o del sistema). Ojo: como
    /// administrador no se ven las unidades de red mapeadas (Z:), solo las rutas \\servidor\carpeta.
    /// </summary>
    public bool EsAdministrador { get; } = Elevacion.EsAdministrador();

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
        ? "Automático: mide la velocidad mientras copia y sube o baja los hilos hasta dar con lo que mejor rinde en este equipo (con teléfonos, siempre 1)"
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
        SiYaExiste = config.SiYaExiste;
        PegarConEspejo = config.PegarConEspejo;
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
            SiYaExiste = SiYaExiste,
            PegarConEspejo = PegarConEspejo,
        };
        config.Guardar();
    }

    partial void OnModoExactoChanged(bool value) => OnPropertyChanged(nameof(ModoRapido));

    partial void OnTemaChanged(TemaApp value)
    {
        Apariencia.Aplicar(value);
        Guardar();
    }

    /// <summary>Se guarda y, si hay una copia en marcha, vale para los archivos que todavía no empezaron.</summary>
    partial void OnVerificarCopiasChanged(bool value)
    {
        if (opcionesEnCurso is not null)
        {
            opcionesEnCurso.Verificar = value;
        }

        Guardar();
    }

    partial void OnSiYaExisteChanged(ReglaConflicto value) => Guardar();

    /// <summary>Ctrl+V en el Explorador lo pega Espejo (en segundo plano, junto al reloj) o vuelve a ser de Windows.</summary>
    partial void OnPegarConEspejoChanged(bool value)
    {
        if (cargando)
        {
            return;
        }

        try
        {
            ((App)System.Windows.Application.Current).CambiarResidente(value);
            Guardar();
            if (value)
            {
                Avisos.Exito("Listo: Ctrl+V en el Explorador ahora lo pega Espejo. Al cerrar la ventana sigue junto al reloj.");
            }
        }
        catch (Exception error) when (error is InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Avisos.Error($"No se pudo cambiar quién pega en el Explorador: {error.Message}");
        }
    }

    /// <summary>Pone o quita las opciones del clic derecho del Explorador (solo para este usuario).</summary>
    partial void OnIntegradoExploradorChanged(bool value)
    {
        try
        {
            if (value)
            {
                IntegracionExplorador.Instalar();
                Avisos.Exito("Listo: en el clic derecho del Explorador (en Windows 11, en \"Mostrar más opciones\") ya está Espejo");
            }
            else
            {
                IntegracionExplorador.Quitar();
            }
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            Avisos.Error($"No se pudo cambiar el menú del Explorador: {error.Message}");
        }
    }

    public IReadOnlyList<ReglaConflicto> Reglas { get; } = Enum.GetValues<ReglaConflicto>();

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
        ReintentarFallidosCommand.NotifyCanExecuteChanged();
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
        Menu[3].Disponible = true;
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
        using var suspension = PrevencionSuspension.Activar();
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
        enMarcha = progreso;
        Progreso.Seguir(progreso, Terminados);
        CopiaTerminada = false;
        OperacionActual = Operacion.Copiando;
        PaginaActual = Pagina.Copia;
        Revision.PausarAvisos();
        cancelacion = new CancellationTokenSource();
        var reloj = Stopwatch.StartNew();
        using var suspension = PrevencionSuspension.Activar();
        try
        {
            var opciones = opcionesEnCurso = new OpcionesCopia
            {
                Verificar = VerificarCopias,
                HilosManuales = HilosAutomaticos ? null : (int)Hilos,
                SiYaExiste = SiYaExiste,
            };
            var resumen = await new SincronizadorArchivos().SincronizarAsync(Revision.Todos, opciones, progreso, cancelacion.Token);
            SeveridadCopia = resumen.Fallidos.IsEmpty ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            ResumenCopia = DescribirResumen(resumen, reloj.Elapsed);
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

    private string DescribirResumen(ResumenSincronizacion resumen, TimeSpan duracion)
    {
        var partes = new List<string> { Formatos.Cantidad(resumen.Copiados, "elemento copiado", "elementos copiados") };
        if (resumen.Saltados > 0)
        {
            partes.Add(Formatos.Cantidad(resumen.Saltados, "saltado", "saltados"));
        }

        if (!resumen.Fallidos.IsEmpty)
        {
            partes.Add(Formatos.Cantidad(resumen.Fallidos.Count, "falló", "fallaron"));
        }

        var verificados = VerificarCopias && resumen.Copiados > 0 ? ", verificados con SHA-256" : string.Empty;

        return $"{string.Join(", ", partes)} en {Formatos.Duracion(duracion)}{verificados}."
            + (resumen.Fallidos.IsEmpty ? string.Empty : " Abajo está el motivo de cada uno.");
    }

    private void Terminar()
    {
        cancelacion?.Dispose();
        cancelacion = null;
        enMarcha = null;
        EnPausa = false;
        OperacionActual = Operacion.Ninguna;
    }

    /// <summary>Pausa o reanuda la copia: los hilos se detienen en el siguiente bloque, sin dejar nada a medias.</summary>
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

    /// <summary>Deja de copiar ese archivo (el destino queda como estaba) y sigue con los demás.</summary>
    [RelayCommand]
    private void SaltarArchivo(FilaEnCurso? fila) => fila?.Archivo.Saltar();

    [RelayCommand]
    private void IrA(Pagina pagina) => PaginaActual = pagina;

    /// <summary>Copia rápida como TeraCopy: sin comparar, elige qué y a dónde, y se abre la ventanita.</summary>
    [RelayCommand]
    private void CopiarArchivos() => LanzarCopiaRapida(Escritorio.ElegirArchivos("Elegir los archivos a copiar"));

    [RelayCommand]
    private void CopiarCarpetas() => LanzarCopiaRapida(Escritorio.ElegirCarpetas("Elegir las carpetas a copiar"));

    private static void LanzarCopiaRapida(IReadOnlyList<string> rutas)
    {
        if (rutas.Count > 0 && LineaDeComandos.Completar(new PedidoExterno(rutas, null, Mover: false, PreguntarDestino: true)) is { } pedido)
        {
            App.Cola.Agregar(pedido);
        }
    }

    /// <summary>Los que fallaron siguen marcados y pendientes: volver a copiar solo toca a esos (y a lo que siga marcado).</summary>
    [RelayCommand(CanExecute = nameof(PuedeEmpezar))]
    private Task ReintentarFallidosAsync() => IniciarCopiaAsync();

    [RelayCommand]
    private void ReiniciarComoAdministrador()
    {
        if (Ocupado)
        {
            Avisos.Advertir("Espera a que termine la operación en curso antes de reiniciar");
            return;
        }

        if (Elevacion.ReiniciarElevado())
        {
            System.Windows.Application.Current.Shutdown();
        }
    }
}
