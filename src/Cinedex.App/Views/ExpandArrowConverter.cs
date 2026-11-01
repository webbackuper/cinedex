using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Cinedex.App.Views;

/// <summary>Flèche d'un sous-menu : vers le bas quand il est ouvert, vers la droite quand il est fermé.</summary>
public sealed class ExpandArrowConverter : IValueConverter
{
    public static ExpandArrowConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new RotateTransform(value is true ? 0 : -90);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
