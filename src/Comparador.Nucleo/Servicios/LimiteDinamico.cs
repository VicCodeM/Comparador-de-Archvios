namespace Comparador.Nucleo.Servicios;

/// <summary>
/// Cuántas copias pueden correr a la vez, con un límite que se puede subir o bajar en marcha. Bajarlo no corta
/// ninguna copia: simplemente las siguientes esperan hasta que haya menos en curso.
/// </summary>
public sealed class LimiteDinamico(int inicial)
{
    private readonly SemaphoreSlim permisos = new(inicial, int.MaxValue);
    private readonly Lock cerrojo = new();
    private int deuda;

    public int Actual { get; private set; } = inicial;

    public Task EsperarAsync(CancellationToken cancelacion) => permisos.WaitAsync(cancelacion);

    public void Liberar()
    {
        lock (cerrojo)
        {
            if (deuda > 0)
            {
                deuda--;
                return;
            }
        }

        permisos.Release();
    }

    public void Cambiar(int nuevo)
    {
        lock (cerrojo)
        {
            var diferencia = nuevo - Actual;
            Actual = nuevo;
            if (diferencia < 0)
            {
                deuda -= diferencia;
                return;
            }

            // Lo que se debía se cancela antes de dar permisos nuevos.
            var pagado = Math.Min(deuda, diferencia);
            deuda -= pagado;
            if (diferencia > pagado)
            {
                permisos.Release(diferencia - pagado);
            }
        }
    }
}
