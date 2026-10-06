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
        var yaEstaba = IntegracionExplorador.EstaInstalada();
        try
        {
            IntegracionExplorador.Instalar();
            Assert.True(IntegracionExplorador.EstaInstalada());
            using var comando = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Directory\Background\shell\Comparador.Pegar\command");
            Assert.Contains("pegar \"%V\"", comando?.GetValue(string.Empty) as string);

            IntegracionExplorador.Quitar();
            Assert.False(IntegracionExplorador.EstaInstalada());
            Assert.Null(Registry.CurrentUser.OpenSubKey(@"Software\Classes\*\shell\Comparador.CopiarA"));
        }
        finally
        {
            if (yaEstaba)
            {
                IntegracionExplorador.Instalar();
            }
        }
    }
}
