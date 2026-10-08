using System.Reflection;
using Comparador.App.ModelosVista;
using Comparador.Nucleo.Modelos;

namespace Comparador.Pruebas;

/// <summary>El repintado del avance nunca debe tumbar la app.</summary>
public sealed class ProgresoPruebas
{
    /// <summary>
    /// En pausa la velocidad cae casi a cero: el tiempo restante salía infinito, TimeSpan se desbordaba y Espejo se
    /// cerraba de golpe (Visor de eventos, 2026-10-08).
    /// </summary>
    [Fact]
    public async Task Con_velocidad_casi_cero_no_se_cae_y_no_inventa_un_tiempo()
    {
        var progreso = new ProgresoOperacion();
        progreso.IniciarFase("Copiando", totalElementos: 1, totalBytes: long.MaxValue / 4);
        var vista = new ProgresoModeloVista();
        vista.Seguir(progreso);

        await Task.Delay(TimeSpan.FromMilliseconds(600));
        progreso.SumarBytes(1);
        typeof(ProgresoModeloVista).GetMethod("Actualizar", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vista, null);

        Assert.Equal("Calculando el tiempo restante...", vista.Restante);
    }
}
