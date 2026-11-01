using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Cinedex.App.Views;

/// <summary>
/// Charge une image locale réduite à la largeur d'affichage (mémoire bornée, image corrompue ignorée).
/// Les vignettes déjà décodées sont gardées en cache : faire défiler une grille de milliers d'affiches
/// ne relit pas le disque à chaque passage.
/// </summary>
public sealed class PathToBitmapConverter : IValueConverter
{
    private const int MaxCached = 600;

    private static readonly ConcurrentDictionary<(string Path, int Width), Bitmap> Cache = new();
    private static readonly ConcurrentQueue<(string Path, int Width)> Order = new();

    public static PathToBitmapConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path)
        {
            return null;
        }

        var width = parameter is string p && int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var w) ? w : 400;
        var key = (path, width);
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality);
            if (Cache.TryAdd(key, bitmap))
            {
                Order.Enqueue(key);
                while (Order.Count > MaxCached && Order.TryDequeue(out var old))
                {
                    // Les bitmaps retirés du cache peuvent encore être affichés : on les laisse au ramasse-miettes.
                    Cache.TryRemove(old, out _);
                }
            }

            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
