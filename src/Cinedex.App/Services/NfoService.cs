using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Media;
using Microsoft.Extensions.Logging;
using Cinedex.Core.Localization;

namespace Cinedex.App.Services;

/// <summary>Réglages .nfo du catalogue.</summary>
public sealed record NfoSettings(bool ReadOnScan, bool WriteAfterMetadata, bool OverwriteExisting, bool Backup)
{
    public const string ReadKey = "nfo.readOnScan";
    public const string WriteKey = "nfo.writeAfterMetadata";
    public const string OverwriteKey = "nfo.overwrite";
    public const string BackupKey = "nfo.backup";

    public static NfoSettings Load(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new(
            settings.Get(ReadKey) != "false",
            settings.Get(WriteKey) != "false",
            settings.Get(OverwriteKey) == "true",
            settings.Get(BackupKey) != "false");
    }
}

/// <summary>
/// Passerelle entre les .nfo et les fiches : un .nfo lu au scan complète la fiche (sans effacer ce qu'elle a),
/// et après une mise à jour TMDb, la fiche est réécrite dans le .nfo de chaque fichier vidéo du titre.
/// </summary>
public sealed class NfoService(
    TitleRepository titles,
    MetadataRepository metadata,
    MediaFileRepository files,
    SettingsStore settings,
    ILogger<NfoService> logger)
{
    public NfoSettings Settings => NfoSettings.Load(settings);

    /// <summary>Titre déjà au catalogue pour l'identifiant TMDb du .nfo (second fichier d'un même film, par exemple).</summary>
    public long? FindExisting(NfoInfo nfo) =>
        nfo?.TmdbId is { } id ? metadata.FindBySource(TitleKind.Movie, id) : null;

    /// <summary>Applique un .nfo à une fiche existante. Les identifiants IMDb/TMDb du .nfo y sont enregistrés.</summary>
    public void ApplyToTitle(long titleId, NfoInfo nfo)
    {
        ArgumentNullException.ThrowIfNull(nfo);
        var current = titles.Get(titleId) ?? throw new KeyNotFoundException(Tr.F("Titre {0} introuvable.", titleId));
        var record = ToRecord(nfo, current);
        try
        {
            metadata.Apply(titleId, record, ImportOptions.Default with
            {
                UpdateTitle = nfo.Title is not null,
                DownloadPoster = false,
                DownloadBackdrop = false,
            });
        }
        catch (DuplicateTitleException)
        {
            // Le film du .nfo est déjà au catalogue sous une autre entrée : on applique sans l'identifiant TMDb.
            metadata.Apply(titleId, record with { SourceId = 0 }, ImportOptions.Default with { UpdateTitle = nfo.Title is not null });
        }

        var updated = titles.Get(titleId)!;
        var tags = nfo.Tags.Except(updated.Tags, StringComparer.OrdinalIgnoreCase).ToList();
        if (nfo.UserRating is { } rating && updated.UserRating is null || tags.Count > 0 || nfo.SortTitle is not null)
        {
            titles.Update(updated with
            {
                UserRating = updated.UserRating ?? (nfo.UserRating is { } r ? Math.Round(r * 2, MidpointRounding.AwayFromZero) / 2 : null),
                Tags = [.. updated.Tags, .. tags],
            });
        }

        if (nfo.Watched == true || nfo.PlayCount > 0)
        {
            titles.SetSeen([titleId], true);
        }
    }

    /// <summary>Écrit ou complète le .nfo de chaque fichier vidéo du titre ; renvoie le nombre de fichiers modifiés.</summary>
    public int WriteForTitle(long titleId, NfoWriteOptions? options = null)
    {
        var detail = titles.Get(titleId);
        if (detail is null || detail.Kind != TitleKind.Movie)
        {
            return 0;
        }

        var current = Settings;
        options ??= new NfoWriteOptions { OverwriteExisting = current.OverwriteExisting, Backup = current.Backup };
        var values = FromTitle(detail);
        var written = 0;
        foreach (var file in files.FilesOf(titleId).Where(f => File.Exists(f.FullPath)).GroupBy(f => Path.GetDirectoryName(f.FullPath)).Select(g => g.First()))
        {
            try
            {
                if (NfoWriter.Write(NfoReader.NfoPathFor(file.FullPath), values, options) is NfoWriteResult.Created or NfoWriteResult.Updated)
                {
                    written++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Dossier en lecture seule (disque optique, partage) : la fiche reste à jour dans Cinedex.
                logger.LogWarning(ex, "Écriture du .nfo impossible pour {Path}", file.FullPath);
            }
        }

        return written;
    }

    /// <summary>Appelée après une mise à jour TMDb : écrit le .nfo si le réglage l'autorise.</summary>
    public void AfterMetadataUpdate(long titleId)
    {
        if (Settings.WriteAfterMetadata)
        {
            WriteForTitle(titleId);
        }
    }

    internal NfoInfo FromTitle(TitleDetail detail)
    {
        var credits = titles.Credits(detail.Id);
        IReadOnlyList<string> Names(string role) => credits.Where(c => c.Role == role).Select(c => c.Name).ToList();
        return new NfoInfo
        {
            Title = detail.Title,
            OriginalTitle = detail.OriginalTitle,
            Year = detail.Year,
            ImdbId = detail.ImdbId,
            TmdbId = detail.TmdbId,
            Plot = detail.Overview,
            Tagline = detail.Tagline,
            RuntimeMinutes = detail.RuntimeMinutes,
            Certification = detail.Certification,
            Rating = detail.TmdbRating,
            UserRating = detail.UserRating,
            Set = detail.CollectionName,
            Trailer = detail.TrailerUrl,
            Genres = titles.GenreLabels(detail.Id),
            Countries = (detail.Countries ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Studios = detail.Studio is null ? [] : [detail.Studio],
            Tags = detail.Tags,
            Directors = Names("director"),
            Writers = Names("writer"),
            Actors = credits.Where(c => c.Role == "actor").Select((c, i) => new NfoActor(c.Name, c.Character, i)).ToList(),
        };
    }

    private static MetadataRecord ToRecord(NfoInfo nfo, TitleDetail current)
    {
        var order = 0;
        var credits = nfo.Directors.Select(d => new MetadataCredit(CreditRole.Director, 0, d, null, order++, null))
            .Concat(nfo.Writers.Select(w => new MetadataCredit(CreditRole.Writer, 0, w, null, order++, null)))
            .Concat(nfo.Actors.Select((a, i) => new MetadataCredit(CreditRole.Actor, 0, a.Name, a.Role, a.Order ?? i, null)))
            .ToList();
        return new MetadataRecord
        {
            Kind = current.Kind,
            SourceId = nfo.TmdbId ?? 0,
            ImdbId = nfo.ImdbId,
            Title = nfo.Title ?? current.Title,
            OriginalTitle = nfo.OriginalTitle ?? current.OriginalTitle,
            Year = nfo.Year,
            RuntimeMinutes = nfo.RuntimeMinutes,
            Tagline = nfo.Tagline,
            Overview = nfo.Plot ?? nfo.Outline,
            Certification = nfo.Certification,
            Countries = nfo.Countries,
            Studio = nfo.Studios.FirstOrDefault(),
            TrailerUrl = nfo.Trailer,
            Rating = nfo.Rating,
            Votes = nfo.Votes,
            GenreNames = nfo.Genres,
            Credits = credits,
            Collection = nfo.Set is null ? null : new MetadataSaga(0, nfo.Set, null, null),
        };
    }
}
