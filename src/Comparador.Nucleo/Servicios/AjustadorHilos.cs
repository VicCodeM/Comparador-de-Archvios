namespace Comparador.Nucleo.Servicios;

/// <summary>
/// Busca, mientras copia, cuántas copias a la vez rinden más en ESTE equipo. Recuerda el mejor número que ha visto:
/// prueba a moverse un hilo; si rinde claramente más, ese pasa a ser el mejor y sigue en esa dirección; si no, vuelve
/// al mejor y la próxima vez prueba hacia el otro lado. Ningún número está atado al tipo de disco: el de partida es
/// solo una primera suposición y lo corrige la medición.
/// </summary>
public sealed class AjustadorHilos(LimiteDinamico limite, int minimo, int maximo)
{
    /// <summary>
    /// Por debajo de esta mejora la diferencia se toma como ruido. No puede ser grande: lejos del óptimo cada hilo
    /// suma poco en proporción y con un 10 % el ajustador se quedaba a medio camino. El ruido de la red lo absorben
    /// promediar dos tramos y comparar siempre contra el mejor visto (no contra el paso anterior).
    /// </summary>
    private const double MejoraSignificativa = 0.05;

    /// <summary>Tramos que se juntan antes de decidir: uno solo es demasiado ruidoso.</summary>
    private const int TramosPorDecision = 2;

    /// <summary>
    /// Un archivo cuenta como si moviera esta cantidad de bytes: abrirlo, crearlo, renombrarlo y ponerle fechas cuesta
    /// aunque pese poco. Sin esto, con miles de archivos pequeños el avance en bytes no diría nada.
    /// </summary>
    private const long CostoPorArchivo = 256 * 1024;

    /// <summary>Decisiones quieto en el mejor antes de volver a probar: al pasar de archivos grandes a pequeños, el óptimo cambia.</summary>
    private const int DecisionesAntesDeProbar = 3;

    private double mejorRendimiento;
    private int mejoresHilos;
    private int direccion = 1;
    private int decisionesQuieto;
    private int tramos;
    private double trabajoAcumulado;
    private double segundosAcumulados;

    /// <summary>Se llama cada pocos segundos con lo avanzado en ese tramo. Devuelve los hilos a usar desde ahora.</summary>
    public int Medir(long bytes, long archivos, TimeSpan tramo)
    {
        if (tramo <= TimeSpan.Zero)
        {
            return limite.Actual;
        }

        trabajoAcumulado += bytes + archivos * CostoPorArchivo;
        segundosAcumulados += tramo.TotalSeconds;
        if (++tramos < TramosPorDecision)
        {
            return limite.Actual;
        }

        var rendimiento = trabajoAcumulado / segundosAcumulados;
        (tramos, trabajoAcumulado, segundosAcumulados) = (0, 0, 0);

        return Decidir(rendimiento);
    }

    private int Decidir(double rendimiento)
    {
        var actual = limite.Actual;
        if (mejorRendimiento == 0 || rendimiento > mejorRendimiento * (1 + MejoraSignificativa))
        {
            (mejorRendimiento, mejoresHilos) = (rendimiento, actual);

            return Mover(actual + direccion);
        }

        if (actual != mejoresHilos)
        {
            // Probar aquí no rindió más: se vuelve al mejor y la próxima prueba irá hacia el otro lado.
            direccion = -direccion;

            return Mover(mejoresHilos);
        }

        // En el mejor: se actualiza su medida (lo que se copia puede haber cambiado) y de vez en cuando se prueba.
        mejorRendimiento = rendimiento;
        if (++decisionesQuieto < DecisionesAntesDeProbar)
        {
            return actual;
        }

        decisionesQuieto = 0;

        return Mover(actual + direccion);
    }

    private int Mover(int deseado)
    {
        var siguiente = Math.Clamp(deseado, minimo, maximo);
        if (siguiente == limite.Actual && deseado != limite.Actual)
        {
            // En el tope: se prueba hacia el otro lado.
            direccion = -direccion;
            siguiente = Math.Clamp(limite.Actual + direccion, minimo, maximo);
        }

        limite.Cambiar(siguiente);

        return siguiente;
    }
}
