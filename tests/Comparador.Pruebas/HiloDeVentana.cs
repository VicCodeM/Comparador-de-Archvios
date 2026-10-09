using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Comparador.Pruebas;

/// <summary>
/// Un solo hilo STA, vivo para todas las pruebas de pantalla: los recursos (estilos, pinceles) quedan atados al hilo
/// que los creó y otra prueba en otro hilo fallaría al tocarlos. Solo carga los estilos, nunca la App de Espejo: crear
/// la App y arrancar el hilo ejecuta su arranque real, que escribió en el registro de Victor el arranque con Windows
/// apuntando a testhost.exe y abrió avisos en su pantalla (2026-10-08). Las pruebas no tocan nada real del usuario.
/// </summary>
internal static class HiloDeVentana
{
    private static readonly Lazy<Dispatcher> Despachador = new(() =>
    {
        Dispatcher? despachador = null;
        using var listo = new ManualResetEventSlim();
        var hilo = new Thread(() =>
        {
            var recursos = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }.Resources.MergedDictionaries;
            recursos.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
            recursos.Add(new Wpf.Ui.Markup.ControlsDictionary());
            recursos.Add((ResourceDictionary)Application.LoadComponent(new Uri("/Espejo;component/Vistas/Estilos.xaml", UriKind.Relative)));
            despachador = Dispatcher.CurrentDispatcher;
            listo.Set();
            Dispatcher.Run();
        }) { IsBackground = true };
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        listo.Wait();

        return despachador!;
    });

    public static T Ejecutar<T>(Func<T> trabajo) => Despachador.Value.Invoke(trabajo);

    public static Task EjecutarAsync(Func<Task> trabajo) => Despachador.Value.InvokeAsync(trabajo).Task.Unwrap();

    /// <summary>Los enlaces se aplican en la cola del hilo: sin vaciarla se mediría la pantalla sin sus valores reales.</summary>
    public static void VaciarCola(DispatcherObject objeto) => objeto.Dispatcher.Invoke(DispatcherPriority.Background, () => { });

    public static IEnumerable<DependencyObject> Descendientes(DependencyObject raiz)
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
