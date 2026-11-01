using Dapper;
using Microsoft.Data.Sqlite;

namespace Bobine.Data;

public sealed record ScanRoot(long Id, string Path, string Kind, bool ScanOnStartup);

public sealed record MediaFileEntry(long Id, long TitleId, string FullPath, int Part, bool IsMissing, long? SizeBytes);

/// <summary>Propriétés techniques à enregistrer, indépendantes de l'outil qui les a lues.</summary>
public sealed record MediaFileProperties(
    string? Container,
    string? VideoCodec,
    int? Width,
    int? Height,
    double? FrameRate,
    int? BitDepth,
    string? AspectRatio,
    IReadOnlyList<string> HdrFormats,
    int? DurationSeconds,
    IReadOnlyList<(string? Codec, int? Channels, string? Language)> Audio,
    IReadOnlyList<string> SubtitleLanguages);

/// <summary>
/// Fichiers vidéo rattachés aux titres. Un fichier situé sous un dossier surveillé est stocké relativement
/// à ce dossier : déplacer la bibliothèque ou changer de lettre de lecteur ne demande qu'une mise à jour du dossier.
/// </summary>
public sealed class MediaFileRepository(CatalogDatabase database)
{
    public long AddScanRoot(string path, string kind, bool scanOnStartup = false)
    {
        var full = Normalize(path);
        if (kind is not ("movies" or "series"))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        using var connection = database.Open();
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO scan_root (path, kind, scan_on_startup) VALUES (@full, @kind, @scanOnStartup)
            ON CONFLICT (path) DO UPDATE SET kind = excluded.kind, scan_on_startup = excluded.scan_on_startup
            RETURNING id;
            """,
            new { full, kind, scanOnStartup });
    }

    public IReadOnlyList<ScanRoot> ScanRoots()
    {
        using var connection = database.Open();
        return connection.Query<(long Id, string Path, string Kind, long Startup)>("SELECT id, path, kind, scan_on_startup FROM scan_root ORDER BY path;")
            .Select(r => new ScanRoot(r.Id, r.Path, r.Kind, r.Startup != 0))
            .ToList();
    }

    public void RemoveScanRoot(long id)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        // Les fichiers gardent un chemin absolu valable avant que leur dossier disparaisse.
        connection.Execute(
            "UPDATE media_file SET path = (SELECT r.path FROM scan_root r WHERE r.id = @id) || '/' || path, scan_root_id = NULL WHERE scan_root_id = @id;",
            new { id },
            transaction);
        connection.Execute("DELETE FROM scan_root WHERE id = @id;", new { id }, transaction);
        transaction.Commit();
    }

    /// <summary>Change l'emplacement d'un dossier surveillé (disque déplacé, autre lettre de lecteur).</summary>
    public void MoveScanRoot(long id, string newPath)
    {
        using var connection = database.Open();
        connection.Execute("UPDATE scan_root SET path = @path WHERE id = @id;", new { id, path = Normalize(newPath) });
    }

    /// <summary>Chemins absolus de tous les fichiers connus, pour ne pas réimporter un fichier.</summary>
    public HashSet<string> KnownPaths()
    {
        using var connection = database.Open();
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return new HashSet<string>(All(connection).Select(f => f.FullPath), comparer);
    }

    /// <summary>Rattache un fichier à l'exemplaire principal d'un titre ; renvoie l'identifiant du fichier.</summary>
    public long Attach(long titleId, string fullPath, long? sizeBytes, int part = 1)
    {
        var full = Normalize(fullPath);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var copyId = connection.ExecuteScalar<long?>("SELECT min(id) FROM copy WHERE title_id = @titleId;", new { titleId }, transaction)
            ?? connection.ExecuteScalar<long>("INSERT INTO copy (title_id) VALUES (@titleId) RETURNING id;", new { titleId }, transaction);
        connection.Execute(
            "UPDATE copy SET medium_id = (SELECT id FROM ref_list WHERE kind = 'medium' AND code = 'media_file') WHERE id = @copyId AND medium_id IS NULL;",
            new { copyId },
            transaction);

        var (rootId, stored) = Split(connection, transaction, full);
        var id = connection.ExecuteScalar<long>(
            """
            INSERT INTO media_file (copy_id, scan_root_id, path, part_number, size_bytes, last_seen_at, is_missing)
            VALUES (@copyId, @rootId, @stored, @part, @sizeBytes, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'), 0)
            ON CONFLICT DO UPDATE SET copy_id = excluded.copy_id, size_bytes = excluded.size_bytes, is_missing = 0,
                last_seen_at = excluded.last_seen_at
            RETURNING id;
            """,
            new { copyId, rootId, stored, part, sizeBytes },
            transaction);
        transaction.Commit();
        return id;
    }

    public IReadOnlyList<MediaFileEntry> FilesOf(long titleId)
    {
        using var connection = database.Open();
        return All(connection, "WHERE m.copy_id IS NOT NULL AND c.title_id = @titleId", new { titleId });
    }

    /// <summary>Rattache un fichier à un épisode ; relatif au dossier surveillé qui le contient, comme pour les films.</summary>
    public long AttachEpisode(long episodeId, string fullPath, long? sizeBytes, int part = 1)
    {
        var full = Normalize(fullPath);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var (rootId, stored) = Split(connection, transaction, full);
        var id = connection.ExecuteScalar<long>(
            """
            INSERT INTO media_file (episode_id, scan_root_id, path, part_number, size_bytes, last_seen_at, is_missing)
            VALUES (@episodeId, @rootId, @stored, @part, @sizeBytes, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'), 0)
            ON CONFLICT DO UPDATE SET episode_id = excluded.episode_id, copy_id = NULL, size_bytes = excluded.size_bytes, is_missing = 0
            RETURNING id;
            """,
            new { episodeId, rootId, stored, part, sizeBytes },
            transaction);
        transaction.Commit();
        return id;
    }

    /// <summary>Résumé lisible des propriétés techniques du premier fichier d'un titre, ou null s'il n'a pas été analysé.</summary>
    public string? TechnicalSummary(long titleId)
    {
        using var connection = database.Open();
        var row = connection.QueryFirstOrDefault<(long Id, string? Container, string? Codec, long? Width, long? Height, double? Fps, long? Bits, string? Aspect, string? Hdr, long? Duration)>(
            """
            SELECT m.id, m.container, m.video_codec, m.width, m.height, m.frame_rate, m.bit_depth, m.aspect_ratio, m.hdr_formats, m.duration_seconds
            FROM media_file m JOIN copy c ON c.id = m.copy_id
            WHERE c.title_id = @titleId AND (m.video_codec IS NOT NULL OR m.container IS NOT NULL)
            ORDER BY m.part_number LIMIT 1;
            """,
            new { titleId });
        if (row.Id == 0)
        {
            return null;
        }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var parts = new List<string?>
        {
            row.Container,
            row.Codec,
            row.Width is null ? null : $"{row.Width}×{row.Height}",
            row.Aspect,
            row.Fps is null ? null : row.Fps.Value.ToString("0.###", inv) + " i/s",
            row.Bits is null ? null : $"{row.Bits} bits",
            row.Hdr,
            row.Duration is null ? null : $"{row.Duration / 3600} h {row.Duration % 3600 / 60:00}",
        };
        var audio = connection.Query<(string? Language, string? Channels)>(
            "SELECT language, channels FROM audio_track WHERE media_file_id = @Id ORDER BY position;", new { row.Id })
            .Select(a => $"{a.Channels} {a.Language}".Trim())
            .ToList();
        var subs = connection.Query<string>("SELECT language FROM subtitle_track WHERE media_file_id = @Id ORDER BY position;", new { row.Id }).ToList();
        var text = string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        if (audio.Count > 0)
        {
            text += "\nAudio : " + string.Join(", ", audio);
        }

        if (subs.Count > 0)
        {
            text += "\nSous-titres : " + string.Join(", ", subs);
        }

        return text;
    }

    public void SaveProperties(long mediaFileId, MediaFileProperties p)
    {
        ArgumentNullException.ThrowIfNull(p);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute(
            """
            UPDATE media_file SET container = @Container, video_codec = @VideoCodec, width = @Width, height = @Height,
                frame_rate = @FrameRate, bit_depth = @BitDepth, aspect_ratio = @AspectRatio, hdr_formats = @Hdr,
                duration_seconds = @DurationSeconds
            WHERE id = @mediaFileId;
            """,
            new
            {
                mediaFileId,
                p.Container,
                p.VideoCodec,
                p.Width,
                p.Height,
                p.FrameRate,
                p.BitDepth,
                p.AspectRatio,
                Hdr = p.HdrFormats.Count == 0 ? null : string.Join(',', p.HdrFormats),
                p.DurationSeconds,
            },
            transaction);
        connection.Execute("DELETE FROM audio_track WHERE media_file_id = @mediaFileId;", new { mediaFileId }, transaction);
        connection.Execute(
            "INSERT INTO audio_track (media_file_id, language, channels, position, format_id) VALUES (@mediaFileId, @Language, @Channels, @Position, NULL);",
            p.Audio.Select((a, i) => new { mediaFileId, a.Language, Channels = $"{a.Codec} {a.Channels}ch".Trim(), Position = i }),
            transaction);
        connection.Execute("DELETE FROM subtitle_track WHERE media_file_id = @mediaFileId;", new { mediaFileId }, transaction);
        connection.Execute(
            "INSERT INTO subtitle_track (media_file_id, language, position) VALUES (@mediaFileId, @Language, @Position);",
            p.SubtitleLanguages.Select((l, i) => new { mediaFileId, Language = l, Position = i }),
            transaction);

        // La durée du fichier complète la fiche quand TMDb ne l'a pas donnée.
        if (p.DurationSeconds is > 0)
        {
            connection.Execute(
                """
                UPDATE title SET runtime_minutes = @Minutes
                WHERE runtime_minutes IS NULL AND id = (SELECT c.title_id FROM media_file m JOIN copy c ON c.id = m.copy_id WHERE m.id = @mediaFileId);
                """,
                new { mediaFileId, Minutes = (int)Math.Round(p.DurationSeconds.Value / 60.0) },
                transaction);
        }

        transaction.Commit();
    }

    /// <summary>Vérifie la présence de chaque fichier ; renvoie les fichiers absents.</summary>
    public IReadOnlyList<MediaFileEntry> CheckMissing()
    {
        using var connection = database.Open();
        var files = All(connection);
        using var transaction = connection.BeginTransaction();
        var missing = new List<MediaFileEntry>();
        foreach (var file in files)
        {
            var exists = File.Exists(file.FullPath);
            if (!exists)
            {
                missing.Add(file with { IsMissing = true });
            }

            connection.Execute(
                exists
                    ? "UPDATE media_file SET is_missing = 0, last_seen_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now') WHERE id = @Id;"
                    : "UPDATE media_file SET is_missing = 1 WHERE id = @Id;",
                new { file.Id },
                transaction);
        }

        transaction.Commit();
        return missing;
    }

    /// <summary>Retrouve les fichiers absents déplacés ailleurs, par nom et taille identiques ; renvoie le nombre corrigé.</summary>
    public int RelinkMoved(IEnumerable<(string FullPath, long Size)> foundFiles)
    {
        ArgumentNullException.ThrowIfNull(foundFiles);
        var byKey = foundFiles
            .GroupBy(f => (Path.GetFileName(f.FullPath).ToUpperInvariant(), f.Size))
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().FullPath);
        using var connection = database.Open();
        var fixedCount = 0;
        foreach (var file in All(connection).Where(f => f.IsMissing && f.SizeBytes is not null))
        {
            if (byKey.TryGetValue((Path.GetFileName(file.FullPath).ToUpperInvariant(), file.SizeBytes!.Value), out var newPath))
            {
                using var transaction = connection.BeginTransaction();
                var (rootId, stored) = Split(connection, transaction, newPath);
                connection.Execute(
                    "UPDATE media_file SET scan_root_id = @rootId, path = @stored, is_missing = 0 WHERE id = @Id;",
                    new { rootId, stored, file.Id },
                    transaction);
                transaction.Commit();
                fixedCount++;
            }
        }

        return fixedCount;
    }

    /// <summary>Remplace un début de chemin absolu (ex. « D:\Films » par « E:\Films ») ; renvoie le nombre modifié.</summary>
    public int ReplacePathPrefix(string oldPrefix, string newPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPrefix);
        using var connection = database.Open();
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var count = 0;
        using var transaction = connection.BeginTransaction();
        foreach (var (id, path) in connection.Query<(long, string)>("SELECT id, path FROM media_file WHERE scan_root_id IS NULL;", transaction: transaction))
        {
            if (path.StartsWith(oldPrefix, comparison))
            {
                connection.Execute("UPDATE media_file SET path = @p WHERE id = @id;", new { id, p = newPrefix + path[oldPrefix.Length..] }, transaction);
                count++;
            }
        }

        foreach (var (id, path) in connection.Query<(long, string)>("SELECT id, path FROM scan_root;", transaction: transaction))
        {
            if (path.StartsWith(oldPrefix, comparison))
            {
                connection.Execute("UPDATE scan_root SET path = @p WHERE id = @id;", new { id, p = newPrefix + path[oldPrefix.Length..] }, transaction);
                count++;
            }
        }

        transaction.Commit();
        return count;
    }

    private static List<MediaFileEntry> All(SqliteConnection connection, string where = "", object? param = null) =>
        connection.Query<(long Id, long TitleId, string Path, string? Root, long Part, long Missing, long? Size)>(
                $"""
                SELECT m.id, coalesce(c.title_id, s.title_id), m.path, r.path, m.part_number, m.is_missing, m.size_bytes
                FROM media_file m
                LEFT JOIN copy c ON c.id = m.copy_id
                LEFT JOIN episode e ON e.id = m.episode_id
                LEFT JOIN season s ON s.id = e.season_id
                LEFT JOIN scan_root r ON r.id = m.scan_root_id
                {where} ORDER BY m.part_number;
                """,
                param)
            .Select(f => new MediaFileEntry(
                f.Id,
                f.TitleId,
                f.Root is null ? f.Path : System.IO.Path.GetFullPath(System.IO.Path.Combine(f.Root, f.Path)),
                (int)f.Part,
                f.Missing != 0,
                f.Size))
            .ToList();

    /// <summary>Trouve le dossier surveillé le plus précis contenant le fichier ; sinon chemin absolu.</summary>
    private static (long? RootId, string Stored) Split(SqliteConnection connection, SqliteTransaction transaction, string full)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var root = connection.Query<(long Id, string Path)>("SELECT id, path FROM scan_root;", transaction: transaction)
            .Where(r => full.StartsWith(r.Path.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, comparison))
            .OrderByDescending(r => r.Path.Length)
            .Select(r => ((long, string)?)r)
            .FirstOrDefault();
        return root is { } r2
            ? (r2.Item1, System.IO.Path.GetRelativePath(r2.Item2, full))
            : (null, full);
    }

    private static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar);
    }
}
