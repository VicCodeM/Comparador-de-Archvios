using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Comparador.App.ModelosVista;
using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;
using MaterialDesignThemes.Wpf;

namespace Comparador.App.Convertidores;

public sealed class PasoAVisibilidadConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PasoApp paso && parameter is string objetivo && Enum.TryParse<PasoApp>(objetivo, out var esperado))
        {
            return paso == esperado ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class FiltroEsIgualConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is FiltroRevision actual && parameter is string paramStr && Enum.TryParse<FiltroRevision>(paramStr, out var filtroEsperado))
        {
            return actual == filtroEsperado;
        }

        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class TamanoFormatoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long tamano)
        {
            return Formatos.Tamano(tamano);
        }

        return "-";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class FechaFormatoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime fecha)
        {
            return fecha.ToString("yyyy-MM-dd HH:mm:ss");
        }

        return "-";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class EstadoAIconoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is EstadoElemento estado)
        {
            return estado switch
            {
                EstadoElemento.Coincide => PackIconKind.CheckCircleOutline,
                EstadoElemento.Falta => PackIconKind.PlusCircleOutline,
                EstadoElemento.Diferente => PackIconKind.FileEditOutline,
                EstadoElemento.Sobra => PackIconKind.DeleteOutline,
                EstadoElemento.SinAcceso => PackIconKind.LockOutline,
                _ => PackIconKind.AlertCircleOutline,
            };
        }

        return PackIconKind.HelpCircleOutline;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class EstadoABrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Verde = new(Color.FromRgb(46, 125, 50));
    private static readonly SolidColorBrush Azul = new(Color.FromRgb(25, 118, 210));
    private static readonly SolidColorBrush Naranja = new(Color.FromRgb(239, 108, 0));
    private static readonly SolidColorBrush Purpura = new(Color.FromRgb(123, 31, 162));
    private static readonly SolidColorBrush Rojo = new(Color.FromRgb(198, 40, 40));
    private static readonly SolidColorBrush Gris = new(Color.FromRgb(117, 117, 117));

    static EstadoABrushConverter()
    {
        Verde.Freeze();
        Azul.Freeze();
        Naranja.Freeze();
        Purpura.Freeze();
        Rojo.Freeze();
        Gris.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is EstadoElemento estado)
        {
            return estado switch
            {
                EstadoElemento.Coincide => Verde,
                EstadoElemento.Falta => Azul,
                EstadoElemento.Diferente => Naranja,
                EstadoElemento.Sobra => Purpura,
                EstadoElemento.SinAcceso or EstadoElemento.Error => Rojo,
                _ => Gris,
            };
        }

        return Gris;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
