using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Bobine.App.Views;

/// <summary>Charge une image locale réduite à la largeur d'affichage (mémoire bornée, image corrompue ignorée).</summary>
public sealed class PathToBitmapConverter : IValueConverter
{
    public static PathToBitmapConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
        {
            return null;
        }

        var width = parameter is string p && int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var w) ? w : 400;
        try
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, width);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
