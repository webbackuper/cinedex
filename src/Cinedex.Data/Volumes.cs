namespace Cinedex.Data;

/// <summary>
/// Disques montés et leur nom (« MediaID » : le nom affiché dans les propriétés du disque sous Windows,
/// le dossier de montage ailleurs). Sert à reconnaître un disque USB quelle que soit sa lettre de lecteur.
/// </summary>
public sealed class Volumes
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly IReadOnlyList<(string Root, string? Label)> _volumes;

    public Volumes(IEnumerable<(string Root, string? Label)> volumes)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        _volumes = volumes
            .Select(v => (Root: WithSeparator(v.Root), Label: string.IsNullOrWhiteSpace(v.Label) ? null : v.Label.Trim()))
            .OrderByDescending(v => v.Root.Length)
            .ToList();
    }

    /// <summary>Disques actuellement prêts.</summary>
    public static Volumes Current()
    {
        var list = new List<(string, string?)>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady)
                {
                    list.Add((drive.RootDirectory.FullName, LabelOf(drive)));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Lecteur qui disparaît pendant l'énumération : ignoré.
            }
        }

        return new Volumes(list);
    }

    /// <summary>Noms des disques actuellement montés (les disques sans nom ne comptent pas).</summary>
    public IReadOnlyList<string> Labels => _volumes.Select(v => v.Label).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Nom du disque qui contient ce chemin, ou null (disque sans nom, inconnu ou système de fichiers racine).</summary>
    public string? LabelOf(string fullPath) => Find(fullPath)?.Label;

    /// <summary>
    /// Chemin à utiliser aujourd'hui pour un fichier enregistré sur le disque <paramref name="label"/> :
    /// si ce disque est branché sous une autre lettre (ou un autre point de montage), le chemin est transposé.
    /// </summary>
    public string Resolve(string fullPath, string? label)
    {
        if (label is null)
        {
            return fullPath;
        }

        var here = Find(fullPath);
        if (here is { } h && string.Equals(h.Label, label, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        var target = _volumes.FirstOrDefault(v => string.Equals(v.Label, label, StringComparison.OrdinalIgnoreCase));
        if (target.Root is null)
        {
            return fullPath;
        }

        // Partie du chemin sous le disque d'origine : sous Windows la racine du lecteur (« E:\ »).
        var originalRoot = here?.Root ?? Path.GetPathRoot(fullPath) ?? string.Empty;
        return Path.Combine(target.Root, fullPath[Math.Min(originalRoot.Length, fullPath.Length)..]);
    }

    /// <summary>Vrai si le fichier enregistré est bien sur le disque branché à cet emplacement (ou si son disque est inconnu).</summary>
    public bool IsOnRecordedVolume(string fullPath, string? label) =>
        label is null || string.Equals(LabelOf(fullPath), label, StringComparison.OrdinalIgnoreCase);

    private (string Root, string? Label)? Find(string fullPath)
    {
        foreach (var v in _volumes)
        {
            if (fullPath.StartsWith(v.Root, PathComparison) || string.Equals(WithSeparator(fullPath), v.Root, PathComparison))
            {
                return v;
            }
        }

        return null;
    }

    private static string? LabelOf(DriveInfo drive)
    {
        if (OperatingSystem.IsWindows())
        {
            return string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel;
        }

        // Linux / macOS : un disque amovible est monté sous un dossier portant son nom (/media/moi/FILMS1, /Volumes/FILMS1).
        var root = drive.RootDirectory.FullName.TrimEnd('/');
        return root.Length == 0 ? null : Path.GetFileName(root);
    }

    private static string WithSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
