using System.Text.Json.Serialization;

namespace Bobine.Metadata.Tmdb;

// Sous-ensemble des réponses de l'API TMDb v3 utilisé par Bobine. Noms en snake_case côté JSON.
#pragma warning disable CA1812 // Instanciés par System.Text.Json.

internal sealed record TmdbPage<T>(IReadOnlyList<T>? Results, int TotalResults);

internal sealed record TmdbSearchItem(
    long Id,
    string? MediaType,
    string? Title,
    string? Name,
    string? OriginalTitle,
    string? OriginalName,
    string? ReleaseDate,
    string? FirstAirDate,
    string? Overview,
    string? PosterPath,
    double? VoteAverage);

internal sealed record TmdbNamed(long Id, string Name);

internal sealed record TmdbCountry([property: JsonPropertyName("iso_3166_1")] string Iso, string? Name);

internal sealed record TmdbLanguage([property: JsonPropertyName("iso_639_1")] string Iso, string? Name);

internal sealed record TmdbCollectionRef(long Id, string Name, string? PosterPath, string? BackdropPath);

internal sealed record TmdbCast(long Id, string Name, string? Character, int Order, string? ProfilePath);

internal sealed record TmdbCrew(long Id, string Name, string? Job, string? Department, string? ProfilePath);

internal sealed record TmdbCredits(IReadOnlyList<TmdbCast>? Cast, IReadOnlyList<TmdbCrew>? Crew);

internal sealed record TmdbRole(string? Character);

internal sealed record TmdbAggregateCast(long Id, string Name, IReadOnlyList<TmdbRole>? Roles, int Order, string? ProfilePath);

internal sealed record TmdbJob(string? Job);

internal sealed record TmdbAggregateCrew(long Id, string Name, IReadOnlyList<TmdbJob>? Jobs, string? Department, string? ProfilePath);

internal sealed record TmdbAggregateCredits(IReadOnlyList<TmdbAggregateCast>? Cast, IReadOnlyList<TmdbAggregateCrew>? Crew);

internal sealed record TmdbReleaseDate(string? Certification, int Type);

internal sealed record TmdbCountryReleases(
    [property: JsonPropertyName("iso_3166_1")] string Iso,
    IReadOnlyList<TmdbReleaseDate>? ReleaseDates);

internal sealed record TmdbContentRating([property: JsonPropertyName("iso_3166_1")] string Iso, string? Rating);

internal sealed record TmdbKeyword(long Id, string Name);

internal sealed record TmdbKeywords(IReadOnlyList<TmdbKeyword>? Keywords, IReadOnlyList<TmdbKeyword>? Results);

internal sealed record TmdbVideo(string? Site, string? Key, string? Type, bool Official, [property: JsonPropertyName("iso_639_1")] string? Language);

internal sealed record TmdbExternalIds(string? ImdbId);

internal sealed record TmdbMovie(
    long Id,
    string? Title,
    string? OriginalTitle,
    string? Overview,
    string? Tagline,
    string? ReleaseDate,
    int? Runtime,
    double? VoteAverage,
    int? VoteCount,
    string? PosterPath,
    string? BackdropPath,
    string? Homepage,
    string? ImdbId,
    bool Adult,
    IReadOnlyList<TmdbNamed>? Genres,
    IReadOnlyList<TmdbCountry>? ProductionCountries,
    IReadOnlyList<TmdbLanguage>? SpokenLanguages,
    IReadOnlyList<TmdbNamed>? ProductionCompanies,
    TmdbCollectionRef? BelongsToCollection,
    TmdbCredits? Credits,
    TmdbPage<TmdbCountryReleases>? ReleaseDates,
    TmdbKeywords? Keywords,
    TmdbPage<TmdbVideo>? Videos);

internal sealed record TmdbTv(
    long Id,
    string? Name,
    string? OriginalName,
    string? Overview,
    string? Tagline,
    string? FirstAirDate,
    IReadOnlyList<int>? EpisodeRunTime,
    double? VoteAverage,
    int? VoteCount,
    string? PosterPath,
    string? BackdropPath,
    string? Homepage,
    string? Status,
    bool Adult,
    IReadOnlyList<TmdbNamed>? Genres,
    IReadOnlyList<string>? OriginCountry,
    IReadOnlyList<TmdbLanguage>? SpokenLanguages,
    IReadOnlyList<TmdbNamed>? Networks,
    IReadOnlyList<TmdbNamed>? CreatedBy,
    TmdbAggregateCredits? AggregateCredits,
    TmdbPage<TmdbContentRating>? ContentRatings,
    TmdbExternalIds? ExternalIds,
    TmdbKeywords? Keywords,
    TmdbPage<TmdbVideo>? Videos,
    IReadOnlyList<TmdbSeasonSummary>? Seasons = null);

internal sealed record TmdbSeasonSummary(long Id, int SeasonNumber, string? Name, string? Overview, string? AirDate, int EpisodeCount, string? PosterPath);

internal sealed record TmdbEpisode(long Id, int EpisodeNumber, string? Name, string? Overview, string? AirDate, int? Runtime, string? StillPath);

internal sealed record TmdbSeason(long Id, int SeasonNumber, string? Name, string? Overview, string? AirDate, string? PosterPath, IReadOnlyList<TmdbEpisode>? Episodes);

internal sealed record TmdbPerson(
    long Id,
    string? Name,
    string? Biography,
    string? Birthday,
    string? Deathday,
    string? PlaceOfBirth,
    string? KnownForDepartment,
    string? ProfilePath,
    string? ImdbId);

internal sealed record TmdbFindResult(IReadOnlyList<TmdbSearchItem>? MovieResults, IReadOnlyList<TmdbSearchItem>? TvResults);

#pragma warning restore CA1812
