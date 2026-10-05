using Comparador.Nucleo.Ubicaciones;
using Wpf.Ui.Controls;

namespace Comparador.App.Servicios;

/// <summary>El icono de cada tipo de ubicación.</summary>
public static class IconosUbicacion
{
    public static SymbolRegular Para(TipoUbicacion tipo) => tipo switch
    {
        TipoUbicacion.Telefono => SymbolRegular.Phone24,
        TipoUbicacion.Red => SymbolRegular.Globe24,
        TipoUbicacion.Usb => SymbolRegular.UsbStick24,
        _ => SymbolRegular.HardDrive20,
    };
}
