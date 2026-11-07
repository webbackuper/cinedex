namespace Cinedex.Core.Titles;

/// <summary>Fiche complète d'un titre, telle qu'éditée dans l'interface.</summary>
public sealed record TitleDetail
{
    public long Id { get; init; }

    public TitleKind Kind { get; init; } = TitleKind.Movie;

    public string Title { get; init; } = string.Empty;

    public string? OriginalTitle { get; init; }

    public string? AlternativeTitle { get; init; }

    public int? Year { get; init; }

    public int? RuntimeMinutes { get; init; }

    /// <summary>Durée affichée sur la fiche : « 2 h 50 ».</summary>
    public string RuntimeText => TitleText.FormatRuntime(RuntimeMinutes);

    public string? Tagline { get; init; }

    public string? Overview { get; init; }

    public string? Certification { get; init; }

    public string? Countries { get; init; }

    public string? SpokenLanguages { get; init; }

    public string? Studio { get; init; }

    public string? WebLink { get; init; }

    public string? TrailerUrl { get; init; }

    public long? TmdbId { get; init; }

    /// <summary>
    /// Section TMDb du numéro : « tv » pour un téléfilm catalogué comme film. Absente quand la section est
    /// celle qu'on attend du genre du titre, ce qui est le cas courant.
    /// </summary>
    public TitleKind? TmdbKind { get; init; }

    /// <summary>Référence TMDb complète (section et numéro), ou null si la fiche n'est pas liée.</summary>
    public Metadata.TmdbReference? Tmdb => TmdbId is { } id ? new Metadata.TmdbReference(TmdbKind ?? Kind, id) : null;

    public string? ImdbId { get; init; }

    public double? TmdbRating { get; init; }

    public double? ImdbRating { get; init; }

    public int? ImdbVotes { get; init; }

    /// <summary>Tomatometer de Rotten Tomatoes, en pourcentage.</summary>
    public int? Tomatometer { get; init; }

    /// <summary>Metascore de Metacritic, sur 100.</summary>
    public int? Metascore { get; init; }

    public double? UserRating { get; init; }

    public string? Comments { get; init; }

    public bool IsOwned { get; init; } = true;

    public bool IsFavorite { get; init; }

    public bool IsShortlisted { get; init; }

    public bool IsLocked { get; init; }

    public bool IsAdult { get; init; }

    public DateTimeOffset AddedAt { get; init; }

    public IReadOnlyList<string> GenreCodes { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    public IReadOnlyList<string> Groups { get; init; } = [];

    public long? CollectionId { get; init; }

    public string? CollectionName { get; init; }

    public CopyInfo? Copy { get; init; }

    public DateTimeOffset? LastSeenAt { get; init; }

    public int ViewCount { get; init; }

    public bool IsSeen => ViewCount > 0;

    public Loan? CurrentLoan { get; init; }
}

/// <summary>Exemplaire principal d'un titre (support, emplacement, numéro).</summary>
public sealed record CopyInfo
{
    public long Id { get; init; }

    public int? Number { get; init; }

    public string? CustomNumber { get; init; }

    public string? MediumCode { get; init; }

    public string? CaseCode { get; init; }

    public string? VersionCode { get; init; }

    public string? Location { get; init; }

    public int? DiscCount { get; init; }

    public string? Ean { get; init; }

    public decimal? Price { get; init; }

    public bool Is3D { get; init; }
}

public sealed record Loan(long Id, string Borrower, DateTimeOffset LoanedAt, DateTimeOffset? ReturnedAt);

/// <summary>D'où vient un visionnage : saisi à la main, déclenché par une lecture, ou lu à l'import.</summary>
public enum ViewingSource
{
    Manual,
    Playback,
    Import,
}

/// <summary>Un visionnage daté. La date est absente quand on sait que le film a été vu, mais pas quand.</summary>
public sealed record Viewing(long Id, DateTimeOffset? ViewedAt, ViewingSource Source);

public sealed record Genre(string Code, string Label);

public sealed record RefItem(string Kind, string Code, string Label);
