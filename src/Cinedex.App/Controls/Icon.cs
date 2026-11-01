using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Cinedex.App.Controls;

/// <summary>
/// Icône monochrome au trait, colorée par le premier plan courant (donc lisible en thème clair comme sombre).
/// Deux sources, même style : les icônes Buddha (images, utilisées comme masque) et des tracés dessinés
/// pour Cinedex dans le même esprit (trait fin, extrémités arrondies, grille 24×24).
/// </summary>
public sealed class Icon : Control
{
    public static readonly StyledProperty<string?> KindProperty =
        AvaloniaProperty.Register<Icon, string?>(nameof(Kind));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    // Icônes du pack Buddha (Assets/Icons/*.png, traits noirs sur fond transparent).
    private static readonly HashSet<string> BitmapKinds = new(StringComparer.Ordinal)
    {
        "about", "add", "database", "default", "delete", "edit", "exit", "export", "filter", "harddisk",
        "insert", "options", "play", "print", "random", "search", "sort", "statistics", "view",
    };

    // Icônes dessinées pour Cinedex, grille 24×24, trait de 1,6.
    private static readonly Dictionary<string, string> PathKinds = new(StringComparer.Ordinal)
    {
        ["film"] = "M4 3.5 H20 V20.5 H4 Z M8 3.5 V20.5 M16 3.5 V20.5 M4 8 H8 M4 12 H8 M4 16 H8 M16 8 H20 M16 12 H20 M16 16 H20",
        ["moon"] = "M19.5 14.5 A 8 8 0 1 1 9.5 4.5 A 6.5 6.5 0 0 0 19.5 14.5 Z",
        ["tv"] = "M3 7.5 H21 V18.5 H3 Z M8 21 H16 M8.5 3.5 L12 7 L15.5 3.5",
        ["heart"] = "M12 20 C 7 16.5 3.5 13 3.5 9 A 4.3 4.3 0 0 1 12 7 A 4.3 4.3 0 0 1 20.5 9 C 20.5 13 17 16.5 12 20 Z",
        ["eye"] = "M2.5 12 C 5 7.5 8.3 5.5 12 5.5 C 15.7 5.5 19 7.5 21.5 12 C 19 16.5 15.7 18.5 12 18.5 C 8.3 18.5 5 16.5 2.5 12 Z M15 12 A 3 3 0 1 1 9 12 A 3 3 0 1 1 15 12 Z",
        ["check"] = "M4.5 12.5 L9.5 17.5 L19.5 6.5",
        ["sync"] = "M19.5 11 A 7.5 7.5 0 0 0 6 6.3 L 4.5 8 M4.5 3.5 V8 H9 M4.5 13 A 7.5 7.5 0 0 0 18 17.7 L 19.5 16 M19.5 20.5 V16 H15",
        ["cloud"] = "M7 18.5 A 4.5 4.5 0 0 1 6.6 9.5 A 6 6 0 0 1 17.8 8.6 A 4.5 4.5 0 0 1 17.5 18.5 Z M12 10.5 V16 M9.5 13.5 L12 16 L14.5 13.5",
        ["folder"] = "M3 6.5 V18 A1.5 1.5 0 0 0 4.5 19.5 H19.5 A1.5 1.5 0 0 0 21 18 V9 A1.5 1.5 0 0 0 19.5 7.5 H11 L9 5 H4.5 A1.5 1.5 0 0 0 3 6.5 Z",
        ["folder-plus"] = "M3 6.5 V18 A1.5 1.5 0 0 0 4.5 19.5 H19.5 A1.5 1.5 0 0 0 21 18 V9 A1.5 1.5 0 0 0 19.5 7.5 H11 L9 5 H4.5 A1.5 1.5 0 0 0 3 6.5 Z M12 10.5 V16.5 M9 13.5 H15",
        ["grid"] = "M4 4 H10 V10 H4 Z M14 4 H20 V10 H14 Z M4 14 H10 V20 H4 Z M14 14 H20 V20 H14 Z",
        ["list"] = "M9 6 H20 M9 12 H20 M9 18 H20 M4.5 6 H5 M4.5 12 H5 M4.5 18 H5",
        ["tag"] = "M3.5 3.5 H11 L20.5 13 L13 20.5 L3.5 11 Z M8 8 H8.5",
        ["loan"] = "M14 4 H20 V10 M20 4 L11.5 12.5 M18 14 V18.5 A1.5 1.5 0 0 1 16.5 20 H5.5 A1.5 1.5 0 0 1 4 18.5 V7.5 A1.5 1.5 0 0 1 5.5 6 H10",
        ["alert"] = "M12 3.5 L21.5 20 H2.5 Z M12 9.5 V14 M12 17 V17.5",
        ["document"] = "M6 2.5 H14 L18.5 7 V21.5 H6 Z M14 2.5 V7 H18.5 M9 12 H15.5 M9 16 H15.5",
        ["bookmark"] = "M6.5 3.5 H17.5 V20.5 L12 16 L6.5 20.5 Z",
        ["star"] = "M12 3 L14.7 8.6 L20.8 9.3 L16.3 13.5 L17.5 19.6 L12 16.6 L6.5 19.6 L7.7 13.5 L3.2 9.3 L9.3 8.6 Z",
        ["chevron-down"] = "M6.5 9.5 L12 15 L17.5 9.5",
        ["sort-direction"] = "M8 4 V20 M4.5 7.5 L8 4 L11.5 7.5 M16 20 V4 M12.5 16.5 L16 20 L19.5 16.5",
        ["more"] = "M5 12 H5.5 M11.75 12 H12.25 M18.5 12 H19",
        ["library"] = "M4 5 H8 V20 H4 Z M10 5 H14 V20 H10 Z M15.6 6.2 L19.3 5.3 L22 19.2 L18.3 20.1 Z",
        ["close"] = "M6.5 6.5 L17.5 17.5 M17.5 6.5 L6.5 17.5",
        ["clock"] = "M21 12 A 9 9 0 1 1 3 12 A 9 9 0 1 1 21 12 Z M12 7 V12 L15.5 14",
        ["funnel"] = "M4 5 H20 L14 12.5 V18.5 L10 20.5 V12.5 Z",
        ["link"] = "M10 14 A 4 4 0 0 0 15.7 14.3 L 18.5 11.5 A 4 4 0 0 0 12.8 5.8 L 11.5 7 M14 10 A 4 4 0 0 0 8.3 9.7 L 5.5 12.5 A 4 4 0 0 0 11.2 18.2 L 12.5 17",
    };

    private static readonly ConcurrentDictionary<string, Bitmap?> Bitmaps = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Geometry> Geometries = new(StringComparer.Ordinal);

    static Icon()
    {
        AffectsRender<Icon>(KindProperty, ForegroundProperty);
    }

    public Icon()
    {
        Width = 20;
        Height = 20;
    }

    public string? Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public static IReadOnlyCollection<string> AllKinds => [.. BitmapKinds, .. PathKinds.Keys];

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var kind = Kind;
        var brush = Foreground ?? Brushes.Black;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (kind is null || size <= 0)
        {
            return;
        }

        var box = new Rect((Bounds.Width - size) / 2, (Bounds.Height - size) / 2, size, size);
        if (BitmapKinds.Contains(kind))
        {
            var bitmap = Bitmaps.GetOrAdd(kind, Load);
            if (bitmap is not null)
            {
                // L'image sert de masque : seule sa forme compte, la couleur vient du thème.
                using (context.PushOpacityMask(new ImageBrush(bitmap) { Stretch = Stretch.Uniform }, box))
                {
                    context.FillRectangle(brush, box);
                }
            }

            return;
        }

        if (PathKinds.TryGetValue(kind, out var data))
        {
            var geometry = Geometries.GetOrAdd(kind, _ => Geometry.Parse(data));
            var scale = size / 24.0;
            using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(box.X, box.Y)))
            {
                context.DrawGeometry(null, new Pen(brush, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
            }
        }
    }

    private static Bitmap? Load(string kind)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://Cinedex/Assets/Icons/{kind}.png"));
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}
