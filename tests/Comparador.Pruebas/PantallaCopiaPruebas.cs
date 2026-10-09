using System.Windows;
using System.Windows.Controls;
using Comparador.App.ModelosVista;
using Comparador.App.Vistas;
using Comparador.Nucleo.Modelos;

namespace Comparador.Pruebas;

/// <summary>La pantalla de copia montada de verdad, en el hilo de ventana compartido.</summary>
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
        var medidas = HiloDeVentana.Ejecutar(() => new[] { 880.0, 600.0 }.Select(alto => (alto, Medir(alto, enCurso: 6, terminados: 11))).ToList());

        foreach (var (alto, (altoTerminados, _, finBotones, desplazable)) in medidas)
        {
            Assert.True(altoTerminados >= 150, $"Con {alto} px la lista de terminados mide {altoTerminados:0} px");
            Assert.True(finBotones <= alto, $"Con {alto} px los botones acaban en {finBotones:0} px, fuera de la pantalla");
            Assert.True(alto >= AltoMinimoContenido || desplazable > 0, $"Con {alto} px no hay desplazamiento para llegar al final");
        }
    }

    /// <summary>
    /// Al empezar la copia no hay nada en curso ni terminado todavía: las dos listas deben verse igual, vacías, y no
    /// aparecer de golpe cuando llega el primer archivo (Victor, 2026-10-08).
    /// </summary>
    [Fact]
    public void Al_empezar_la_copia_ya_se_ven_las_dos_listas_aunque_esten_vacias()
    {
        var (altoTerminados, altoEnCurso, _, _) = HiloDeVentana.Ejecutar(() => Medir(880, enCurso: 0, terminados: 0));

        Assert.True(altoEnCurso >= 150, $"La lista de lo que se copia mide {altoEnCurso:0} px");
        Assert.True(altoTerminados >= 150, $"La lista de terminados mide {altoTerminados:0} px");
    }

    private static (double AltoTerminados, double AltoEnCurso, double FinBotones, double Desplazable) Medir(double alto, int enCurso, int terminados)
    {
        var principal = new PrincipalModeloVista();
        var progreso = new ProgresoOperacion();
        progreso.IniciarFase("Copiando", 20, 20L * 700_000_000);
        for (var i = 0; i < enCurso; i++)
        {
            progreso.Empezar($@"Temporada 1\capitulo {i}.mkv", 700_000_000, "Copiando");
        }

        principal.Progreso.Seguir(progreso, principal.Terminados);
        principal.Terminados.Agregar([.. Enumerable.Range(0, terminados).Select(i => new ArchivoTerminado(
            $@"Temporada 2\capitulo {i}.mkv", "", "", 700_000_000, ResultadoArchivo.Copiado, "Copiado", DateTime.Now))]);
        principal.OperacionActual = Operacion.Copiando;

        var vista = new VistaCopia { DataContext = principal };
        HiloDeVentana.VaciarCola(vista);
        vista.Measure(new Size(1500, alto));
        vista.Arrange(new Rect(0, 0, 1500, alto));
        vista.UpdateLayout();
        var cancelar = HiloDeVentana.Descendientes(vista).OfType<Wpf.Ui.Controls.Button>().First(boton => boton.Content as string == "Cancelar");
        var medida = (
            ((FrameworkElement)vista.FindName("ListaTerminados")).ActualHeight,
            ((FrameworkElement)vista.FindName("ListaEnCurso")).ActualHeight,
            cancelar.TranslatePoint(new Point(0, cancelar.ActualHeight), vista).Y,
            ((ScrollViewer)vista.FindName("Desplazamiento")).ScrollableHeight);
        principal.Progreso.Detener();

        return medida;
    }
}
