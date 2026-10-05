using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Comparador.App.Servicios;
using Comparador.Nucleo.Modelos;
using Comparador.Nucleo.Servicios;
using Wpf.Ui.Controls;

namespace Comparador.App.Convertidores;

/// <summary>Base de los convertidores de un solo sentido: la pantalla solo muestra, nunca escribe de vuelta.</summary>
public abstract class ConvertidorDeIda : IValueConverter
{
    public abstract object Convert(object? value, Type targetType, object? parameter, CultureInfo culture);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Visible si el valor (un enum) es igual al parámetro; con "!" delante, al revés.</summary>
public sealed class IgualAVisibleConverter : ConvertidorDeIda
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var texto = parameter as string ?? string.Empty;
        var negado = texto.StartsWith('!');
        var igual = string.Equals(value?.ToString(), negado ? texto[1..] : texto, StringComparison.Ordinal);

        return igual != negado ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>Visible si es true; con parámetro "!" visible si es false.</summary>
public sealed class BoolAVisibleConverter : ConvertidorDeIda
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true != (parameter as string == "!") ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Visible si el número es mayor que cero (o la lista tiene elementos).</summary>
public sealed class HayAlgoAVisibleConverter : ConvertidorDeIda
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int cuantos && cuantos > 0 ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class TamanoConverter : ConvertidorDeIda
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long tamano ? Formatos.Tamano(tamano) : string.Empty;
}

public sealed class FechaConverter : ConvertidorDeIda
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime fecha && fecha != default ? fecha.ToString("dd/MM/yyyy HH:mm") : string.Empty;
}

public sealed class EstadoTextoConverter : ConvertidorDeIda
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is EstadoElemento estado ? ExportadorReporte.NombreEstado(estado) : string.Empty;
}

public sealed class EstadoIconoConverter : ConvertidorDeIda
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        EstadoElemento.Coincide => SymbolRegular.CheckmarkCircle24,
        EstadoElemento.Falta => SymbolRegular.AddCircle24,
        EstadoElemento.Diferente => SymbolRegular.DocumentEdit24,
        EstadoElemento.Sobra => SymbolRegular.SubtractCircle24,
        EstadoElemento.SinAcceso => SymbolRegular.LockClosed24,
        _ => SymbolRegular.ErrorCircle24,
    };
}

public sealed class TemaTextoConverter : ConvertidorDeIda
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TemaApp.Claro => "Claro",
        TemaApp.Oscuro => "Oscuro",
        _ => "Usar el de Windows",
    };
}

public sealed class TipoIconoConverter : ConvertidorDeIda
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Nucleo.Ubicaciones.TipoUbicacion tipo ? IconosUbicacion.Para(tipo) : SymbolRegular.Folder24;
}

/// <summary>Para botones de filtro: marcado si el valor es el del parámetro; al marcarlo, fija ese valor.</summary>
public sealed class EnumMarcadoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter as string, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string nombre ? Enum.Parse(targetType, nombre) : Binding.DoNothing;
}

public sealed class NoEsConverter : ConvertidorDeIda
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
