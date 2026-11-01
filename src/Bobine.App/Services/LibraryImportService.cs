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

    /// <summary>Compléter les fiches avec le .nfo trouvé à côté du fichier (si le réglage du catalogue l'autorise).</summary>
    public bool ReadNfo { get; init; } = true;
}

/// <summary>Bilan d'un import ; Episodes = épisodes rattachés (ou laissés de côté si les séries ne sont pas gérées).</summary>
public sealed record LibraryImportResult(int Added, int Linked, int WithoutMetadata, int Episodes, int Failed, IReadOnlyList<string> Messages, int FromNfo = 0);

/// <summary>Ajoute au catalogue les fichiers trouvés par le scan : identification TMDb, fichier, jaquette locale, propriétés techniques.</summary>
public sealed class LibraryImportService(
    TitleRepository titles,
    MediaFileRepository files,
    ImageStore images,
    MetadataService? metadataService,
    Func<MediaProbe> probeFactory,
    ILogger<LibraryImportService> logger,
    SeriesRepository? series = null,
    NfoService? nfo = null)
{
    public void RememberFolder(string folder) => files.AddScanRoot(folder, "movies");

    /// <summary>Relit avec MediaInfo les propriétés techniques des fichiers des titres donnés ; renvoie le nombre de fichiers lus.</summary>
    public async Task<(int Read, string Tool)> RefreshTechnicalAsync(IReadOnlyList<long> titleIds, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(titleIds);
        var probe = probeFactory();
        if (!probe.IsAvailable)
        {
            return (0, probe.ToolName);
        }

        var read = 0;
        var targets = titleIds.SelectMany(files.FilesOf).Where(f => File.Exists(f.FullPath)).ToList();
        foreach (var file in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"{probe.ToolName} {read + 1}/{targets.Count} : {Path.GetFileName(file.FullPath)}");
            if (await probe.ProbeAsync(file.FullPath, cancellationToken).ConfigureAwait(false) is { } props)
            {
                files.SaveProperties(file.Id, ToProperties(props));
                read++;
            }
        }

        return (read, probe.ToolName);
    }

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
        int added = 0, linked = 0, manual = 0, episodes = 0, failed = 0, fromNfo = 0;
        var readNfo = nfo is not null && options.ReadNfo && nfo.Settings.ReadOnScan;
        var messages = new List<string>();
        var useTmdb = options.UseTmdb && metadataService is not null;
        var probe = options.ReadTechnicalProperties ? probeFactory() : null;
        if (probe is { IsAvailable: false })
        {
            messages.Add("MediaInfo introuvable (ni ffprobe) : propriétés techniques non lues. Indiquez le chemin de MediaInfo dans les réglages.");
            probe = null;
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[i];
            progress?.Report($"{i + 1}/{candidates.Count} : {candidate.BestTitle}");
            if (candidate.Parsed.IsEpisode)
            {
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
                else if (readNfo && candidate.Nfo is { } known && nfo!.FindExisting(known) is { } existing)
                {
                    // Même film (identifiant TMDb du .nfo) déjà au catalogue : on y rattache ce fichier, sans doublon.
                    titleId = existing;
                    linked++;
                }
                else
                {
                    titleId = titles.Add(new NewTitle(TitleKind.Movie, candidate.BestTitle, candidate.BestYear));
                    manual++;
                    if (readNfo && candidate.Nfo is { } info)
                    {
                        // Le .nfo complète la fiche ; ses identifiants servent ensuite à « Mettre à jour via TMDb ».
                        nfo!.ApplyToTitle(titleId, info);
                        fromNfo++;
                    }
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

                if (options.WriteNfo && nfo is not null)
                {
                    nfo.WriteForTitle(titleId);
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

        var episodeCandidates = candidates.Where(c => c.Parsed.IsEpisode).ToList();
        if (series is null)
        {
            episodes = episodeCandidates.Count;
        }
        else
        {
            // Les épisodes sont regroupés par série (titre déduit du nom de fichier), puis rattachés un par un.
            foreach (var group in episodeCandidates.GroupBy(c => c.Parsed.Title, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Série : {group.Key}");
                try
                {
                    var titleId = await FindOrCreateSeriesAsync(group.Key, useTmdb, cancellationToken).ConfigureAwait(false);
                    foreach (var episode in group)
                    {
                        var episodeId = series.FindEpisode(titleId, episode.Parsed.Season!.Value, episode.Parsed.Episode!.Value)
                            ?? series.EnsureEpisode(titleId, episode.Parsed.Season.Value, episode.Parsed.Episode.Value);
                        files.AttachEpisode(episodeId, episode.FullPath, episode.SizeBytes, episode.Parsed.Part ?? 1);
                        episodes++;
                    }
                }
                catch (Exception ex) when (ex is IOException or TmdbUnavailableException or TmdbAuthenticationException or HttpRequestException or KeyNotFoundException)
                {
                    failed += group.Count();
                    messages.Add($"{group.Key} : {ex.Message}");
                }
            }
        }

        return new LibraryImportResult(added, linked, manual, episodes, failed, messages, fromNfo);
    }

    private async Task<long> FindOrCreateSeriesAsync(string name, bool useTmdb, CancellationToken cancellationToken)
    {
        if (useTmdb)
        {
            var match = (await metadataService!.SearchAsync(name, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(r => r.Kind == TitleKind.Series);
            if (match is not null)
            {
                return (await metadataService.ImportAsync(match, ImportOptions.Default, cancellationToken).ConfigureAwait(false)).TitleId;
            }
        }

        var existing = titles.Query(new TitleQuery(Filter: QuickFilter.Series))
            .FirstOrDefault(t => string.Equals(t.Title, name, StringComparison.OrdinalIgnoreCase));
        return existing?.Id ?? titles.Add(new NewTitle(TitleKind.Series, name));
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
