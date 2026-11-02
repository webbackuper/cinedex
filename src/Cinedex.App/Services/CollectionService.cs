using System.Globalization;
using System.Text;
using Cinedex.Core.Metadata;
using Cinedex.Data;
using Cinedex.Metadata.Tmdb;
using Microsoft.Extensions.Logging;

namespace Cinedex.App.Services;

/// <summary>Bilan d'une mise à jour des collections depuis TMDb.</summary>
public sealed record CollectionSyncResult(int Updated, int Identified, int NotFound, int Failed);

/// <summary>
/// Collections (sagas TMDb) : lecture de la fiche TMDb (présentation, films de la saga), téléchargement de l'affiche
/// et du décor, identification par le nom des collections créées à la main ou reprises d'un autre logiciel.
/// </summary>
public sealed class CollectionService(CollectionRepository repository, TmdbClient tmdb, ImageStore images, ILogger<CollectionService> logger, SettingsStore? settings = null)
{
    public CollectionRepository Repository => repository;

    /// <summary>Réglage du catalogue (Réglages > Général) ; activé par défaut.</summary>
    public bool IsEnabled => settings is null || DisplaySettings.Load(settings).ManageCollections;

    /// <summary>Chemin absolu d'une image du catalogue, ou null si elle n'existe pas.</summary>
    public string? Resolve(string? relative) => relative is null ? null : images.ResolveInside(relative);

    /// <summary>Petite affiche d'un film manquant de la saga (non enregistrée dans le catalogue).</summary>
    public Task<byte[]> DownloadThumbnailAsync(string posterPath, CancellationToken cancellationToken = default) =>
        tmdb.DownloadImageAsync(posterPath, "w154", cancellationToken);

    /// <summary>Lit la fiche TMDb de la collection et télécharge son affiche et son décor ; renvoie l'identifiant gardé.</summary>
    public async Task<long> RefreshAsync(long id, long tmdbId, bool overwriteImages = false, CancellationToken cancellationToken = default)
    {
        var record = await tmdb.GetCollectionAsync(tmdbId, cancellationToken).ConfigureAwait(false);
        id = repository.SaveDetails(id, record);
        await DownloadAsync(id, "poster", record.PosterPath, "w500", overwriteImages, cancellationToken).ConfigureAwait(false);
        await DownloadAsync(id, "backdrop", record.BackdropPath, "w1280", overwriteImages, cancellationToken).ConfigureAwait(false);
        return id;
    }

    /// <summary>
    /// Cherche sur TMDb une collection sans identifiant, par son nom. Elle n'est liée que si un seul résultat existe
    /// ou si un nom correspond exactement (en ignorant « Collection », « Saga », accents et ponctuation).
    /// </summary>
    public async Task<long?> IdentifyAsync(long id, string name, CancellationToken cancellationToken = default)
    {
        var results = await tmdb.SearchCollectionsAsync(name, cancellationToken).ConfigureAwait(false);
        var key = Normalize(name);
        var match = results.Count == 1 ? results[0] : results.FirstOrDefault(r => Normalize(r.Name) == key);
        return match is null ? null : await RefreshAsync(id, match.SourceId, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Après l'import ou la mise à jour d'un titre : complète la fiche de sa collection si elle n'a jamais été lue.</summary>
    public async Task CompleteForTitleAsync(long titleId, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || repository.ForTitle(titleId) is not { TmdbId: { } tmdbId, Fetched: false } collection)
        {
            return;
        }

        try
        {
            await RefreshAsync(collection.Id, tmdbId, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TmdbUnavailableException or InvalidDataException)
        {
            // Une fiche de collection manquante ne doit pas faire échouer l'import du titre.
            logger.LogWarning(ex, "Fiche de la collection {CollectionId} non lue", collection.Id);
        }
    }

    /// <summary>
    /// Identifie les collections sans fiche TMDb, puis lit la fiche de celles qui ne l'ont pas encore
    /// (ou de toutes si <paramref name="all"/>). Les erreurs ponctuelles sont comptées, pas levées.
    /// </summary>
    public async Task<CollectionSyncResult> SyncAsync(bool all, IProgress<string>? progress, CancellationToken cancellationToken, bool identify = true)
    {
        int updated = 0, identified = 0, notFound = 0, failed = 0;
        var unlinked = identify ? repository.Unlinked() : [];
        var stale = all ? repository.Stale(DateTime.UtcNow.AddMinutes(1)) : repository.Stale();
        var total = unlinked.Count + stale.Count;
        var done = 0;

        foreach (var (id, name) in unlinked)
        {
            progress?.Report(Cinedex.Core.Localization.Tr.F("Collections : {0} / {1} — {2}", ++done, total, name));
            try
            {
                if (await IdentifyAsync(id, name, cancellationToken).ConfigureAwait(false) is null)
                {
                    notFound++;
                }
                else
                {
                    identified++;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TmdbUnavailableException or InvalidDataException)
            {
                logger.LogWarning(ex, "Collection {Name} non identifiée", name);
                failed++;
            }
        }

        foreach (var (id, tmdbId) in stale)
        {
            progress?.Report(Cinedex.Core.Localization.Tr.F("Collections : {0} / {1}", ++done, total));
            try
            {
                await RefreshAsync(id, tmdbId, overwriteImages: all, cancellationToken).ConfigureAwait(false);
                updated++;
            }
            catch (Exception ex) when (ex is HttpRequestException or TmdbUnavailableException or InvalidDataException)
            {
                logger.LogWarning(ex, "Fiche de la collection {CollectionId} non lue", id);
                failed++;
            }
        }

        return new CollectionSyncResult(updated, identified, notFound, failed);
    }

    /// <summary>Nom réduit à l'essentiel pour comparer « Star Wars », « Star Wars - Saga » et « Star Wars Collection ».</summary>
    internal static string Normalize(string name)
    {
        var text = new StringBuilder();
        foreach (var c in (name ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            text.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        string[] noise = ["collection", "saga", "la", "le", "les", "the", "film", "films", "trilogie", "trilogy"];
        return string.Join(' ', text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !noise.Contains(w)));
    }

    private async Task DownloadAsync(long id, string role, string? path, string size, bool overwrite, CancellationToken cancellationToken)
    {
        if (path is null || (!overwrite && images.HasCollectionImage(id, role)))
        {
            return;
        }

        try
        {
            var bytes = await tmdb.DownloadImageAsync(path, size, cancellationToken).ConfigureAwait(false);
            images.AttachToCollection(id, images.Save(bytes, $"https://{TmdbClient.ImageHost}/t/p/{size}{path}"), role);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or TmdbUnavailableException or ArgumentException)
        {
            logger.LogWarning(ex, "Image {Role} non téléchargée pour la collection {CollectionId}", role, id);
        }
    }
}
