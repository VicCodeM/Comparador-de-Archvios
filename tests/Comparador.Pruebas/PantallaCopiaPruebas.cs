using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Comparador.App.ModelosVista;
using Comparador.App.Vistas;
using Comparador.Nucleo.Modelos;

namespace Comparador.Pruebas;

/// <summary>La pantalla de copia montada de verdad. WPF exige un solo hilo STA: todo se mide dentro del mismo.</summary>
public sealed class PantallaCopiaPruebas
{
    private const double AltoMinimoContenido = 760;

    /// <summary>
    /// Con varios archivos en curso, "Ahora mismo" tomaba todo el alto y la lista de terminados quedaba en cero
    /// (captura de Victor, 2026-10-08). En una pantalla 1080 maximizada (880) las dos listas y los botones deben verse;
    /// con la ventana baja (600) la página entera debe poder desplazarse hasta el final.
    /// </summary>
    [Fact]
    public void Con_muchos_archivos_en_curso_se_ven_los_terminados_los_botones_y_se_llega_al_final()
    {
        var medidas = EnHiloDeVentana(() => new[] { 880.0, 600.0 }.Select(alto => (alto, Medir(alto))).ToList());

        foreach (var (alto, (altoTerminados, finBotones, desplazable)) in medidas)
        {
            Assert.True(altoTerminados >= 150, $"Con {alto} px la lista de terminados mide {altoTerminados:0} px");
            Assert.True(finBotones <= alto, $"Con {alto} px los botones acaban en {finBotones:0} px, fuera de la pantalla");
            Assert.True(alto >= AltoMinimoContenido || desplazable > 0, $"Con {alto} px no hay desplazamiento para llegar al final");
        }
    }

    private static (double AltoTerminados, double FinBotones, double Desplazable) Medir(double alto)
    {
        var principal = new PrincipalModeloVista();
        var progreso = new ProgresoOperacion();
        progreso.IniciarFase("Copiando", 20, 20L * 700_000_000);
        for (var i = 0; i < 6; i++)
        {
            progreso.Empezar($@"Temporada 1\capitulo {i}.mkv", 700_000_000, "Copiando");
        }

        principal.Progreso.Seguir(progreso, principal.Terminados);
        principal.Terminados.Agregar([.. Enumerable.Range(0, 11).Select(i => new ArchivoTerminado(
            $@"Temporada 2\capitulo {i}.mkv", "", "", 700_000_000, ResultadoArchivo.Copiado, "Copiado", DateTime.Now))]);
        principal.OperacionActual = Operacion.Copiando;

        var vista = new VistaCopia { DataContext = principal };
        vista.Measure(new Size(1500, alto));
        vista.Arrange(new Rect(0, 0, 1500, alto));
        vista.UpdateLayout();
        var cancelar = Descendientes(vista).OfType<Wpf.Ui.Controls.Button>().First(boton => boton.Content as string == "Cancelar");
        var medida = (
            ((FrameworkElement)vista.FindName("ListaTerminados")).ActualHeight,
            cancelar.TranslatePoint(new Point(0, cancelar.ActualHeight), vista).Y,
            ((ScrollViewer)vista.FindName("Desplazamiento")).ScrollableHeight);
        principal.Progreso.Detener();

        return medida;
    }

    private static T EnHiloDeVentana<T>(Func<T> trabajo)
    {
        T resultado = default!;
        ExceptionDispatchInfo? fallo = null;
        var hilo = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    new Comparador.App.App().InitializeComponent();
                }

                resultado = trabajo();
            }
            catch (Exception error)
            {
                fallo = ExceptionDispatchInfo.Capture(error);
            }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();
        fallo?.Throw();

        return resultado;
    }

    private static IEnumerable<DependencyObject> Descendientes(DependencyObject raiz)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var hijo = VisualTreeHelper.GetChild(raiz, i);
            yield return hijo;
            foreach (var nieto in Descendientes(hijo))
            {
                yield return nieto;
            }
        }
    }
}
