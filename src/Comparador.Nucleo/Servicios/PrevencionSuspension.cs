using System.Runtime.InteropServices;

namespace Comparador.Nucleo.Servicios;

/// <summary>Mientras dura (using), Windows no se suspende: una copia larga no se corta a la mitad.</summary>
public sealed class PrevencionSuspension : IDisposable
{
    private const uint Continuo = 0x80000000;
    private const uint SistemaRequerido = 0x00000001;

    private PrevencionSuspension() => SetThreadExecutionState(Continuo | SistemaRequerido);

    public static PrevencionSuspension Activar() => new();

    public void Dispose() => SetThreadExecutionState(Continuo);

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint estado);
}
