using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Cinedex.Android.Views;

/// <summary>Chemin d'affiche → image décodée à la taille d'affichage (mémoire limitée sur téléphone), avec un petit cache.</summary>
public sealed class PosterConverter(int width) : IValueConverter
{
    public static PosterConverter Thumbnail { get; } = new(112);

    public static PosterConverter Large { get; } = new(360);

    private readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.Ordinal);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
        {
            return null;
        }

        if (_cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        if (_cache.Count > 300)
        {
            _cache.Clear();
        }

        try
        {
            using var stream = File.OpenRead(path);
            return _cache[path] = Bitmap.DecodeToWidth(stream, width);
        }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException)
        {
            return _cache[path] = null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
