using System.Windows;
using System.Windows.Controls;
using Comparador.App.ModelosVista;
using Comparador.App.Servicios;
using Comparador.App.Vistas;
using Comparador.Nucleo.Servicios;
using InfoBarSeverity = Wpf.Ui.Controls.InfoBarSeverity;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Comparador.Pruebas;

/// <summary>
/// La ventanita de copia (la del Explorador), con una copia real: Victor la vio con textos encima de los botones y con
/// Pausar y Cancelar que no respondían (2026-10-08).
/// </summary>
public sealed class VentanaCopiaPruebas : IDisposable
{
    private const long TamanoArchivo = 400L * 1024 * 1024;
    private readonly CarpetasDePrueba carpetas = new();

    public void Dispose() => carpetas.Dispose();

    private TrabajoCopiaModeloVista NuevoTrabajo(string nombre)
    {
        var ruta = Path.Combine(carpetas.Origen, nombre);
        using (var archivo = File.Create(ruta))
        {
            archivo.SetLength(TamanoArchivo);
        }

        return new TrabajoCopiaModeloVista(new PedidoCopia([ruta], carpetas.Destino), new Configuracion(), new AccionesArchivo(new AvisosModeloVista()));
    }

    /// <summary>Arranca la copia y la pausa en cuanto empieza, antes de que copie nada.</summary>
    private static async Task<Task> EmpezarEnPausaAsync(TrabajoCopiaModeloVista trabajo)
    {
        var copia = trabajo.EjecutarAsync();
        while (!trabajo.Copiando && !trabajo.Terminado)
        {
            await Task.Delay(5);
        }

        trabajo.PausarOReanudarCommand.Execute(null);

        return copia;
    }

    [Fact]
    public Task Pausar_detiene_la_copia_y_Cancelar_la_termina_sin_dejar_nada() => HiloDeVentana.EjecutarAsync(async () =>
    {
        var trabajo = NuevoTrabajo("pelicula.mkv");
        var copia = await EmpezarEnPausaAsync(trabajo);
        await Task.Delay(600);

        Assert.True(trabajo.EnPausa);
        Assert.False(File.Exists(Path.Combine(carpetas.Destino, "pelicula.mkv")));

        trabajo.CancelarCommand.Execute(null);
        await copia.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(trabajo.Terminado);
        Assert.Equal(InfoBarSeverity.Informational, trabajo.Severidad);
        Assert.Empty(Directory.GetFiles(carpetas.Destino, "*", SearchOption.AllDirectories));
    });

    [Fact]
    public Task Reanudar_continua_y_la_copia_termina_bien() => HiloDeVentana.EjecutarAsync(async () =>
    {
        var trabajo = NuevoTrabajo("pelicula.mkv");
        var copia = await EmpezarEnPausaAsync(trabajo);
        await Task.Delay(300);

        trabajo.PausarOReanudarCommand.Execute(null);
        await copia.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.False(trabajo.EnPausa);
        Assert.Equal(InfoBarSeverity.Success, trabajo.Severidad);
        Assert.Equal(TamanoArchivo, new FileInfo(Path.Combine(carpetas.Destino, "pelicula.mkv")).Length);
    });

    /// <summary>Con nombres largos y la copia en marcha, ningún texto se monta sobre otro ni sobre los botones.</summary>
    [Fact]
    public Task Nada_se_encima_en_la_ventanita_mientras_copia() => HiloDeVentana.EjecutarAsync(async () =>
    {
        var trabajo = NuevoTrabajo("Prison Break S5-E1 HD-720p con un nombre de archivo bastante largo para probar.mkv");
        var copia = await EmpezarEnPausaAsync(trabajo);
        var ventana = new VentanaCopia { DataContext = trabajo };
        var contenido = (FrameworkElement)ventana.Content;
        HiloDeVentana.VaciarCola(contenido);
        await Task.Delay(400);
        contenido.Measure(new Size(ventana.Width, double.PositiveInfinity));
        contenido.Arrange(new Rect(contenido.DesiredSize));
        contenido.UpdateLayout();

        var botones = HiloDeVentana.Descendientes(contenido).OfType<Wpf.Ui.Controls.Button>().Where(Visible).ToList();
        var textos = HiloDeVentana.Descendientes(contenido).OfType<TextBlock>()
            .Where(texto => Visible(texto) && texto.Text.Length > 0 && !DentroDe<System.Windows.Controls.Primitives.ButtonBase>(texto)
                && !DentroDe<Wpf.Ui.Controls.TitleBar>(texto))
            .ToList();
        var piezas = botones.Cast<FrameworkElement>().Concat(textos.Cast<FrameworkElement>()).Select(pieza => (pieza, Caja(pieza, contenido))).ToList();
        var encimados = (from a in piezas
                         from b in piezas
                         where a.pieza.GetHashCode() < b.pieza.GetHashCode() && a.Item2.IntersectsWith(b.Item2)
                               && !Rect.Intersect(a.Item2, b.Item2).IsEmpty && Rect.Intersect(a.Item2, b.Item2).Width * Rect.Intersect(a.Item2, b.Item2).Height > 1
                         select $"'{Nombre(a.pieza)}' con '{Nombre(b.pieza)}'").ToList();

        trabajo.CancelarCommand.Execute(null);
        await copia.WaitAsync(TimeSpan.FromSeconds(10));
        ventana.Close();

        Assert.True(botones.Count >= 3, $"Solo se ven {botones.Count} botones");
        Assert.Empty(encimados);
    });

    /// <summary>La ventana no se muestra en pantalla (IsVisible sería siempre falso): visible es tener tamaño y no estar oculto.</summary>
    private static bool Visible(FrameworkElement pieza)
    {
        for (DependencyObject? actual = pieza; actual is not null; actual = System.Windows.Media.VisualTreeHelper.GetParent(actual))
        {
            if (actual is UIElement elemento && elemento.Visibility != Visibility.Visible)
            {
                return false;
            }
        }

        return pieza.ActualWidth > 0 && pieza.ActualHeight > 0;
    }

    private static bool DentroDe<T>(DependencyObject pieza) where T : DependencyObject
    {
        for (var padre = System.Windows.Media.VisualTreeHelper.GetParent(pieza); padre is not null; padre = System.Windows.Media.VisualTreeHelper.GetParent(padre))
        {
            if (padre is T)
            {
                return true;
            }
        }

        return false;
    }

    private static Rect Caja(FrameworkElement pieza, System.Windows.Media.Visual raiz) => pieza.TransformToAncestor(raiz).TransformBounds(new Rect(0, 0, pieza.ActualWidth, pieza.ActualHeight));

    private static string Nombre(FrameworkElement pieza) => pieza switch
    {
        TextBlock texto => texto.Text,
        Wpf.Ui.Controls.Button boton => "botón " + (boton.Content as string ?? boton.ToolTip as string ?? "?"),
        _ => pieza.GetType().Name,
    };
}
