using System.Globalization;
using System.Text.RegularExpressions;
using Bobine.Core.Metadata;
using Bobine.Core.Titles;

namespace Bobine.Metadata.Tmdb;

/// <summary>Convertit les réponses TMDb en <see cref="MetadataRecord"/>.</summary>
internal static partial class TmdbMapper
{
    private static readonly HashSet<string> WriterJobs = new(StringComparer.OrdinalIgnoreCase) { "Screenplay", "Writer", "Story", "Author", "Novel" };
    private static readonly HashSet<string> ComposerJobs = new(StringComparer.OrdinalIgnoreCase) { "Original Music Composer", "Music", "Composer" };

    public static MetadataRecord ToRecord(TmdbMovie movie, TmdbOptions options)
    {
        var credits = new List<MetadataCredit>();
        foreach (var cast in (movie.Credits?.Cast ?? []).OrderBy(c => c.Order).Take(50))
        {
            credits.Add(new MetadataCredit(CreditRole.Actor, cast.Id, cast.Name, Clean(cast.Character), cast.Order, cast.ProfilePath));
        }

        AddCrew(credits, (movie.Credits?.Crew ?? []).Select(c => (c.Id, c.Name, c.Job, c.ProfilePath)));

        return new MetadataRecord
        {
            Kind = TitleKind.Movie,
            SourceId = movie.Id,
            ImdbId = ValidImdb(movie.ImdbId),
            Title = movie.Title ?? movie.OriginalTitle ?? string.Empty,
            OriginalTitle = movie.OriginalTitle,
            Year = YearOf(movie.ReleaseDate),
            RuntimeMinutes = movie.Runtime is > 0 ? movie.Runtime : null,
            Tagline = Clean(movie.Tagline),
            Overview = Clean(movie.Overview),
            Certification = MovieCertification(movie, options.CertificationCountry),
            Countries = (movie.ProductionCountries ?? []).Select(c => c.Iso).ToList(),
            SpokenLanguages = (movie.SpokenLanguages ?? []).Select(l => l.Iso).ToList(),
            Studio = movie.ProductionCompanies?.FirstOrDefault()?.Name,
            Homepage = SafeHttpUrl(movie.Homepage),
            TrailerUrl = Trailer(movie.Videos?.Results, options.Language),
            Rating = movie.VoteCount > 0 ? movie.VoteAverage : null,
            Votes = movie.VoteCount,
            IsAdult = movie.Adult,
            GenreSourceIds = (movie.Genres ?? []).Select(g => g.Id).ToList(),
            Keywords = (movie.Keywords?.Keywords ?? []).Select(k => k.Name).ToList(),
            Credits = credits,
            Collection = movie.BelongsToCollection is { } c ? new MetadataSaga(c.Id, c.Name, c.PosterPath, c.BackdropPath) : null,
            PosterPath = movie.PosterPath,
            BackdropPath = movie.BackdropPath,
        };
    }

    public static MetadataRecord ToRecord(TmdbTv tv, TmdbOptions options)
    {
        var credits = new List<MetadataCredit>();
        foreach (var cast in (tv.AggregateCredits?.Cast ?? []).OrderBy(c => c.Order).Take(50))
        {
            credits.Add(new MetadataCredit(CreditRole.Actor, cast.Id, cast.Name, Clean(cast.Roles?.FirstOrDefault()?.Character), cast.Order, cast.ProfilePath));
        }

        // Pour une série, les créateurs tiennent lieu de scénaristes principaux.
        var order = 0;
        foreach (var creator in tv.CreatedBy ?? [])
        {
            credits.Add(new MetadataCredit(CreditRole.Writer, creator.Id, creator.Name, null, order++, null));
        }

        AddCrew(credits, (tv.AggregateCredits?.Crew ?? [])
            .Where(c => c.Jobs?.Any(j => ComposerJobs.Contains(j.Job ?? string.Empty)) == true)
            .Select(c => (c.Id, c.Name, (string?)"Original Music Composer", c.ProfilePath)));

        var certification = (tv.ContentRatings?.Results ?? []).FirstOrDefault(r => r.Iso == options.CertificationCountry)?.Rating
            ?? (tv.ContentRatings?.Results ?? []).FirstOrDefault(r => r.Iso == "US")?.Rating;

        return new MetadataRecord
        {
            Kind = TitleKind.Series,
            SourceId = tv.Id,
            ImdbId = ValidImdb(tv.ExternalIds?.ImdbId),
            Title = tv.Name ?? tv.OriginalName ?? string.Empty,
            OriginalTitle = tv.OriginalName,
            Year = YearOf(tv.FirstAirDate),
            RuntimeMinutes = tv.EpisodeRunTime?.FirstOrDefault() is > 0 and var runtime ? runtime : null,
            Tagline = Clean(tv.Tagline),
            Overview = Clean(tv.Overview),
            Certification = Clean(certification),
            Countries = tv.OriginCountry ?? [],
            SpokenLanguages = (tv.SpokenLanguages ?? []).Select(l => l.Iso).ToList(),
            Studio = tv.Networks?.FirstOrDefault()?.Name,
            Homepage = SafeHttpUrl(tv.Homepage),
            TrailerUrl = Trailer(tv.Videos?.Results, options.Language),
            Rating = tv.VoteCount > 0 ? tv.VoteAverage : null,
            Votes = tv.VoteCount,
            IsAdult = tv.Adult,
            SeriesStatus = tv.Status switch
            {
                "Returning Series" or "In Production" or "Planned" => "continuing",
                "Ended" => "ended",
                "Canceled" => "cancelled",
                _ => "unknown",
            },
            GenreSourceIds = (tv.Genres ?? []).Select(g => g.Id).ToList(),
            Keywords = (tv.Keywords?.Results ?? []).Select(k => k.Name).ToList(),
            Credits = credits,
            PosterPath = tv.PosterPath,
            BackdropPath = tv.BackdropPath,
        };
    }

    public static int? YearOf(string? date) =>
        date is { Length: >= 4 } && int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year > 1800
            ? year
            : null;

    private static void AddCrew(List<MetadataCredit> credits, IEnumerable<(long Id, string Name, string? Job, string? Profile)> crew)
    {
        var seen = new HashSet<(CreditRole, long)>();
        var order = credits.Count;
        foreach (var (id, name, job, profile) in crew)
        {
            CreditRole? role = job switch
            {
                "Director" => CreditRole.Director,
                _ when WriterJobs.Contains(job ?? string.Empty) => CreditRole.Writer,
                _ when ComposerJobs.Contains(job ?? string.Empty) => CreditRole.Composer,
                _ => null,
            };
            if (role is { } r && seen.Add((r, id)) && !credits.Any(c => c.Role == r && c.SourcePersonId == id))
            {
                credits.Add(new MetadataCredit(r, id, name, null, order++, profile));
            }
        }
    }

    private static string? MovieCertification(TmdbMovie movie, string country)
    {
        string? For(string iso) => (movie.ReleaseDates?.Results ?? [])
            .FirstOrDefault(r => r.Iso == iso)?.ReleaseDates?
            .OrderBy(d => d.Type == 3 ? 0 : 1)
            .Select(d => Clean(d.Certification))
            .FirstOrDefault(c => c is not null);
        return For(country) ?? For("US");
    }

    private static string? Trailer(IReadOnlyList<TmdbVideo>? videos, string language)
    {
        var lang = language.Split('-')[0];
        var best = (videos ?? [])
            .Where(v => v.Site == "YouTube" && v.Type == "Trailer" && v.Key is not null && YouTubeKey().IsMatch(v.Key))
            .OrderBy(v => v.Language == lang ? 0 : 1)
            .ThenBy(v => v.Official ? 0 : 1)
            .FirstOrDefault();
        return best is null ? null : $"https://www.youtube.com/watch?v={best.Key}";
    }

    private static string? ValidImdb(string? id) => id is not null && ImdbId().IsMatch(id) ? id : null;

    private static string? SafeHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? uri.AbsoluteUri : null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Za-z0-9_-]{6,20}$")]
    private static partial Regex YouTubeKey();

    [GeneratedRegex("^tt[0-9]{7,10}$")]
    private static partial Regex ImdbId();
}
