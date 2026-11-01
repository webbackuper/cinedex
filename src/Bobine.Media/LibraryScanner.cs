namespace Bobine.Media;

public sealed record ScanOptions
{
    public bool Recursive { get; init; } = true;

    /// <summary>Fichiers ignorés s'ils contiennent l'un de ces mots (bandes-annonces, échantillons...).</summary>
    public IReadOnlyList<string> IgnoreKeywords { get; init; } = ["sample", "trailer", "-ts.", "featurette"];

    public IReadOnlyList<string> NoiseWords { get; init; } = [];

    /// <summary>Ignorer le contenu entre ( ), [ ] et { } dans les noms (l'année y est lue avant).</summary>
    public bool IgnoreBracketed { get; init; } = true;

    /// <summary>Taille minimale d'un fichier vidéo retenu (les petits fichiers sont souvent des extraits).</summary>
    public long MinimumBytes { get; init; } = 20L * 1024 * 1024;

    public int MaxFiles { get; init; } = 50_000;
}

/// <summary>Fichier vidéo trouvé, avec ce qu'on en déduit localement (nom, .nfo, jaquette).</summary>
public sealed record ScanCandidate(
    string FullPath,
    long SizeBytes,
    ParsedName Parsed,
    NfoInfo? Nfo,
    string? LocalCoverPath)
{
    public string BestTitle => Nfo?.Title ?? Parsed.Title;

    public int? BestYear => Nfo?.Year ?? Parsed.Year;

    public string? ImdbId => Nfo?.ImdbId ?? Parsed.ImdbId;
}

/// <summary>
/// Parcourt un dossier à la recherche de fichiers vidéo. Ne suit pas les liens symboliques ni les points
/// de jonction (boucles, sorties de l'arborescence), ignore les dossiers système et cachés, et tolère les
/// dossiers illisibles. Les structures DVD/Blu-ray (VIDEO_TS, BDMV) donnent un seul candidat par disque.
/// </summary>
public static class LibraryScanner
{
    private static readonly string[] CoverNames = ["folder.jpg", "cover.jpg", "poster.jpg"];

    public static IReadOnlyList<ScanCandidate> Scan(string root, ScanOptions? options = null, ISet<string>? knownPaths = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        options ??= new ScanOptions();
        var rootDir = new DirectoryInfo(Path.GetFullPath(root));
        if (!rootDir.Exists)
        {
            throw new DirectoryNotFoundException($"Dossier introuvable : {root}");
        }

        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
        };

        var results = new List<ScanCandidate>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(rootDir);
        while (pending.Count > 0 && results.Count < options.MaxFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = pending.Pop();

            if (DiscStructure(dir) is { } disc)
            {
                AddCandidate(results, disc, dir.Name, options, knownPaths);
                continue;
            }

            foreach (var file in dir.EnumerateFiles("*", enumeration))
            {
                if (FileNameParser.IsVideo(file.Name) && !Ignored(file.Name, options) && file.Length >= options.MinimumBytes)
                {
                    AddCandidate(results, file, null, options, knownPaths);
                }
            }

            if (options.Recursive)
            {
                foreach (var sub in dir.EnumerateDirectories("*", enumeration).OrderByDescending(d => d.Name, StringComparer.OrdinalIgnoreCase))
                {
                    pending.Push(sub);
                }
            }
        }

        return results.OrderBy(r => r.FullPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddCandidate(List<ScanCandidate> results, FileInfo file, string? nameOverride, ScanOptions options, ISet<string>? knownPaths)
    {
        if (knownPaths?.Contains(file.FullName) == true)
        {
            return;
        }

        var parsed = FileNameParser.Parse(nameOverride is null ? file.Name : nameOverride + ".iso", options.NoiseWords, options.IgnoreBracketed);

        // Nom de fichier peu parlant (« movie.mkv », « VTS_01_1.VOB ») : on prend le nom du dossier.
        if (nameOverride is null && parsed.Year is null && file.Directory is { } parent && parent.Name.Length > 3)
        {
            var fromFolder = FileNameParser.Parse(parent.Name + ".mkv", options.NoiseWords, options.IgnoreBracketed);
            if (fromFolder.Year is not null)
            {
                parsed = parsed with { Title = fromFolder.Title, Year = fromFolder.Year };
            }
        }

        var nfoPath = NfoReader.FindFor(file.FullName);
        var cover = Path.Combine(file.DirectoryName!, Path.GetFileNameWithoutExtension(file.Name) + ".jpg");
        var localCover = File.Exists(cover)
            ? cover
            : CoverNames.Select(n => Path.Combine(file.DirectoryName!, n)).FirstOrDefault(File.Exists);
        results.Add(new ScanCandidate(file.FullName, file.Length, parsed, nfoPath is null ? null : NfoReader.Read(nfoPath), localCover));
    }

    private static FileInfo? DiscStructure(DirectoryInfo dir)
    {
        var videoTs = Path.Combine(dir.FullName, "VIDEO_TS", "VIDEO_TS.IFO");
        if (File.Exists(videoTs))
        {
            return new FileInfo(videoTs);
        }

        var bdmv = Path.Combine(dir.FullName, "BDMV", "index.bdmv");
        return File.Exists(bdmv) ? new FileInfo(bdmv) : null;
    }

    private static bool Ignored(string name, ScanOptions options) =>
        options.IgnoreKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
}
