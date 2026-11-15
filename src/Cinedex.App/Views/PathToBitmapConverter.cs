using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Cinedex.App.Views;

/// <summary>
/// Chemin de fichier vers image, décodée à la largeur d'affichage et gardée en cache (image corrompue ignorée).
/// <para>
/// Le cache est borné en octets, non en nombre d'images : une vignette de liste pèse 40 Ko, une affiche de
/// l'économiseur d'écran près de 5 Mo, et les compter pareil laissait la mémoire dépendre de ce qu'on
/// regardait. La moins récemment lue sort la première, si bien que les vignettes de la liste affichée ne sont
/// pas chassées par quelques grandes images.
/// </para>
/// </summary>
public sealed class PathToBitmapConverter : IValueConverter
{
    /// <summary>Budget du cache. 256 Mo tiennent environ 6 000 vignettes de liste, ou 50 affiches plein écran.</summary>
    public const long MaxBytes = 256L * 1024 * 1024;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<(string Path, int Width), LinkedListNode<Entry>> Index = [];
    private static readonly LinkedList<Entry> Recent = new();

    /// <summary>
    /// Mémoire déclarée au ramasse-miettes pour chaque image vivante. Une image tient surtout de la mémoire
    /// non gérée : sans cela le ramasse-miettes ne voit qu'un petit objet et ne se presse pas de le libérer.
    /// </summary>
    private static readonly ConditionalWeakTable<Bitmap, Pressure> Pressures = [];

    private static long _bytes;

    public static PathToBitmapConverter Instance { get; } = new();

    /// <summary>Budget en vigueur ; <see cref="MaxBytes"/> sauf dans les tests, qui le resserrent pour le voir agir.</summary>
    public static long Budget { get; set; } = MaxBytes;

    /// <summary>Octets tenus par le cache en ce moment.</summary>
    public static long CachedBytes
    {
        get
        {
            lock (Gate)
            {
                return _bytes;
            }
        }
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path)
        {
            return null;
        }

        var width = parameter is string p && int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var w) ? w : 400;
        return TryGetCached(path, width, out var cached) ? cached : Load(path, width);
    }

    public static bool TryGetCached(string path, int width, out Bitmap? bitmap)
    {
        lock (Gate)
        {
            if (Index.TryGetValue((path, width), out var node))
            {
                // Une lecture remonte l'image en tête : c'est ce qui la protège de l'éviction.
                Recent.Remove(node);
                Recent.AddFirst(node);
                bitmap = node.Value.Bitmap;
                return true;
            }
        }

        bitmap = null;
        return false;
    }

    /// <summary>Décode l'image (ou la reprend du cache). Peut être appelé hors du fil d'interface.</summary>
    public static Bitmap? Load(string path, int width)
    {
        if (TryGetCached(path, width, out var cached))
        {
            return cached;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            Bitmap bitmap;
            using (var stream = File.OpenRead(path))
            {
                bitmap = Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality);
            }

            return Remember((path, width), bitmap);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException
                                       or UnauthorizedAccessException or NullReferenceException)
        {
            // NullReferenceException : c'est ce que lève le décodeur Skia d'Avalonia devant un fichier qui
            // n'est pas une image (téléchargement tronqué, fichier renommé). Une affiche illisible est une
            // affiche absente, pas une raison de tomber.
            return null;
        }
    }

    /// <summary>Oublie tout. Les images encore affichées restent valides : elles ne sont jamais libérées de force.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Index.Clear();
            Recent.Clear();
            _bytes = 0;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    private static Bitmap Remember((string Path, int Width) key, Bitmap bitmap)
    {
        var bytes = SizeOf(bitmap);
        lock (Gate)
        {
            if (Index.TryGetValue(key, out var existing))
            {
                // Deux décodages concurrents de la même image : le premier arrivé reste, l'autre est rendu.
                return existing.Value.Bitmap;
            }

            Pressures.Add(bitmap, new Pressure(bytes));
            Index[key] = Recent.AddFirst(new Entry(key, bitmap, bytes));
            _bytes += bytes;

            // Une image qui dépasse le budget à elle seule est gardée quand même, pour ne pas être redécodée
            // à chaque affichage : d'où le « plus d'une entrée ».
            while (_bytes > Budget && Recent.Count > 1 && Recent.Last is { } oldest)
            {
                // L'image évincée peut encore être à l'écran : elle est laissée au ramasse-miettes, qui la
                // voit maintenant à son vrai poids.
                Recent.RemoveLast();
                Index.Remove(oldest.Value.Key);
                _bytes -= oldest.Value.Bytes;
            }
        }

        return bitmap;
    }

    private static long SizeOf(Bitmap bitmap) => (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;

    private sealed record Entry((string Path, int Width) Key, Bitmap Bitmap, long Bytes);

    /// <summary>Rend au ramasse-miettes la pression déclarée, quand l'image à laquelle elle est attachée disparaît.</summary>
    private sealed class Pressure
    {
        private readonly long _bytes;

        public Pressure(long bytes)
        {
            _bytes = bytes;
            if (bytes > 0)
            {
                GC.AddMemoryPressure(bytes);
            }
        }

        ~Pressure()
        {
            if (_bytes > 0)
            {
                GC.RemoveMemoryPressure(_bytes);
            }
        }
    }
}
