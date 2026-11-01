namespace Bobine.Core.Titles;

/// <summary>Filtres rapides de la liste principale.</summary>
public enum QuickFilter
{
    All,
    Owned,
    Wishlist,
    Seen,
    Unseen,
    Favorites,
    Shortlist,
    Movies,
    Series,
    Loaned,
    StandardDefinition,
    HighDefinition,
    UltraHighDefinition,
    ThreeD,
    PhysicalMedia,
    MediaFiles,
}

public enum SortField
{
    Title,
    Year,
    Number,
    AddedAt,
    UserRating,
    Runtime,
}

/// <summary>Critères de la liste principale : texte libre, filtre rapide et tri.</summary>
public sealed record TitleQuery(
    string? Text = null,
    QuickFilter Filter = QuickFilter.All,
    SortField Sort = SortField.Title,
    bool Descending = false,
    bool HideAdult = false)
{
    public static TitleQuery Default { get; } = new();
}
