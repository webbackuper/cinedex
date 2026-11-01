using Bobine.Core.Metadata;
using Bobine.Core.Titles;
using Bobine.Data;
using Bobine.Metadata.Tmdb;
using Microsoft.Extensions.Logging;

namespace Bobine.App.Services;

/// <summary>Enchaîne la recherche TMDb, l'import de la fiche et le téléchargement des images.</summary>
public sealed class MetadataService(
    TmdbClient tmdb,
    MetadataRepository metadata,
    ImageStore images,
    ILogger<MetadataService> logger)
{
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
        var id = metadata.Create(record, options);
        await DownloadImagesAsync(id, record, options, cancellationToken).ConfigureAwait(false);
        return (id, true);
    }

    /// <summary>Lie un titre existant à une fiche TMDb (ou la rafraîchit) ; false si le titre est verrouillé.</summary>
    public async Task<bool> UpdateAsync(long titleId, TitleKind kind, long sourceId, ImportOptions options, CancellationToken cancellationToken = default)
    {
        var record = await tmdb.GetDetailsAsync(kind, sourceId, cancellationToken).ConfigureAwait(false);
        if (!metadata.Apply(titleId, record, options))
        {
            return false;
        }

        await DownloadImagesAsync(titleId, record, options, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Met à jour tous les titres déjà liés à TMDb, en sautant les verrouillés.</summary>
    public async Task<BatchResult> UpdateAllAsync(ImportOptions options, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var linked = metadata.LinkedTitles();
        int updated = 0, skipped = 0, failed = 0, index = 0;
        foreach (var (titleId, kind, sourceId, locked) in linked)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            if (locked)
            {
                skipped++;
                continue;
            }

            try
            {
                progress?.Report($"Mise à jour {index}/{linked.Count}");
                if (await UpdateAsync(titleId, kind, sourceId, options, cancellationToken).ConfigureAwait(false))
                {
                    updated++;
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception ex) when (ex is TmdbUnavailableException or KeyNotFoundException or HttpRequestException)
            {
                failed++;
                logger.LogWarning(ex, "Échec de mise à jour du titre {TitleId}", titleId);
            }
        }

        return new BatchResult(updated, skipped, failed);
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

public sealed record BatchResult(int Updated, int Skipped, int Failed);
