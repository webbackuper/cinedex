using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Cinedex.Data;

namespace Cinedex.App.Services;

/// <summary>Fréquence des sauvegardes automatiques.</summary>
public enum BackupSchedule
{
    None,
    AtStartup,
    Daily,
    Weekly,
    Monthly,
}

/// <summary>Réglages des sauvegardes (dumps) du catalogue : contenu, dossier, planification, nombre gardé.</summary>
public sealed record BackupSettings
{
    public const string FolderKey = "backup.folder";
    public const string DatabaseKey = "backup.includeDatabase";
    public const string ImagesKey = "backup.includeImages";
    public const string PreferencesKey = "backup.includePreferences";
    public const string ScheduleKey = "backup.schedule";
    public const string KeepKey = "backup.keep";
    public const string LastRunKey = "backup.lastRun";

    /// <summary>Dossier des sauvegardes ; vide : sous-dossier « dumps » des sauvegardes de Cinedex.</summary>
    public string? Folder { get; init; }

    /// <summary>Base seule : fiches, notes, prêts, fichiers, réglages du catalogue.</summary>
    public bool IncludeDatabase { get; init; } = true;

    /// <summary>Images : affiches, fonds, photos des acteurs, décors des collections.</summary>
    public bool IncludeImages { get; init; } = true;

    /// <summary>Préférences communes (apparence, langue, fenêtre, journal).</summary>
    public bool IncludePreferences { get; init; } = true;

    public BackupSchedule Schedule { get; init; } = BackupSchedule.None;

    /// <summary>Nombre de sauvegardes gardées dans le dossier ; les plus anciennes sont effacées (0 : toutes gardées).</summary>
    public int Keep { get; init; } = 10;

    public DateTime? LastRunUtc { get; init; }

    public static BackupSettings Load(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new BackupSettings
        {
            Folder = settings.Get(FolderKey) is { Length: > 0 } folder ? folder : null,
            IncludeDatabase = settings.Get(DatabaseKey) != "false",
            IncludeImages = settings.Get(ImagesKey) != "false",
            IncludePreferences = settings.Get(PreferencesKey) != "false",
            Schedule = Enum.TryParse<BackupSchedule>(settings.Get(ScheduleKey), out var schedule) ? schedule : BackupSchedule.None,
            Keep = int.TryParse(settings.Get(KeepKey), NumberStyles.None, CultureInfo.InvariantCulture, out var keep) ? Math.Clamp(keep, 0, 999) : 10,
            LastRunUtc = DateTime.TryParse(settings.Get(LastRunKey), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var last) ? last : null,
        };
    }

    public void Save(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Set(FolderKey, Folder ?? string.Empty);
        settings.Set(DatabaseKey, ScanSettings.Flag(IncludeDatabase));
        settings.Set(ImagesKey, ScanSettings.Flag(IncludeImages));
        settings.Set(PreferencesKey, ScanSettings.Flag(IncludePreferences));
        settings.Set(ScheduleKey, Schedule.ToString());
        settings.Set(KeepKey, Math.Clamp(Keep, 0, 999).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Une sauvegarde automatique est-elle due (au démarrage, ou selon l'ancienneté de la dernière) ?</summary>
    public bool IsDue(DateTime nowUtc, bool atStartup) => Schedule switch
    {
        BackupSchedule.AtStartup => atStartup,
        BackupSchedule.Daily => LastRunUtc is not { } d || nowUtc - d >= TimeSpan.FromDays(1),
        BackupSchedule.Weekly => LastRunUtc is not { } w || nowUtc - w >= TimeSpan.FromDays(7),
        BackupSchedule.Monthly => LastRunUtc is not { } m || nowUtc - m >= TimeSpan.FromDays(30),
        _ => false,
    } && (IncludeDatabase || IncludeImages || IncludePreferences);
}

/// <summary>Une sauvegarde présente dans le dossier : fichier, date, taille et contenu.</summary>
public sealed record DumpInfo(string Path, DateTime CreatedUtc, long SizeBytes, bool HasDatabase, bool HasImages, bool HasPreferences, int? Titles)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// Sauvegardes complètes (dumps) du catalogue ouvert : une archive .zip qui contient, au choix, la base
/// (copie cohérente par l'API de sauvegarde de SQLite), les images et les préférences communes, avec un
/// manifeste. Création à la demande ou planifiée, rotation des anciennes, restauration.
/// </summary>
public sealed class BackupService(MaintenanceRepository maintenance, SettingsStore settings, string catalog, string dataRoot, string backupsDirectory, string imagesDirectory)
{
    private const string Manifest = "manifest.json";
    private const string DatabaseEntry = "database/cinedex.db";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Catalog => catalog;

    public string DefaultFolder => Path.Combine(backupsDirectory, "dumps");

    public BackupSettings LoadSettings() => BackupSettings.Load(settings);

    public void SaveSettings(BackupSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Save(settings);
    }

    public string FolderOf(BackupSettings value) => value?.Folder is { Length: > 0 } folder ? folder : DefaultFolder;

    /// <summary>Crée une sauvegarde selon les réglages donnés ; renvoie le chemin de l'archive.</summary>
    public string CreateDump(BackupSettings options, IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IncludeDatabase && !options.IncludeImages && !options.IncludePreferences)
        {
            throw new InvalidOperationException(Cinedex.Core.Localization.Tr.T("Choisissez au moins un contenu à sauvegarder."));
        }

        var folder = FolderOf(options);
        Directory.CreateDirectory(folder);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var target = Path.Combine(folder, $"cinedex-{Safe(catalog)}-{stamp}.zip");
        var temp = target + ".tmp";
        var work = Path.Combine(Path.GetTempPath(), "cinedex-dump-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                if (options.IncludeDatabase)
                {
                    progress?.Report(Cinedex.Core.Localization.Tr.T("Copie de la base…"));
                    var copy = maintenance.Backup(work, "dump");
                    zip.CreateEntryFromFile(copy, DatabaseEntry, CompressionLevel.Optimal);
                }

                var images = 0;
                if (options.IncludeImages && Directory.Exists(imagesDirectory))
                {
                    progress?.Report(Cinedex.Core.Localization.Tr.T("Copie des images…"));
                    foreach (var file in Directory.EnumerateFiles(imagesDirectory, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(imagesDirectory, file).Replace('\\', '/');
                        // Images déjà compressées (JPEG, PNG, WebP) : stockées telles quelles, bien plus vite.
                        zip.CreateEntryFromFile(file, "images/" + relative, CompressionLevel.NoCompression);
                        images++;
                    }
                }

                if (options.IncludePreferences && Directory.Exists(dataRoot))
                {
                    foreach (var file in Directory.EnumerateFiles(dataRoot, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        zip.CreateEntryFromFile(file, "preferences/" + Path.GetFileName(file), CompressionLevel.Optimal);
                    }
                }

                var info = maintenance.Info();
                var manifest = new DumpManifest(
                    typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "?",
                    catalog,
                    DateTime.UtcNow,
                    options.IncludeDatabase,
                    options.IncludeImages,
                    options.IncludePreferences,
                    info.Titles,
                    images);
                using var stream = zip.CreateEntry(Manifest).Open();
                JsonSerializer.Serialize(stream, manifest, Json);
            }

            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            if (Directory.Exists(work))
            {
                Directory.Delete(work, recursive: true);
            }
        }

        settings.Set(BackupSettings.LastRunKey, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        Prune(folder, options.Keep);
        return target;
    }

    /// <summary>Sauvegardes du catalogue présentes dans le dossier, de la plus récente à la plus ancienne.</summary>
    public IReadOnlyList<DumpInfo> List(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(folder, $"cinedex-{Safe(catalog)}-*.zip")
            .Select(Describe)
            .OfType<DumpInfo>()
            .OrderByDescending(d => d.CreatedUtc)];
    }

    /// <summary>Lit le manifeste d'une sauvegarde ; null si ce n'est pas une sauvegarde Cinedex lisible.</summary>
    public static DumpInfo? Describe(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var manifest = zip.GetEntry(Manifest) is { } entry
                ? JsonSerializer.Deserialize<DumpManifest>(entry.Open())
                : null;
            return new DumpInfo(
                path,
                manifest?.CreatedUtc ?? File.GetLastWriteTimeUtc(path),
                new FileInfo(path).Length,
                zip.GetEntry(DatabaseEntry) is not null,
                zip.Entries.Any(e => e.FullName.StartsWith("images/", StringComparison.Ordinal)),
                zip.Entries.Any(e => e.FullName.StartsWith("preferences/", StringComparison.Ordinal)),
                manifest?.Titles);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Lance la sauvegarde planifiée si elle est due ; renvoie son chemin, ou null.</summary>
    public string? RunIfDue(bool atStartup)
    {
        var options = LoadSettings();
        return options.IsDue(DateTime.UtcNow, atStartup) ? CreateDump(options) : null;
    }

    /// <summary>
    /// Restaure une sauvegarde dans le catalogue (fermé) : la base est remplacée, après une copie de sécurité de
    /// l'actuelle ; les images de la sauvegarde sont ajoutées à celles du catalogue (rangées par empreinte,
    /// elles ne se remplacent jamais à tort). Les préférences communes ne sont pas touchées.
    /// </summary>
    public static string? Restore(string dumpPath, string databasePath, string imagesDir, string safetyDirectory)
    {
        using var zip = ZipFile.OpenRead(dumpPath);
        string? safety = null;
        if (zip.GetEntry(DatabaseEntry) is { } database)
        {
            Directory.CreateDirectory(safetyDirectory);
            safety = Path.Combine(safetyDirectory, $"{Path.GetFileNameWithoutExtension(databasePath)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-avant-restauration.db");
            if (File.Exists(databasePath))
            {
                File.Copy(databasePath, safety, overwrite: true);
            }

            var temp = databasePath + ".restore";
            database.ExtractToFile(temp, overwrite: true);
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                if (File.Exists(databasePath + suffix))
                {
                    File.Delete(databasePath + suffix);
                }
            }

            File.Move(temp, databasePath, overwrite: true);
        }

        var root = Path.GetFullPath(imagesDir) + Path.DirectorySeparatorChar;
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("images/", StringComparison.Ordinal) && e.Name.Length > 0))
        {
            var target = Path.GetFullPath(Path.Combine(imagesDir, entry.FullName["images/".Length..]));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue; // Chemin hors du dossier des images : ignoré.
            }

            if (!File.Exists(target))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target);
            }
        }

        return safety;
    }

    /// <summary>Garde les <paramref name="keep"/> sauvegardes les plus récentes du catalogue (0 : toutes).</summary>
    private void Prune(string folder, int keep)
    {
        if (keep <= 0)
        {
            return;
        }

        foreach (var old in Directory.EnumerateFiles(folder, $"cinedex-{Safe(catalog)}-*.zip").OrderByDescending(f => f, StringComparer.Ordinal).Skip(keep))
        {
            File.Delete(old);
        }
    }

    private static string Safe(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));

    private sealed record DumpManifest(string Version, string Catalog, DateTime CreatedUtc, bool Database, bool Images, bool Preferences, int Titles, int ImageFiles);
}
