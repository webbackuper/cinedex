using Cinedex.Core.Titles;

namespace Cinedex.Core.Metadata;

/// <summary>Résultat de recherche d'une source de métadonnées.</summary>
public sealed record MetadataSearchResult(
    TitleKind Kind,
    long SourceId,
    string Title,
    string? OriginalTitle,
    int? Year,
    string? Overview,
    string? PosterPath,
    double? Rating);

public enum CreditRole
{
    Actor,
    Director,
    Writer,
    Composer,
}

public sealed record MetadataCredit(
    CreditRole Role,
    long SourcePersonId,
    string Name,
    string? Character,
    int Order,
    string? ProfilePath);

public sealed record SeasonRecord(int Number, string? Name, string? Overview, DateOnly? AirDate, int EpisodeCount, string? PosterPath);

public sealed record EpisodeRecord(int Number, string? Name, string? Overview, DateOnly? AirDate, int? RuntimeMinutes, long SourceId);

public sealed record MetadataSaga(long SourceId, string Name, string? PosterPath, string? BackdropPath);

/// <summary>Fiche normalisée, indépendante de la source (aujourd'hui TMDb).</summary>
public sealed record MetadataRecord
{
    public TitleKind Kind { get; init; }

    public long SourceId { get; init; }

    public string? ImdbId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? OriginalTitle { get; init; }

    public int? Year { get; init; }

    public int? RuntimeMinutes { get; init; }

    public string? Tagline { get; init; }

    public string? Overview { get; init; }

    public string? Certification { get; init; }

    public IReadOnlyList<string> Countries { get; init; } = [];

    public IReadOnlyList<string> SpokenLanguages { get; init; } = [];

    public string? Studio { get; init; }

    public string? Homepage { get; init; }

    public string? TrailerUrl { get; init; }

    public double? Rating { get; init; }

    public int? Votes { get; init; }

    public bool IsAdult { get; init; }

    public string? SeriesStatus { get; init; }

    public IReadOnlyList<long> GenreSourceIds { get; init; } = [];

    /// <summary>Genres par nom (fichiers .nfo), rapprochés des genres du catalogue par libellé ou code.</summary>
    public IReadOnlyList<string> GenreNames { get; init; } = [];

    public IReadOnlyList<string> Keywords { get; init; } = [];

    public IReadOnlyList<MetadataCredit> Credits { get; init; } = [];

    public MetadataSaga? Collection { get; init; }

    /// <summary>Saisons d'une série (0 = épisodes spéciaux), sans le détail des épisodes.</summary>
    public IReadOnlyList<SeasonRecord> Seasons { get; init; } = [];

    public string? PosterPath { get; init; }

    public string? BackdropPath { get; init; }
}

/// <summary>Champs à importer ; tout décocher d'un côté le protège des mises à jour.</summary>
public sealed record ImportOptions
{
    public bool UpdateTitle { get; init; } = true;

    public bool UpdateOverview { get; init; } = true;

    public bool UpdateCredits { get; init; } = true;

    public bool UpdateGenres { get; init; } = true;

    public bool UpdateCollection { get; init; } = true;

    public bool ImportKeywordsAsTags { get; init; }

    public bool DownloadPoster { get; init; } = true;

    public bool DownloadBackdrop { get; init; } = true;

    public bool DownloadCastPhotos { get; init; }

    public bool OverwriteExistingImages { get; init; }

    /// <summary>
    /// Garde le genre du titre tel qu'il est au lieu de prendre celui de la fiche distante. Sert aux
    /// téléfilms : TMDb les range sous « tv », mais on les catalogue comme des films.
    /// </summary>
    public bool KeepKind { get; init; }

    /// <summary>
    /// Genre à donner à l'entrée, quelle que soit la section d'où vient la fiche. Sert à l'import d'un
    /// téléfilm trouvé dans la section télévision : le fichier scanné est un film et le reste.
    /// </summary>
    public Titles.TitleKind? AsKind { get; init; }

    public int MaxActors { get; init; } = 25;

    public static ImportOptions Default { get; } = new();
}

/// <summary>Un film d'une collection selon la source (présent ou non dans le catalogue).</summary>
public sealed record CollectionPart(long SourceId, string Title, int? Year, string? PosterPath);

/// <summary>Fiche d'une collection (saga) : présentation, affiche, décor et liste de ses films.</summary>
public sealed record CollectionRecord(long SourceId, string Name, string? Overview, string? PosterPath, string? BackdropPath, IReadOnlyList<CollectionPart> Parts);
