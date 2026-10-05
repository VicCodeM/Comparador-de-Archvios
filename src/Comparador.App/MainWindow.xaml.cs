using Comparador.App.ModelosVista;
using Comparador.App.Servicios;

namespace Comparador.App;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Apariencia.Aplicar(((PrincipalModeloVista)DataContext).Tema);
    }
}
