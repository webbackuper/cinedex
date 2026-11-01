using Bobine.Data;
using Bobine.Metadata.Ratings;
using Microsoft.Extensions.Logging;

namespace Bobine.App.Services;

/// <summary>
/// Notes IMDb, Rotten Tomatoes et Metacritic à partir de l'identifiant IMDb que TMDb fournit.
/// <para>
/// Choix retenu : OMDb en premier, car un seul appel par film donne les trois notes (et OMDb est de toute façon
/// nécessaire pour Rotten Tomatoes et Metacritic). Le fichier officiel IMDb sert de complément : il prend le
/// relais quand OMDb n'a pas de clé, a épuisé son quota du jour ou ne connaît pas le film, et il permet
/// d'actualiser d'un coup les notes IMDb de tout le catalogue sans consommer le quota OMDb.
/// </para>
/// </summary>
public sealed class RatingsService(
    OmdbClient omdb,
    ImdbRatingsDataset dataset,
    TitleRepository titles,
    MetadataRepository metadata,
    SettingsStore settings,
    ILogger<RatingsService> logger)
{
    public const string UseDatasetKey = "ratings.useImdbDataset";

    private bool _omdbExhausted;

    public bool UseDataset => settings.Get(UseDatasetKey) != "false";

    /// <summary>Récupère et enregistre les notes d'un titre ; renvoie les notes trouvées (ou null).</summary>
    public async Task<ExternalRatings?> EnrichAsync(long titleId, CancellationToken cancellationToken = default)
    {
        var imdbId = titles.Get(titleId)?.ImdbId;
        if (imdbId is null)
        {
            return null;
        }

        ExternalRatings? ratings = null;
        if (!_omdbExhausted)
        {
            try
            {
                ratings = await omdb.GetRatingsAsync(imdbId, cancellationToken).ConfigureAwait(false);
            }
            catch (OmdbException ex)
            {
                // Quota épuisé ou clé refusée : inutile d'insister pour les titres suivants de la session.
                _omdbExhausted = ex.IsQuota || ex.IsAuth;
                logger.LogWarning("OMDb : {Message}", ex.Message);
            }
        }

        if (ratings?.ImdbRating is null && UseDataset)
        {
            try
            {
                await dataset.RefreshAsync(TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false);
                if ((await dataset.LookupAsync([imdbId], cancellationToken).ConfigureAwait(false)).TryGetValue(imdbId, out var found))
                {
                    ratings = (ratings ?? new ExternalRatings(null, null, null, null)) with { ImdbRating = found.Rating, ImdbVotes = found.Votes };
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
            {
                logger.LogWarning(ex, "Fichier des notes IMDb indisponible");
            }
        }

        if (ratings is null || ratings.IsEmpty)
        {
            return null;
        }

        metadata.ApplyRatings(titleId, ratings.ImdbRating, ratings.ImdbVotes, ratings.Tomatometer, ratings.Metascore);
        return ratings;
    }

    /// <summary>Actualise les notes IMDb de tout le catalogue depuis le fichier officiel ; renvoie le nombre de titres mis à jour.</summary>
    public async Task<int> RefreshAllImdbAsync(IProgress<string>? progress, CancellationToken cancellationToken = default)
    {
        progress?.Report("Téléchargement du fichier des notes IMDb (au plus une fois par jour)...");
        await dataset.RefreshAsync(TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false);
        var targets = metadata.TitlesWithImdbId();
        progress?.Report($"Lecture des notes de {targets.Count} titre(s)...");
        var found = await dataset.LookupAsync(targets.Select(t => t.ImdbId), cancellationToken).ConfigureAwait(false);
        var updated = 0;
        foreach (var (titleId, imdbId) in targets)
        {
            if (found.TryGetValue(imdbId, out var r) && metadata.ApplyRatings(titleId, r.Rating, r.Votes, null, null))
            {
                updated++;
            }
        }

        return updated;
    }
}
