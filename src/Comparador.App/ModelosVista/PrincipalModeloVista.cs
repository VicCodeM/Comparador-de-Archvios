using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Comparador.App.Servicios;
using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;
using MaterialDesignThemes.Wpf;

namespace Comparador.App.ModelosVista;

public enum PasoApp
{
    Configuracion = 1,
    Comparando = 2,
    Revision = 3,
    Sincronizando = 4,
    Resumen = 5,
}

/// <summary>
/// Modelo de vista principal que orquesta el flujo en 4 pasos de la aplicación,
/// la configuración persistente, el cambio de temas y las operaciones asíncronas.
/// </summary>
public sealed partial class PrincipalModeloVista : ObservableObject
{
    private readonly PaletteHelper paleta = new();
    private CancellationTokenSource? cts;
    private Configuracion config;

    [ObservableProperty] private PasoApp pasoActual = PasoApp.Configuracion;
    [ObservableProperty] private string rutaOrigen = string.Empty;
    [ObservableProperty] private string rutaDestino = string.Empty;
    [ObservableProperty] private bool modoExacto;
    [ObservableProperty] private bool detectarSobrantes = true;
    [ObservableProperty] private string exclusiones = string.Empty;
    [ObservableProperty] private bool verificarCopias = true;
    [ObservableProperty] private bool temaOscuro;
    [ObservableProperty] private ModoEmparejado modoEmparejado = ModoEmparejado.UnoAUno;
    [ObservableProperty] private int resumenCopiados;
    [ObservableProperty] private int resumenFallidos;
    [ObservableProperty] private string resumenTiempo = string.Empty;

    public ObservableCollection<ParRutas> ParesManuales { get; } = [];
    public ObservableCollection<ParRutas> Recientes { get; } = [];
    public SnackbarMessageQueue Avisos { get; } = new(TimeSpan.FromSeconds(4));
    public ProgresoModeloVista Progreso { get; } = new();
    public RevisionModeloVista Revision { get; }

    public bool EsPasoConfiguracion => PasoActual == PasoApp.Configuracion;
    public bool EsPasoComparando => PasoActual == PasoApp.Comparando;
    public bool EsPasoRevision => PasoActual == PasoApp.Revision;
    public bool EsPasoSincronizando => PasoActual == PasoApp.Sincronizando;
    public bool EsPasoResumen => PasoActual == PasoApp.Resumen;
    public bool EnOperacion => EsPasoComparando || EsPasoSincronizando;

    public PrincipalModeloVista()
    {
        Revision = new RevisionModeloVista(Avisos);
        config = Configuracion.Cargar();
        AplicarConfiguracion(config);
    }

    private void AplicarConfiguracion(Configuracion c)
    {
        ModoExacto = c.ModoExacto;
        DetectarSobrantes = c.DetectarSobrantes;
        Exclusiones = c.Exclusiones;
        VerificarCopias = c.VerificarCopias;
        TemaOscuro = c.TemaOscuro;
        Recientes.Clear();
        foreach (var par in c.Recientes)
        {
            Recientes.Add(par);
        }

        if (Recientes.Count > 0)
        {
            RutaOrigen = Recientes[0].Origen;
            RutaDestino = Recientes[0].Destino;
        }

        ActualizarTemaVisual(TemaOscuro);
    }

    private void GuardarConfiguracionActual()
    {
        config = config with
        {
            ModoExacto = ModoExacto,
            DetectarSobrantes = DetectarSobrantes,
            Exclusiones = Exclusiones,
            VerificarCopias = VerificarCopias,
            TemaOscuro = TemaOscuro,
        };
        config.Guardar();
    }

    partial void OnPasoActualChanged(PasoApp value)
    {
        OnPropertyChanged(nameof(EsPasoConfiguracion));
        OnPropertyChanged(nameof(EsPasoComparando));
        OnPropertyChanged(nameof(EsPasoRevision));
        OnPropertyChanged(nameof(EsPasoSincronizando));
        OnPropertyChanged(nameof(EsPasoResumen));
        OnPropertyChanged(nameof(EnOperacion));
    }

    public bool ModoRapido
    {
        get => !ModoExacto;
        set
        {
            if (value)
            {
                ModoExacto = false;
            }
        }
    }

    public PackIconKind IconoTema => TemaOscuro ? PackIconKind.WeatherSunny : PackIconKind.WeatherNight;

    partial void OnModoExactoChanged(bool value)
    {
        OnPropertyChanged(nameof(ModoRapido));
    }

    partial void OnTemaOscuroChanged(bool value)
    {
        ActualizarTemaVisual(value);
        GuardarConfiguracionActual();
        OnPropertyChanged(nameof(IconoTema));
    }

    private void ActualizarTemaVisual(bool oscuro)
    {
        var theme = paleta.GetTheme();
        theme.SetBaseTheme(oscuro ? BaseTheme.Dark : BaseTheme.Light);
        paleta.SetTheme(theme);
    }

    [RelayCommand]
    private void AlternarTema() => TemaOscuro = !TemaOscuro;

    [RelayCommand]
    private void ExaminarOrigen()
    {
        var carpetas = Escritorio.ElegirCarpetas("Seleccionar carpeta de origen", varias: false);
        if (carpetas.Count > 0)
        {
            RutaOrigen = carpetas[0];
        }
    }

    [RelayCommand]
    private void ExaminarDestino()
    {
        var carpetas = Escritorio.ElegirCarpetas("Seleccionar carpeta de destino", varias: false);
        if (carpetas.Count > 0)
        {
            RutaDestino = carpetas[0];
        }
    }

    [RelayCommand]
    private void UsarReciente(ParRutas? par)
    {
        if (par is null) return;
        RutaOrigen = par.Origen;
        RutaDestino = par.Destino;
    }

    [RelayCommand]
    private void IntercambiarRutas()
    {
        (RutaOrigen, RutaDestino) = (RutaDestino, RutaOrigen);
    }

    [RelayCommand]
    private void AgregarPar()
    {
        if (string.IsNullOrWhiteSpace(RutaOrigen) || string.IsNullOrWhiteSpace(RutaDestino))
        {
            Avisos.Enqueue("Debe especificar tanto el origen como el destino");
            return;
        }

        var par = new ParRutas(RutaOrigen.Trim(), RutaDestino.Trim());
        if (!ParesManuales.Contains(par))
        {
            ParesManuales.Add(par);
            Avisos.Enqueue("Par añadido a la lista");
        }
    }

    [RelayCommand]
    private void QuitarPar(ParRutas? par)
    {
        if (par is not null)
        {
            ParesManuales.Remove(par);
        }
    }

    [RelayCommand]
    private void LimpiarPares() => ParesManuales.Clear();

    public IReadOnlyList<ParRutas> ObtenerParesAComparar()
    {
        if (ParesManuales.Count > 0)
        {
            return ParesManuales.ToList();
        }

        if (string.IsNullOrWhiteSpace(RutaOrigen) || string.IsNullOrWhiteSpace(RutaDestino))
        {
            return [];
        }

        return [new ParRutas(RutaOrigen.Trim(), RutaDestino.Trim())];
    }

    [RelayCommand]
    private async Task IniciarComparacionAsync()
    {
        var pares = ObtenerParesAComparar();
        if (pares.Count == 0)
        {
            Avisos.Enqueue("Seleccione una carpeta de origen y una de destino");
            return;
        }

        foreach (var par in pares)
        {
            if (!Directory.Exists(par.Origen))
            {
                Avisos.Enqueue($"La carpeta de origen no existe: {par.Origen}");
                return;
            }
            if (!Directory.Exists(par.Destino))
            {
                try
                {
                    Directory.CreateDirectory(par.Destino);
                }
                catch (Exception error)
                {
                    Avisos.Enqueue($"No se pudo acceder al destino: {error.Message}");
                    return;
                }
            }
        }

        config = config.ConRecientes(pares);
        GuardarConfiguracionActual();
        Recientes.Clear();
        foreach (var r in config.Recientes)
        {
            Recientes.Add(r);
        }

        var opciones = new OpcionesComparacion
        {
            Modo = ModoExacto ? ModoComparacion.Exacto : ModoComparacion.Rapido,
            DetectarSobrantes = DetectarSobrantes,
            Exclusiones = FiltroExclusiones.DesdeTexto(Exclusiones),
        };

        var progreso = new ProgresoOperacion();
        Progreso.Seguir(progreso);
        PasoActual = PasoApp.Comparando;
        cts = new CancellationTokenSource();

        using var suspension = PrevencionSuspension.Activar();
        try
        {
            var comparador = new ComparadorCarpetas();
            var resultado = await comparador.CompararAsync(pares, opciones, progreso, cts.Token);
            Progreso.Detener();
            Revision.Cargar(resultado);
            PasoActual = PasoApp.Revision;
        }
        catch (OperationCanceledException)
        {
            Progreso.Detener();
            Avisos.Enqueue("Comparación cancelada por el usuario");
            PasoActual = PasoApp.Configuracion;
        }
        catch (Exception error)
        {
            Progreso.Detener();
            Avisos.Enqueue($"Error en la comparación: {error.Message}");
            PasoActual = PasoApp.Configuracion;
        }
        finally
        {
            cts.Dispose();
            cts = null;
        }
    }

    [RelayCommand]
    private void CancelarOperacion()
    {
        cts?.Cancel();
    }

    [RelayCommand]
    private void VolverAConfiguracion()
    {
        PasoActual = PasoApp.Configuracion;
    }

    [RelayCommand]
    private void VolverARevision()
    {
        PasoActual = PasoApp.Revision;
    }

    [RelayCommand]
    private async Task IniciarSincronizacionAsync()
    {
        var seleccionados = Revision.Todos.Where(e => e.Seleccionado && e.SePuedeSincronizar).ToList();
        if (seleccionados.Count == 0)
        {
            Avisos.Enqueue("No hay elementos seleccionados para sincronizar");
            return;
        }

        var progreso = new ProgresoOperacion();
        Progreso.Seguir(progreso);
        PasoActual = PasoApp.Sincronizando;
        cts = new CancellationTokenSource();

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        using var suspension = PrevencionSuspension.Activar();
        try
        {
            var sincronizador = new SincronizadorArchivos();
            var resumen = await sincronizador.SincronizarAsync(Revision.Todos, VerificarCopias, progreso, cts.Token);
            Progreso.Detener();
            reloj.Stop();

            ResumenCopiados = resumen.Copiados;
            ResumenFallidos = resumen.Fallidos.Count;
            ResumenTiempo = Formatos.Duracion(reloj.Elapsed);
            Revision.Recontar();

            PasoActual = PasoApp.Resumen;
        }
        catch (OperationCanceledException)
        {
            Progreso.Detener();
            Revision.Recontar();
            Avisos.Enqueue("Sincronización cancelada. Los archivos pendientes no se modificaron.");
            PasoActual = PasoApp.Revision;
        }
        catch (Exception error)
        {
            Progreso.Detener();
            Revision.Recontar();
            Avisos.Enqueue($"Error durante la sincronización: {error.Message}");
            PasoActual = PasoApp.Revision;
        }
        finally
        {
            cts.Dispose();
            cts = null;
        }
    }
}
