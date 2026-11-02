using System.Globalization;
using Avalonia.Data.Converters;
using Cinedex.Core.Localization;

namespace Cinedex.App.Views;

/// <summary>
/// Comme <c>StringFormat</c>, mais le format (en français dans le XAML) est d'abord traduit :
/// <c>Converter={x:Static views:TrFormatConverter.Instance}, ConverterParameter='Prêté à {0}'</c>.
/// </summary>
public sealed class TrFormatConverter : IValueConverter
{
    public static TrFormatConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? null : parameter is string format ? Tr.F(format.StartsWith("{}", StringComparison.Ordinal) ? format[2..] : format, value) : value.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
