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
/// Toutes les icônes sont dessinées pour Cinedex sur une grille 24×24 ; le jeu choisi dans les réglages
/// (<see cref="Set"/>) règle le traitement : trait fin, trait marqué, deux tons, ou icônes Buddha d'origine
/// (images utilisées comme masque) là où elles existent.
/// </summary>
public sealed class Icon : Control
{
    public static readonly StyledProperty<string?> KindProperty =
        AvaloniaProperty.Register<Icon, string?>(nameof(Kind));

    /// <summary>Jeu imposé à cette icône (aperçus des réglages) ; sinon le jeu choisi pour toute l'application.</summary>
    public static readonly StyledProperty<Services.IconSet?> VariantProperty =
        AvaloniaProperty.Register<Icon, Services.IconSet?>(nameof(Variant));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    // Icônes du pack Buddha (Assets/Icons/*.png, traits noirs sur fond transparent).
    private static readonly HashSet<string> BitmapKinds = new(StringComparer.Ordinal)
    {
        "about", "add", "database", "default", "delete", "edit", "exit", "export", "filter", "harddisk",
        "insert", "options", "play", "print", "random", "search", "sort", "statistics", "view",
    };

    // Icônes dessinées pour Cinedex, grille 24×24 (épaisseur du trait selon le jeu choisi).
    private static readonly Dictionary<string, string> PathKinds = new(StringComparer.Ordinal)
    {
        // Équivalents dessinés des icônes Buddha, pour des jeux homogènes.
        ["about"] = "M21 12 A 9 9 0 1 1 3 12 A 9 9 0 1 1 21 12 Z M12 11 V16.5 M12 7.5 V8",
        ["add"] = "M21 12 A 9 9 0 1 1 3 12 A 9 9 0 1 1 21 12 Z M12 8 V16 M8 12 H16",
        ["database"] = "M5 6 C 5 3.8 19 3.8 19 6 C 19 8.2 5 8.2 5 6 Z M5 6 V18 C 5 20.2 19 20.2 19 18 V6 M5 12 C 5 14.2 19 14.2 19 12",
        ["default"] = "M3.5 9.5 H20.5 V20 H3.5 Z M3.5 9.5 L3 6.6 L19.7 3.6 L20.2 6.5 Z M8.2 9.5 L7 5.7 M13.2 9.5 L12 4.8 M18 9.5 L16.9 3.9",
        ["delete"] = "M4.5 6.5 H19.5 M9.5 6.5 V4 H14.5 V6.5 M6.5 6.5 L7.5 20.5 H16.5 L17.5 6.5 M10 10.5 V16.5 M14 10.5 V16.5",
        ["edit"] = "M4.5 19.5 L5.3 15.7 L15.8 5.2 A 1.6 1.6 0 0 1 18.1 5.2 L18.8 5.9 A 1.6 1.6 0 0 1 18.8 8.2 L8.3 18.7 Z M14 7 L17 10",
        ["exit"] = "M14 4 H18.5 A 1.5 1.5 0 0 1 20 5.5 V18.5 A 1.5 1.5 0 0 1 18.5 20 H14 M10 8 L6 12 L10 16 M6 12 H15",
        ["export"] = "M12 3.5 V14 M8 7.5 L12 3.5 L16 7.5 M5 12 V19 A 1.5 1.5 0 0 0 6.5 20.5 H17.5 A 1.5 1.5 0 0 0 19 19 V12",
        ["filter"] = "M4 5 H20 L14 12.5 V18.5 L10 20.5 V12.5 Z",
        ["harddisk"] = "M3.5 13 L6 5 H18 L20.5 13 V18.5 A 1.5 1.5 0 0 1 19 20 H5 A 1.5 1.5 0 0 1 3.5 18.5 Z M3.5 13 H20.5 M16.5 16.5 H17",
        ["insert"] = "M12 3.5 V14 M8 10 L12 14 L16 10 M5 12 V19 A 1.5 1.5 0 0 0 6.5 20.5 H17.5 A 1.5 1.5 0 0 0 19 19 V12",
        ["options"] = "M4 7 H20 M4 12 H20 M4 17 H20 M15 5 V9 M8 10 V14 M16.5 15 V19",
        ["play"] = "M21 12 A 9 9 0 1 1 3 12 A 9 9 0 1 1 21 12 Z M10 8.5 L15.5 12 L10 15.5 Z",
        ["print"] = "M7 9 V3.5 H17 V9 M7 17.5 H4.5 V10.5 A 1.5 1.5 0 0 1 6 9 H18 A 1.5 1.5 0 0 1 19.5 10.5 V17.5 H17 M7 14 H17 V20.5 H7 Z",
        ["random"] = "M3.5 7 H7 C 11 7 13 17 17 17 H20.5 M18 14.5 L20.5 17 L18 19.5 M3.5 17 H7 C 8.6 17 9.8 15.4 10.8 13.6 M13.2 10.4 C 14.2 8.6 15.4 7 17 7 H20.5 M18 4.5 L20.5 7 L18 9.5",
        ["search"] = "M16.5 10.5 A 6 6 0 1 1 4.5 10.5 A 6 6 0 1 1 16.5 10.5 Z M15 15 L20 20",
        ["sort"] = "M4 6 H20 M4 12 H15 M4 18 H10",
        ["statistics"] = "M3.5 20.5 H20.5 M7 17 V12 M11 17 V6 M15 17 V9 M19 17 V13",
        ["view"] = "M2.5 12 C 5 7.5 8.3 5.5 12 5.5 C 15.7 5.5 19 7.5 21.5 12 C 19 16.5 15.7 18.5 12 18.5 C 8.3 18.5 5 16.5 2.5 12 Z M15 12 A 3 3 0 1 1 9 12 A 3 3 0 1 1 15 12 Z",
        ["palette"] = "M12 3.5 A 8.5 8.5 0 1 0 12 20.5 C 13.4 20.5 13.8 19.4 13.2 18.4 C 12.5 17.2 13.2 15.8 14.6 15.8 H16.8 A 3.7 3.7 0 0 0 20.5 12.1 C 20.5 7.4 16.7 3.5 12 3.5 Z M8 11 H8.5 M10.5 7.5 H11 M15 8 H15.5",
        ["backup"] = "M5 6 C 5 3.8 19 3.8 19 6 C 19 8.2 5 8.2 5 6 Z M5 6 V12.5 M19 6 V10 M5 12 C 5 13.6 9 14.3 12 14.2 M16.5 21 A 4.5 4.5 0 1 1 21 16.5 M21 13 V16.5 H17.5",
        ["columns"] = "M3.5 4.5 H20.5 V19.5 H3.5 Z M9.5 4.5 V19.5 M15 4.5 V19.5",
        ["film"] = "M4 3.5 H20 V20.5 H4 Z M8 3.5 V20.5 M16 3.5 V20.5 M4 8 H8 M4 12 H8 M4 16 H8 M16 8 H20 M16 12 H20 M16 16 H20",
        ["help"] = "M21 12 A 9 9 0 1 1 3 12 A 9 9 0 1 1 21 12 Z M9.5 9.5 A 2.5 2.5 0 1 1 13 11.8 C 12.3 12.2 12 12.7 12 13.5 V14 M12 17 V17.5",
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
        ["clapper"] = "M3.5 9.5 H20.5 V20 H3.5 Z M3.5 9.5 L3 6.6 L19.7 3.6 L20.2 6.5 Z M8.2 9.5 L7 5.7 M13.2 9.5 L12 4.8 M18 9.5 L16.9 3.9 M9.5 13 L15 15.5 L9.5 18 Z",
        ["collection"] = "M8.5 3.5 H20.5 V15.5 H8.5 Z M5.5 6.5 V18.5 H17.5 M3 9.5 V21 H14.5",
        ["library"] = "M4 5 H8 V20 H4 Z M10 5 H14 V20 H10 Z M15.6 6.2 L19.3 5.3 L22 19.2 L18.3 20.1 Z",
        ["close"] = "M6.5 6.5 L17.5 17.5 M17.5 6.5 L6.5 17.5",
        ["clock"] = "M21 12 A 9 9 0 1 1 3 12 A 9 9 0 1 1 21 12 Z M12 7 V12 L15.5 14",
        ["funnel"] = "M4 5 H20 L14 12.5 V18.5 L10 20.5 V12.5 Z",
        ["link"] = "M10 14 A 4 4 0 0 0 15.7 14.3 L 18.5 11.5 A 4 4 0 0 0 12.8 5.8 L 11.5 7 M14 10 A 4 4 0 0 0 8.3 9.7 L 5.5 12.5 A 4 4 0 0 0 11.2 18.2 L 12.5 17",
    };

    private static readonly ConcurrentDictionary<string, Bitmap?> Bitmaps = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Geometry> Geometries = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Geometry?> Fills = new(StringComparer.Ordinal);

    /// <summary>Jeu d'icônes choisi dans les réglages (Apparence) ; appliqué à toutes les icônes.</summary>
    public static Services.IconSet Set { get; set; } = Services.IconSet.Fine;

    static Icon()
    {
        AffectsRender<Icon>(KindProperty, ForegroundProperty, VariantProperty);
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

    public Services.IconSet? Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public static IReadOnlyCollection<string> AllKinds => [.. BitmapKinds.Union(PathKinds.Keys)];

    /// <summary>Icônes qui ont un tracé (toutes, sauf erreur) : les jeux dessinés sont complets.</summary>
    public static bool HasPath(string kind) => PathKinds.ContainsKey(kind);

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
        var set = Variant ?? Set;
        if ((set == Services.IconSet.Classic || !PathKinds.ContainsKey(kind)) && BitmapKinds.Contains(kind))
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
            var thickness = set == Services.IconSet.Bold ? 2.3 : 1.6;
            using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(box.X, box.Y)))
            {
                if (set == Services.IconSet.Duotone && Fills.GetOrAdd(kind, _ => ClosedShapes(data)) is { } fill)
                {
                    // Deux tons : les formes fermées sont teintées de la même couleur, en transparence.
                    using (context.PushOpacity(0.22))
                    {
                        context.DrawGeometry(brush, null, fill);
                    }
                }

                context.DrawGeometry(null, new Pen(brush, thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
            }
        }
    }

    /// <summary>Formes fermées d'un tracé (sous-chemins terminés par Z), pour le jeu deux tons.</summary>
    internal static Geometry? ClosedShapes(string data)
    {
        var figures = data.Split('M', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(f => f.EndsWith('Z'))
            .Select(f => "M" + f)
            .ToList();
        return figures.Count == 0 ? null : Geometry.Parse(string.Join(' ', figures));
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
