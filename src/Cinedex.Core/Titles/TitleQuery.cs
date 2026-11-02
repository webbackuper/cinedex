namespace Cinedex.Core.Titles;

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
    MissingFiles,
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

public enum GenreMatch
{
    Any,
    All,
    None,
}

/// <summary>Critères de recherche par champ, combinables (ET logique entre critères renseignés).</summary>
public sealed record SearchCriteria
{
    /// <summary>Nom (partiel) d'une personne du générique.</summary>
    public string? PersonName { get; init; }

    /// <summary>Rôle de la personne ; null = n'importe quel rôle.</summary>
    public Metadata.CreditRole? PersonRole { get; init; }

    /// <summary>Nom (partiel) d'un personnage.</summary>
    public string? Character { get; init; }

    public int? YearFrom { get; init; }

    public int? YearTo { get; init; }

    public double? UserRatingMin { get; init; }

    public double? SourceRatingMin { get; init; }

    public IReadOnlyList<string> GenreCodes { get; init; } = [];

    public GenreMatch GenreMatch { get; init; } = GenreMatch.Any;

    public string? Tag { get; init; }

    public string? Group { get; init; }

    public long? CollectionId { get; init; }

    public string? Location { get; init; }

    public string? Borrower { get; init; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(PersonName) && string.IsNullOrWhiteSpace(Character) && YearFrom is null && YearTo is null &&
        UserRatingMin is null && SourceRatingMin is null && GenreCodes.Count == 0 && string.IsNullOrWhiteSpace(Tag) &&
        string.IsNullOrWhiteSpace(Group) && CollectionId is null && string.IsNullOrWhiteSpace(Location) && string.IsNullOrWhiteSpace(Borrower);
}

/// <summary>Critères de la liste principale : texte libre, filtre rapide, recherche par champ et tri.</summary>
public sealed record TitleQuery(
    string? Text = null,
    QuickFilter Filter = QuickFilter.All,
    SortField Sort = SortField.Title,
    bool Descending = false,
    bool HideAdult = false,
    SearchCriteria? Criteria = null,
    RuleSearch? Rules = null,
    IReadOnlyCollection<long>? Ids = null)
{
    public static TitleQuery Default { get; } = new();
}
