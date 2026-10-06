using System.Collections.ObjectModel;
using System.Windows;
using Comparador.App.ModelosVista;
using Comparador.App.Vistas;
using Comparador.Nucleo.Servicios;

namespace Comparador.App.Servicios;

/// <summary>
/// Las copias lanzadas desde el Explorador, una detrás de otra: dos copias a la vez sobre los mismos discos se
/// estorban y van más lentas que en fila. Cada una tiene su ventanita; las que esperan dicen que están en cola.
/// </summary>
public sealed class ColaDeCopias(AccionesArchivo acciones)
{
    private readonly Queue<TrabajoCopiaModeloVista> esperando = new();
    private TrabajoCopiaModeloVista? actual;

    public ObservableCollection<TrabajoCopiaModeloVista> Trabajos { get; } = [];

    public void Agregar(PedidoCopia pedido)
    {
        var trabajo = new TrabajoCopiaModeloVista(pedido, Configuracion.Cargar(), acciones);
        trabajo.Termino += AlTerminarTrabajo;
        Trabajos.Add(trabajo);
        new VentanaCopia { DataContext = trabajo }.Show();
        esperando.Enqueue(trabajo);
        if (actual is null)
        {
            Siguiente();
        }
    }

    private void Siguiente()
    {
        if (!esperando.TryDequeue(out var trabajo))
        {
            actual = null;
            return;
        }

        actual = trabajo;
        _ = trabajo.EjecutarAsync();
    }

    private void AlTerminarTrabajo(TrabajoCopiaModeloVista trabajo)
    {
        Trabajos.Remove(trabajo);
        // La acción del equipo (suspender, apagar) espera a que no quede nada en la cola.
        if (esperando.Count == 0 && trabajo.AlTerminar is AlTerminar.Suspender or AlTerminar.Apagar)
        {
            AccionAlTerminar.Ejecutar(trabajo.AlTerminar);
        }

        Application.Current.Dispatcher.InvokeAsync(Siguiente);
    }
}
