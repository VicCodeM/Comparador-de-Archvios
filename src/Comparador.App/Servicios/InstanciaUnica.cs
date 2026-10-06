using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;

namespace Comparador.App.Servicios;

/// <summary>
/// Una sola app abierta a la vez. Con varios archivos elegidos en el Explorador, Windows abre la app una vez por
/// archivo: las que llegan después le pasan su pedido a la primera y se cierran. La primera junta los pedidos que
/// llegan casi juntos en uno solo, para preguntar el destino una vez y copiar todo en una sola cola.
/// </summary>
public sealed class InstanciaUnica : IDisposable
{
    /// <summary>
    /// Distinto para la app abierta como administrador: Windows no deja que una apertura normal (la del Explorador)
    /// le hable a una de administrador. Con un solo nombre, la del Explorador fallaba y se cerraba sin mostrar nada.
    /// </summary>
    private static readonly string Nombre = "VMSofts.ComparadorArchivos" + (Elevacion.EsAdministrador() ? ".Admin" : string.Empty);
    private static readonly TimeSpan EsperaParaJuntar = TimeSpan.FromMilliseconds(500);

    private readonly Mutex? unica;
    private readonly CancellationTokenSource fin = new();
    private readonly List<PedidoExterno> pendientes = [];
    private readonly Lock cerrojo = new();
    private Timer? juntar;

    private InstanciaUnica(Mutex? unica) => this.unica = unica;

    /// <summary>Sin otra app a la que hablarle: esta trabaja por su cuenta y no escucha a nadie.</summary>
    private bool PorSuCuenta => unica is null;

    /// <summary>
    /// La instancia que debe trabajar, o null si ya había otra y el pedido se le entregó. Si la entrega falla, esta
    /// apertura trabaja por su cuenta: nunca se cierra en silencio dejando al usuario sin ventana.
    /// </summary>
    public static InstanciaUnica? Tomar(IReadOnlyList<string> argumentos)
    {
        var unica = new Mutex(initiallyOwned: true, Nombre, out var esLaPrimera);
        if (esLaPrimera)
        {
            return new InstanciaUnica(unica);
        }

        unica.Dispose();

        return Enviar(argumentos) ? null : new InstanciaUnica(unica: null);
    }

    /// <summary>
    /// Escucha pedidos de otras aperturas y los entrega (juntados) en el hilo de la ventana. Una apertura sin pedido
    /// (desde el menú Inicio) trae al frente la ventana principal.
    /// </summary>
    public void Escuchar(Action<PedidoExterno> alRecibir, Action alAbrirSinPedido)
    {
        if (!PorSuCuenta)
        {
            _ = Task.Run(() => EscucharAsync(alRecibir, alAbrirSinPedido));
        }
    }

    private async Task EscucharAsync(Action<PedidoExterno> alRecibir, Action alAbrirSinPedido)
    {
        while (!fin.IsCancellationRequested)
        {
            try
            {
                await using var tuberia = new NamedPipeServerStream(Nombre, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await tuberia.WaitForConnectionAsync(fin.Token);
                var argumentos = await JsonSerializer.DeserializeAsync<List<string>>(tuberia, cancellationToken: fin.Token) ?? [];
                // "pegar" lee el portapapeles, que Windows solo deja leer desde el hilo de la ventana.
                var interpretado = await Application.Current.Dispatcher.InvokeAsync(() => LineaDeComandos.Interpretar(argumentos));
                if (interpretado is { } pedido)
                {
                    Recibir(pedido, alRecibir);
                }
                else
                {
                    await Application.Current.Dispatcher.InvokeAsync(alAbrirSinPedido);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
            {
                // Una apertura que se cerró a medias (o sin permiso) no debe tumbar la escucha de las siguientes.
            }
        }
    }

    /// <summary>Los pedidos que preguntan destino se juntan medio segundo: son los archivos de una misma selección.</summary>
    private void Recibir(PedidoExterno pedido, Action<PedidoExterno> alRecibir)
    {
        if (!pedido.PreguntarDestino)
        {
            Application.Current.Dispatcher.InvokeAsync(() => alRecibir(pedido));
            return;
        }

        lock (cerrojo)
        {
            pendientes.Add(pedido);
            juntar?.Dispose();
            juntar = new Timer(_ => EntregarJuntos(alRecibir), null, EsperaParaJuntar, Timeout.InfiniteTimeSpan);
        }
    }

    private void EntregarJuntos(Action<PedidoExterno> alRecibir)
    {
        List<PedidoExterno> lote;
        lock (cerrojo)
        {
            lote = [.. pendientes];
            pendientes.Clear();
        }

        foreach (var grupo in lote.GroupBy(pedido => pedido.Mover))
        {
            var juntos = new PedidoExterno(grupo.SelectMany(pedido => pedido.Rutas).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), null, grupo.Key, true);
            Application.Current.Dispatcher.InvokeAsync(() => alRecibir(juntos));
        }
    }

    /// <summary>Lo que llegó en la primera apertura también pasa por aquí, para juntarse con las que vengan.</summary>
    public void RecibirPropio(PedidoExterno pedido, Action<PedidoExterno> alRecibir) => Recibir(pedido, alRecibir);

    /// <summary>True si la otra app recibió el pedido.</summary>
    private static bool Enviar(IReadOnlyList<string> argumentos)
    {
        try
        {
            using var tuberia = new NamedPipeClientStream(".", Nombre, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            tuberia.Connect(TimeSpan.FromSeconds(3));
            JsonSerializer.Serialize(tuberia, argumentos);

            return true;
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        fin.Cancel();
        juntar?.Dispose();
        unica?.ReleaseMutex();
        unica?.Dispose();
    }
}
