using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Metadata.Tmdb;
using Microsoft.Extensions.Logging;
using Cinedex.Core.Localization;

namespace Cinedex.App.Services;

/// <summary>Enchaîne la recherche TMDb, l'import de la fiche et le téléchargement des images.</summary>
public sealed class MetadataService(
    TmdbClient tmdb,
    MetadataRepository metadata,
    ImageStore images,
    ILogger<MetadataService> logger,
    SeriesRepository? series = null,
    NfoService? nfo = null,
    RatingsService? ratings = null,
    SettingsStore? settings = null,
    ApiGate? gate = null,
    MatchReviewRepository? reviews = null)
{
    /// <summary>Liste « À vérifier » des rapprochements ambigus (absente hors catalogue, par exemple en test).</summary>
    public MatchReviewRepository? Reviews => reviews;

    /// <summary>Petite affiche d'un candidat TMDb, pour le choisir à l'œil.</summary>
    public Task<byte[]> DownloadThumbnailAsync(string posterPath, CancellationToken cancellationToken = default) =>
        tmdb.DownloadImageAsync(posterPath, "w154", cancellationToken);

    public const string MaxActorsKey = "metadata.maxActors";
    public const int DefaultMaxActors = 25;

    /// <summary>Nombre d'acteurs gardés dans la distribution, choisi dans les réglages (1 à 200).</summary>
    public int MaxActors => ReadMaxActors(settings);

    public static int ReadMaxActors(SettingsStore? store) =>
        int.TryParse(store?.Get(MaxActorsKey), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 200
            ? n
            : DefaultMaxActors;

    /// <summary>Notes IMDb / Rotten Tomatoes / Metacritic : un échec n'empêche jamais la mise à jour de la fiche.</summary>
    private async Task EnrichRatingsAsync(long titleId, CancellationToken cancellationToken)
    {
        if (ratings is null)
        {
            return;
        }

        try
        {
            await ratings.EnrichAsync(titleId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or ArgumentException)
        {
            logger.LogWarning(ex, "Notes externes non récupérées pour le titre {TitleId}", titleId);
        }
    }

    /// <summary>
    /// Récupère saisons et épisodes d'une série liée à TMDb et les fusionne (saisons verrouillées épargnées).
    /// Renvoie le nombre de nouveaux épisodes.
    /// </summary>
    public async Task<int> SyncEpisodesAsync(long titleId, long sourceId, MetadataRecord? record = null, CancellationToken cancellationToken = default)
    {
        if (series is null)
        {
            return 0;
        }

        record ??= await tmdb.GetDetailsAsync(TitleKind.Series, sourceId, cancellationToken).ConfigureAwait(false);
        var episodes = new Dictionary<int, IReadOnlyList<EpisodeRecord>>();
        foreach (var season in record.Seasons)
        {
            episodes[season.Number] = await tmdb.GetSeasonAsync(sourceId, season.Number, cancellationToken).ConfigureAwait(false);
        }

        return series.Merge(titleId, record.Seasons, episodes);
    }

    public Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string text, CancellationToken cancellationToken = default)
    {
        var (title, year) = TitleText.SplitYear(text);
        return tmdb.SearchAsync(title, year, cancellationToken);
    }

    /// <summary>Crée un titre depuis un résultat de recherche ; renvoie l'identifiant (existant si déjà présent).</summary>
    public async Task<(long TitleId, bool Created)> ImportAsync(MetadataSearchResult result, ImportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (metadata.FindBySource(result.Kind, result.SourceId) is { } existing)
        {
            return (existing, false);
        }

        var record = await tmdb.GetDetailsAsync(result.Kind, result.SourceId, cancellationToken).ConfigureAwait(false);
        var id = metadata.Create(record, options with { MaxActors = MaxActors });
        await DownloadImagesAsync(id, record, options, cancellationToken).ConfigureAwait(false);
        if (record.Kind == TitleKind.Series)
        {
            await SyncEpisodesAsync(id, record.SourceId, record, cancellationToken).ConfigureAwait(false);
        }

        await EnrichRatingsAsync(id, cancellationToken).ConfigureAwait(false);
        nfo?.AfterMetadataUpdate(id);
        return (id, true);
    }

    /// <summary>Lie un titre existant à une fiche TMDb (ou la rafraîchit) ; false si le titre est verrouillé.</summary>
    public async Task<bool> UpdateAsync(long titleId, TitleKind kind, long sourceId, ImportOptions options, CancellationToken cancellationToken = default)
    {
        var record = await tmdb.GetDetailsAsync(kind, sourceId, cancellationToken).ConfigureAwait(false);
        if (!metadata.Apply(titleId, record, options with { MaxActors = MaxActors }))
        {
            return false;
        }

        await DownloadImagesAsync(titleId, record, options, cancellationToken).ConfigureAwait(false);
        if (record.Kind == TitleKind.Series)
        {
            await SyncEpisodesAsync(titleId, record.SourceId, record, cancellationToken).ConfigureAwait(false);
        }

        await EnrichRatingsAsync(titleId, cancellationToken).ConfigureAwait(false);
        nfo?.AfterMetadataUpdate(titleId);
        reviews?.Remove(titleId);
        return true;
    }

    /// <summary>
    /// « Mettre à jour via TMDb » : enrichit les titres existants, sans jamais en créer.
    /// Un titre déjà lié est rafraîchi ; un titre non lié est identifié par son titre et son année (à un an près),
    /// puis mis à jour sur place. Les titres verrouillés sont ignorés ; un titre dont la fiche TMDb appartient
    /// déjà à une autre entrée est signalé comme doublon et laissé intact.
    /// </summary>
    public async Task<BatchResult> UpdateTitlesAsync(IReadOnlyList<TitleDetail> targets, ImportOptions options, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        gate?.Reset();

        // Titres déjà identifiés (TMDb ou IMDb connu) d'abord ; les rapprochements à vérifier viennent ensuite.
        targets = [.. targets.OrderBy(t => t.TmdbId is not null || t.ImdbId is not null ? 0 : 1)];
        int updated = 0, skipped = 0, failed = 0, notFound = 0, duplicates = 0, toReview = 0;
        var messages = new List<string>();
        for (var i = 0; i < targets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var title = targets[i];
            progress?.Report(Tr.F("TMDb {0}/{1} : {2}", i + 1, targets.Count, title.Title));
            if (title.IsLocked)
            {
                skipped++;
                continue;
            }

            if (gate?.IsDisabled(ExternalApi.Tmdb) == true)
            {
                // TMDb écarté après sa limite : on complète au moins les notes (OMDb / fichier IMDb) si l'identifiant est connu.
                skipped++;
                await EnrichRatingsAsync(title.Id, cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                var identification = title.TmdbId is null ? await IdentifyAsync(title, cancellationToken).ConfigureAwait(false) : null;
                if (identification is { NeedsReview: true })
                {
                    // Plusieurs fiches possibles ou année inconnue : on ne devine pas, l'utilisateur choisira.
                    reviews?.Add(title.Id, identification.Query, identification.Candidates);
                    toReview++;
                    messages.Add(Tr.F("À vérifier ({0} possibilités) : {1}{2}", identification.Candidates.Count, title.Title, (title.Year is { } ty ? $" ({ty})" : string.Empty)));
                    continue;
                }

                var (kind, sourceId) = title.TmdbId is { } known
                    ? (title.Kind, known)
                    : identification?.Match is { } match
                        ? (match.Kind, match.SourceId)
                        : (title.Kind, 0L);
                if (sourceId == 0)
                {
                    notFound++;
                    messages.Add(Tr.F("Non trouvé sur TMDb : {0}{1}", title.Title, (title.Year is { } y ? $" ({y})" : string.Empty)));
                    continue;
                }

                if (await UpdateAsync(title.Id, kind, sourceId, options, cancellationToken).ConfigureAwait(false))
                {
                    updated++;
                }
                else
                {
                    skipped++;
                }
            }
            catch (DuplicateTitleException)
            {
                duplicates++;
                messages.Add(Tr.F("Déjà au catalogue sous une autre entrée : {0}", title.Title));
            }
            catch (Exception ex) when (gate is not null && ex is TmdbRateLimitException or TmdbAuthenticationException)
            {
                // Limite TMDb ou jeton refusé : l'utilisateur choisit de terminer sans TMDb ou d'interrompre.
                await gate.ReportLimitAsync(ExternalApi.Tmdb, ex.Message).ConfigureAwait(false);
                messages.Add(Tr.F("TMDb écarté : {0}", ex.Message));
                i--;
            }
            catch (Exception ex) when (ex is TmdbUnavailableException or KeyNotFoundException or HttpRequestException)
            {
                failed++;
                logger.LogWarning(ex, "Échec de mise à jour du titre {TitleId}", title.Id);
            }
        }

        return new BatchResult(updated, skipped, failed, notFound, duplicates, messages, toReview);
    }

    /// <summary>
    /// Recherche la fiche TMDb d'un titre déjà au catalogue : identifiant IMDb d'abord, sinon titre et année
    /// (même type). Un seul résultat à un an près est retenu ; sinon les candidats sont à faire choisir.
    /// </summary>
    internal async Task<Identification> IdentifyAsync(TitleDetail title, CancellationToken cancellationToken)
    {
        var query = title.Year is { } y ? $"{title.Title} ({y})" : title.Title;
        if (title.ImdbId is { } imdb)
        {
            var byImdb = await tmdb.SearchAsync(imdb, null, cancellationToken).ConfigureAwait(false);
            if (byImdb.Count > 0)
            {
                return Identification.Found(byImdb[0], imdb);
            }
        }

        var results = (await tmdb.SearchAsync(title.Title, title.Year, cancellationToken).ConfigureAwait(false))
            .Where(r => r.Kind == title.Kind)
            .ToList();
        return Identification.Decide(results, title.Year, query);
    }

    private async Task DownloadImagesAsync(long titleId, MetadataRecord record, ImportOptions options, CancellationToken cancellationToken)
    {
        await DownloadAsync(titleId, "poster", record.PosterPath, "w780", options.DownloadPoster, options, cancellationToken).ConfigureAwait(false);
        await DownloadAsync(titleId, "backdrop", record.BackdropPath, "w1280", options.DownloadBackdrop, options, cancellationToken).ConfigureAwait(false);
    }

    private async Task DownloadAsync(long titleId, string role, string? path, string size, bool wanted, ImportOptions options, CancellationToken cancellationToken)
    {
        if (!wanted || path is null || (!options.OverwriteExistingImages && images.HasImage(titleId, role)))
        {
            return;
        }

        try
        {
            var bytes = await tmdb.DownloadImageAsync(path, size, cancellationToken).ConfigureAwait(false);
            var imageId = images.Save(bytes, $"https://{TmdbClient.ImageHost}/t/p/{size}{path}");
            images.Attach(titleId, imageId, role);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or TmdbUnavailableException or ArgumentException)
        {
            // Une image manquante ne doit pas faire échouer l'import de la fiche.
            logger.LogWarning(ex, "Image {Role} non téléchargée pour le titre {TitleId}", role, titleId);
        }
    }
}

public sealed record BatchResult(int Updated, int Skipped, int Failed, int NotFound = 0, int Duplicates = 0, IReadOnlyList<string>? Messages = null, int ToReview = 0);
