using Comparador.App.Servicios;
using Microsoft.Win32;

namespace Comparador.Pruebas;

/// <summary>Lo que llega desde el Explorador o la línea de comandos.</summary>
public sealed class AppPruebas
{
    [Fact]
    public void Entiende_copiar_con_destino()
    {
        var pedido = LineaDeComandos.Interpretar(["copiar", @"C:\a.txt", @"C:\Fotos", "--destino", @"D:\Respaldo"]);

        Assert.NotNull(pedido);
        Assert.Equal([@"C:\a.txt", @"C:\Fotos"], pedido.Rutas);
        Assert.Equal((@"D:\Respaldo", false, false), (pedido.Destino, pedido.Mover, pedido.PreguntarDestino));
    }

    [Fact]
    public void Sin_destino_lo_pregunta_y_mover_se_respeta()
    {
        var pedido = LineaDeComandos.Interpretar(["mover", @"C:\a.txt"]);

        Assert.NotNull(pedido);
        Assert.Equal((null, true, true), (pedido.Destino, pedido.Mover, pedido.PreguntarDestino));
    }

    [Theory]
    [InlineData]
    [InlineData("abrir")]
    [InlineData("copiar")]
    public void Sin_un_pedido_valido_abre_la_ventana_normal(params string[] argumentos) =>
        Assert.Null(LineaDeComandos.Interpretar(argumentos));

    [Fact]
    public void Instala_y_quita_el_clic_derecho_sin_dejar_nada()
    {
        // Raíz aparte: la prueba no debe tocar el menú real (antes lo reescribía apuntando a testhost.exe).
        const string raiz = @"Software\Comparador.Pruebas\Classes";
        const string ejecutable = @"C:\Programas\Comparador\Comparador.App.exe";
        try
        {
            IntegracionExplorador.Instalar(raiz, ejecutable);
            Assert.True(IntegracionExplorador.EstaInstalada(raiz));
            using var comando = Registry.CurrentUser.OpenSubKey($@"{raiz}\Directory\Background\shell\Comparador.Pegar\command");
            Assert.Equal($"\"{ejecutable}\" pegar \"%V\"", comando?.GetValue(string.Empty) as string);

            IntegracionExplorador.Quitar(raiz);
            Assert.False(IntegracionExplorador.EstaInstalada(raiz));
            Assert.Null(Registry.CurrentUser.OpenSubKey($@"{raiz}\*\shell\Comparador.CopiarA"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Comparador.Pruebas", throwOnMissingSubKey: false);
        }
    }
}
