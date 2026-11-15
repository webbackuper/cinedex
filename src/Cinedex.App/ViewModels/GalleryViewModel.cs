using Avalonia.Media;

namespace Cinedex.App.ViewModels;

/// <summary>Ce que montre une galerie de la vue centrale.</summary>
public enum GalleryKind
{
    /// <summary>Les sagas et coffrets du catalogue.</summary>
    Collections,

    /// <summary>Les genres portés par au moins un titre.</summary>
    Genres,
}

/// <summary>
/// Une vignette de galerie : une collection ou un genre. Son visuel est fait des images du catalogue (le
/// décor ou l'affiche de la saga, quatre affiches du genre), sous un voile de couleur propre à la vignette,
/// qui reste un visuel honorable quand aucune image n'est encore téléchargée.
/// </summary>
public sealed record GalleryCard(GalleryKind Kind, string Key, string Title, string Subtitle, IReadOnlyList<string> Images, long? Id = null)
{
    /// <summary>Couleur de la vignette, tirée de sa clé : la même d'une ouverture à l'autre.</summary>
    public IBrush Tint { get; } = new SolidColorBrush(TintOf(Key));

    /// <summary>Voile du bas, sur lequel le nom reste lisible quelle que soit l'image.</summary>
    public IBrush Veil { get; } = new LinearGradientBrush
    {
        StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
        EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.25),
            new GradientStop(Color.FromArgb(215, 0, 0, 0), 1),
        },
    };

    public bool HasImages => Images.Count > 0;

    /// <summary>Teinte stable d'après la clé (un hachage simple, identique d'une machine à l'autre), sombre pour porter du texte blanc.</summary>
    private static Color TintOf(string key)
    {
        var hash = 17;
        foreach (var c in key)
        {
            hash = unchecked((hash * 31) + c);
        }

        var hue = Math.Abs(hash % 360);
        return new HslColor(1, hue, 0.45, 0.32).ToRgb();
    }
}

/// <summary>
/// Le fil d'Ariane au-dessus de la liste quand on est entré dans une collection ou un genre : d'où l'on
/// vient (pour y revenir d'un clic), où l'on est, et ce qu'il y a à en dire.
/// </summary>
public sealed record GalleryTrail(GalleryKind Kind, string BackLabel, string Title, string Summary, long? Id = null);

/// <summary>
/// Galerie de la vue centrale : les collections, ou les genres, en vignettes. Entrer dans une vignette montre
/// ses films dans la liste ordinaire, avec toutes ses actions ; le fil d'Ariane ramène ici.
/// </summary>
public sealed class GalleryViewModel(GalleryKind kind, string title, string hint, IReadOnlyList<GalleryCard> cards)
{
    public GalleryKind Kind { get; } = kind;

    public string Title { get; } = title;

    public string Hint { get; } = hint;

    public IReadOnlyList<GalleryCard> Cards { get; } = cards;

    public bool IsEmpty => Cards.Count == 0;

    public bool IsCollections => Kind == GalleryKind.Collections;
}
