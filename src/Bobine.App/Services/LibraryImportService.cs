using Bobine.Core.Metadata;
using Bobine.Core.Titles;
using Bobine.Data;
using Bobine.Media;
using Bobine.Metadata.Tmdb;
using Microsoft.Extensions.Logging;

namespace Bobine.App.Services;

public sealed record LibraryImportOptions
{
    /// <summary>Si plusieurs résultats TMDb, prendre le premier dont l'année concorde (sinon : ajout sans métadonnées).</summary>
    public bool UseFirstMatch { get; init; } = true;

    public bool UseTmdb { get; init; } = true;

    public bool ReadTechnicalProperties { get; init; } = true;

    public bool UseLocalCover { get; init; } = true;

    public bool WriteNfo { get; init; }
}

public sealed record LibraryImportResult(int Added, int Linked, int WithoutMetadata, int SkippedEpisodes, int Failed, IReadOnlyList<string> Messages);

/// <summary>Ajoute au catalogue les fichiers trouvés par le scan : identification TMDb, fichier, jaquette locale, propriétés techniques.</summary>
public sealed class LibraryImportService(
    TitleRepository titles,
    MediaFileRepository files,
    ImageStore images,
    MetadataService? metadataService,
    Func<MediaProbe> probeFactory,
    ILogger<LibraryImportService> logger)
{
    public void RememberFolder(string folder) => files.AddScanRoot(folder, "movies");

    /// <summary>
    /// Vérifie tous les fichiers ; ceux qui manquent sont recherchés (même nom, même taille) dans les dossiers surveillés.
    /// Renvoie (manquants restants, retrouvés).
    /// </summary>
    public Task<(int Missing, int Relinked)> CheckFilesAsync(CancellationToken cancellationToken = default) => Task.Run(
        () =>
        {
            var missing = files.CheckMissing();
            if (missing.Count == 0)
            {
                return (0, 0);
            }

            var found = files.ScanRoots()
                .Where(r => Directory.Exists(r.Path))
                .SelectMany(r => LibraryScanner.Scan(r.Path, new ScanOptions { MinimumBytes = 0 }, cancellationToken: cancellationToken))
                .Select(c => (c.FullPath, c.SizeBytes));
            var relinked = files.RelinkMoved(found);
            return (missing.Count - relinked, relinked);
        },
        cancellationToken);

    public Task<IReadOnlyList<ScanCandidate>> ScanAsync(string folder, ScanOptions options, CancellationToken cancellationToken = default)
    {
        var known = files.KnownPaths();
        return Task.Run(() => LibraryScanner.Scan(folder, options, known, cancellationToken), cancellationToken);
    }

    public async Task<LibraryImportResult> ImportAsync(
        IReadOnlyList<ScanCandidate> candidates,
        LibraryImportOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);
        int added = 0, linked = 0, manual = 0, episodes = 0, failed = 0;
        var messages = new List<string>();
        var useTmdb = options.UseTmdb && metadataService is not null;
        var probe = options.ReadTechnicalProperties ? probeFactory() : null;
        if (probe is { IsAvailable: false })
        {
            messages.Add("ffprobe introuvable : propriétés techniques non lues (installez FFmpeg ou indiquez son chemin dans les réglages).");
            probe = null;
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[i];
            progress?.Report($"{i + 1}/{candidates.Count} : {candidate.BestTitle}");
            if (candidate.Parsed.IsEpisode)
            {
                episodes++;
                continue;
            }

            try
            {
                long titleId;
                var choice = useTmdb ? await IdentifyAsync(candidate, options, cancellationToken).ConfigureAwait(false) : null;
                if (choice is not null)
                {
                    var (id, created) = await metadataService!.ImportAsync(choice, ImportOptions.Default, cancellationToken).ConfigureAwait(false);
                    titleId = id;
                    if (created)
                    {
                        added++;
                    }
                    else
                    {
                        linked++;
                    }
                }
                else
                {
                    titleId = titles.Add(new NewTitle(TitleKind.Movie, candidate.BestTitle, candidate.BestYear));
                    manual++;
                }

                var fileId = files.Attach(titleId, candidate.FullPath, candidate.SizeBytes, candidate.Parsed.Part ?? 1);
                if (options.UseLocalCover && candidate.LocalCoverPath is { } cover && !images.HasImage(titleId, "poster"))
                {
                    TryAttachCover(titleId, cover);
                }

                if (probe is not null && await probe.ProbeAsync(candidate.FullPath, cancellationToken).ConfigureAwait(false) is { } props)
                {
                    files.SaveProperties(fileId, ToProperties(props));
                }

                if (options.WriteNfo && titles.Get(titleId) is { } detail)
                {
                    NfoReader.WriteIfMissing(candidate.FullPath, detail.Title, detail.Year, detail.ImdbId, detail.TmdbId);
                }
            }
            catch (TmdbAuthenticationException ex)
            {
                // Sans jeton valide, on continue sans TMDb plutôt que d'échouer sur chaque fichier.
                messages.Add(ex.Message);
                useTmdb = false;
                i--;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TmdbUnavailableException or HttpRequestException or InvalidDataException or KeyNotFoundException)
            {
                failed++;
                messages.Add($"{Path.GetFileName(candidate.FullPath)} : {ex.Message}");
                logger.LogWarning(ex, "Import impossible : {Path}", candidate.FullPath);
            }
        }

        return new LibraryImportResult(added, linked, manual, episodes, failed, messages);
    }

    /// <summary>
    /// Identification : identifiant TMDb ou IMDb du .nfo ou du nom de fichier en priorité, sinon recherche
    /// titre + année. Un résultat n'est retenu que si l'année concorde (à un an près) quand elle est connue.
    /// </summary>
    internal async Task<MetadataSearchResult?> IdentifyAsync(ScanCandidate candidate, LibraryImportOptions options, CancellationToken cancellationToken)
    {
        if (candidate.Nfo?.TmdbId is { } tmdbId)
        {
            return (await metadataService!.SearchAsync($"movie/{tmdbId}", cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        }

        if (candidate.ImdbId is { } imdb)
        {
            var byImdb = await metadataService!.SearchAsync(imdb, cancellationToken).ConfigureAwait(false);
            if (byImdb.Count > 0)
            {
                return byImdb[0];
            }
        }

        var query = candidate.BestYear is { } y ? $"{candidate.BestTitle} ({y})" : candidate.BestTitle;
        var results = (await metadataService!.SearchAsync(query, cancellationToken).ConfigureAwait(false))
            .Where(r => r.Kind == TitleKind.Movie)
            .ToList();
        var plausible = candidate.BestYear is { } year
            ? results.Where(r => r.Year is { } ry && Math.Abs(ry - year) <= 1).ToList()
            : results;
        return plausible.Count == 1 || (plausible.Count > 1 && options.UseFirstMatch) ? plausible[0] : null;
    }

    private void TryAttachCover(long titleId, string cover)
    {
        try
        {
            var info = new FileInfo(cover);
            if (info.Length <= ImageStore.MaxBytes)
            {
                images.Attach(titleId, images.Save(File.ReadAllBytes(cover)), "poster");
            }
        }
        catch (InvalidDataException)
        {
            // Fichier .jpg qui n'est pas une image : ignoré.
        }
    }

    internal static MediaFileProperties ToProperties(MediaProperties p) => new(
        p.Container,
        p.VideoCodec,
        p.Width,
        p.Height,
        p.FrameRate,
        p.BitDepth,
        p.AspectRatio,
        p.HdrFormats,
        p.DurationSeconds,
        p.Audio.Select(a => (a.Codec, a.Channels, a.Language)).ToList(),
        p.SubtitleLanguages);
}
