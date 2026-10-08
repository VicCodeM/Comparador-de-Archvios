using System.Runtime.InteropServices;

namespace Comparador.Nucleo.Servicios;

/// <summary>
/// Mientras dura (using), Windows no se suspende: una copia larga no se corta a la mitad. Lleva la cuenta de las
/// operaciones abiertas: con dos copias a la vez, la primera que termina no debe devolverle la suspensión a la otra.
/// </summary>
public sealed class PrevencionSuspension : IDisposable
{
    private const uint Continuo = 0x80000000;
    private const uint SistemaRequerido = 0x00000001;
    private static readonly Lock Cerrojo = new();
    private static int abiertas;
    private bool liberada;

    private PrevencionSuspension()
    {
        lock (Cerrojo)
        {
            abiertas++;
            SetThreadExecutionState(Continuo | SistemaRequerido);
        }
    }

    public static PrevencionSuspension Activar() => new();

    public void Dispose()
    {
        lock (Cerrojo)
        {
            if (liberada)
            {
                return;
            }

            liberada = true;
            abiertas--;
            if (abiertas == 0)
            {
                SetThreadExecutionState(Continuo);
            }
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint estado);
}
