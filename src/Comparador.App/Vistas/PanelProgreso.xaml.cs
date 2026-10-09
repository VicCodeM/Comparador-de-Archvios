using System.Windows;
using System.Windows.Controls;

namespace Comparador.App.Vistas;

public partial class PanelProgreso : UserControl
{
    /// <summary>
    /// La página de copia pinta la lista "Ahora mismo" por su cuenta, en una fila que se reparte la altura con la de
    /// terminados: dentro de este panel tomaba todo el alto y aplastaba los terminados a cero (2026-10-08).
    /// </summary>
    public static readonly DependencyProperty MostrarEnCursoProperty =
        DependencyProperty.Register(nameof(MostrarEnCurso), typeof(bool), typeof(PanelProgreso), new PropertyMetadata(true));

    public PanelProgreso() => InitializeComponent();

    public bool MostrarEnCurso
    {
        get => (bool)GetValue(MostrarEnCursoProperty);
        set => SetValue(MostrarEnCursoProperty, value);
    }
}
