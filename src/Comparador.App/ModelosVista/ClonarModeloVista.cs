using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Comparador.App.Servicios;
using Comparador.Nucleo.Clonacion;
using Comparador.Nucleo.Servicios;
using Comparador.Nucleo.Ubicaciones;
using Wpf.Ui.Controls;

namespace Comparador.App.ModelosVista;

/// <summary>Una fila de las listas de origen o destino: un disco, una de sus particiones o un archivo de imagen.</summary>
public sealed record OpcionClon(ExtremoClon Extremo, string Titulo, string Detalle, SymbolRegular Icono, bool EsParticion, string? Bloqueo)
{
    public bool Disponible => Bloqueo is null;
}

/// <summary>
/// La pantalla Clonar: discos y particiones conectados, o imágenes (también las de Raspberry Pi), como origen y como
/// destino. Las reglas de seguridad se revisan en vivo; clonar abre otro Espejo como administrador con su ventanita.
/// </summary>
public sealed partial class ClonarModeloVista(AvisosModeloVista avisos) : ObservableObject
{
    private const string FiltroImagenes = "Imágenes de disco (*.img, *.xz, *.gz, *.zip, *.iso)|*.img;*.xz;*.gz;*.zip;*.iso|Todos los archivos|*.*";
    private const int CanceladoPorElUsuario = 1223;

    private IReadOnlyList<DiscoFisico> discos = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problemas), nameof(Resumen))]
    [NotifyCanExecuteChangedFor(nameof(ClonarCommand))]
    private OpcionClon? origen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problemas), nameof(Resumen))]
    [NotifyCanExecuteChangedFor(nameof(ClonarCommand))]
    private OpcionClon? destino;

    [ObservableProperty] private bool verificar = true;

    public ObservableCollection<OpcionClon> Origenes { get; } = [];

    public ObservableCollection<OpcionClon> Destinos { get; } = [];

    public IReadOnlyList<string> Problemas => Origen is null || Destino is null
        ? []
        : ReglasClon.Revisar(Origen.Extremo, Destino.Extremo, discos, LargoOrigen());

    public string Resumen => Origen is null || Destino is null
        ? "Elige qué clonar (izquierda) y dónde (derecha)."
        : Destino.Extremo is ExtremoClon.DeImagen
            ? $"Se guardará {Origen.Extremo.Describir(discos)} como imagen en {Destino.Titulo}."
            : $"Se copiará {Origen.Extremo.Describir(discos)} sobre {Destino.Extremo.Describir(discos)}. Todo lo que tenga se borrará.";

    [RelayCommand]
    private void Actualizar()
    {
        discos = ListadoDiscos.Leer();
        Rellenar(Origenes, esDestino: false);
        Rellenar(Destinos, esDestino: true);
        Origen = Volver(Origenes, Origen);
        Destino = Volver(Destinos, Destino);
    }

    [RelayCommand]
    private void ElegirImagenOrigen()
    {
        if (Escritorio.ElegirArchivo("Imagen que se va a grabar", FiltroImagenes) is { } ruta)
        {
            Origen = AgregarImagen(Origenes, ruta, "Imagen de origen");
        }
    }

    [RelayCommand]
    private void ElegirImagenDestino()
    {
        var nombre = Origen is null ? "imagen.img" : $"{Path.GetFileNameWithoutExtension(Origen.Titulo)}.img";
        if (Escritorio.ElegirDondeGuardar("Guardar como imagen", nombre, "Imagen de disco (*.img)|*.img") is { } ruta)
        {
            Destino = AgregarImagen(Destinos, ruta, "Se guardará aquí");
        }
    }

    private bool PuedeClonar() => Origen is not null && Destino is not null && Problemas.Count == 0;

    [RelayCommand(CanExecute = nameof(PuedeClonar))]
    private void Clonar()
    {
        if (Origen is null || Destino is null || !Dialogos.ConfirmarClonado(Resumen, borraDatos: Destino.Extremo is not ExtremoClon.DeImagen))
        {
            return;
        }

        var argumentos = $"clonar \"{Origen.Extremo.Texto}\" \"{Destino.Extremo.Texto}\"{(Verificar ? string.Empty : " --sin-verificar")}";
        try
        {
            // Escribir en un disco entero exige administrador; el resto de Espejo sigue sin serlo (ve las unidades de red).
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, argumentos) { UseShellExecute = true, Verb = "runas" });
        }
        catch (Win32Exception error) when (error.NativeErrorCode == CanceladoPorElUsuario)
        {
            avisos.Advertir("No se clonó: Windows necesita tu permiso de administrador para escribir en un disco.");
        }
    }

    private long? LargoOrigen()
    {
        try
        {
            return Origen is null ? null : MotorClon.LargoDe(Origen.Extremo, discos);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void Rellenar(ObservableCollection<OpcionClon> lista, bool esDestino)
    {
        var imagen = lista.FirstOrDefault(opcion => opcion.Extremo is ExtremoClon.DeImagen);
        lista.Clear();
        if (imagen is not null)
        {
            lista.Add(imagen);
        }

        foreach (var disco in discos)
        {
            lista.Add(new OpcionClon(new ExtremoClon.DeDisco(disco.Numero), disco.Titulo, disco.Detalle, IconoDe(disco), false, BloqueoDe(disco, esDestino)));
            foreach (var particion in disco.Particiones)
            {
                lista.Add(new OpcionClon(new ExtremoClon.DeParticion(disco.Numero, particion.Numero), particion.Titulo, particion.Detalle,
                    SymbolRegular.DataPie24, true, BloqueoDe(disco, esDestino)));
            }
        }
    }

    private static string? BloqueoDe(DiscoFisico disco, bool esDestino) => disco switch
    {
        { TieneMedio: false } => "Sin tarjeta o sin medio",
        { EsDeWindows: true } when esDestino => "Aquí está Windows: no se puede escribir",
        _ => null,
    };

    private static SymbolRegular IconoDe(DiscoFisico disco) => disco.Bus switch
    {
        BusDisco.Usb => SymbolRegular.UsbStick24,
        BusDisco.Tarjeta => SymbolRegular.Storage24,
        _ => SymbolRegular.Server24,
    };

    private static OpcionClon AgregarImagen(ObservableCollection<OpcionClon> lista, string ruta, string detalle)
    {
        var anterior = lista.FirstOrDefault(opcion => opcion.Extremo is ExtremoClon.DeImagen);
        if (anterior is not null)
        {
            lista.Remove(anterior);
        }

        var imagen = new OpcionClon(new ExtremoClon.DeImagen(ruta), Path.GetFileName(ruta), $"{detalle} · {Path.GetDirectoryName(ruta)}", SymbolRegular.SaveCopy24, false, null);
        lista.Insert(0, imagen);

        return imagen;
    }

    /// <summary>Al releer los discos se conserva la elección si sigue existiendo y se puede usar.</summary>
    private static OpcionClon? Volver(IEnumerable<OpcionClon> lista, OpcionClon? elegida) =>
        elegida is null ? null : lista.FirstOrDefault(opcion => opcion.Extremo == elegida.Extremo && opcion.Disponible);
}
