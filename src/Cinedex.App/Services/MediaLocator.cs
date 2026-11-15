using Cinedex.Core.Localization;

namespace Cinedex.App.Services;

/// <summary>Un fichier à lire, tel que le catalogue le connaît : son chemin, et le disque (MediaID) où il a été trouvé.</summary>
public sealed record MediaCandidate(string Path, string? Label, long? FileId = null);

/// <summary>Le fichier retrouvé : où il est aujourd'hui, et s'il a fallu le chercher ailleurs que là où le catalogue le croyait.</summary>
public sealed record LocatedMedia(MediaCandidate File, string Path, bool FoundElsewhere);

/// <summary>
/// Retrouve un fichier vidéo avant de le lire. Un catalogue de disques USB connaît des fichiers qui ne sont
/// pas toujours branchés, et dont la lettre de lecteur change d'un branchement à l'autre : plutôt qu'un
/// message d'erreur, on demande le disque, et on sait aller chercher le même chemin sur les autres lecteurs.
/// </summary>
public static class MediaLocator
{
    /// <summary>Question posée : titre, message, réponses ; renvoie le rang de la réponse, ou -1.</summary>
    public delegate Task<int> Ask(string title, string message, params string[] answers);

    /// <summary>
    /// Renvoie le premier fichier présent. S'il n'y en a aucun, demande de connecter le disque et propose de
    /// réessayer ou de chercher sur les autres lecteurs, jusqu'à ce que le fichier soit trouvé ou que
    /// l'utilisateur renonce (nul).
    /// </summary>
    /// <param name="files">Les fichiers candidats, relus à chaque essai : un disque qu'on vient de brancher peut changer leur chemin.</param>
    /// <param name="roots">Racines où chercher ; par défaut les disques montés de ce système.</param>
    public static async Task<LocatedMedia?> LocateAsync(Func<IReadOnlyList<MediaCandidate>> files, Ask ask, Func<IReadOnlyList<string>>? roots = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(ask);
        var note = string.Empty;
        while (true)
        {
            var candidates = files();
            if (candidates.FirstOrDefault(c => File.Exists(c.Path)) is { } present)
            {
                return new LocatedMedia(present, present.Path, FoundElsewhere: false);
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            var answer = await ask(Tr.T("Disque à connecter"), Message(candidates, note), Tr.T("Réessayer"), SearchLabel, Tr.T("Annuler")).ConfigureAwait(true);
            if (answer == 0)
            {
                note = string.Empty;
                continue;
            }

            if (answer != 1)
            {
                return null;
            }

            var mounted = (roots ?? MountRoots)();
            foreach (var candidate in candidates)
            {
                if (FindElsewhere(candidate.Path, mounted) is { } found)
                {
                    return new LocatedMedia(candidate, found, FoundElsewhere: true);
                }
            }

            note = Tr.F("Introuvable sur les {0} disque(s) connecté(s).", mounted.Count);
        }
    }

    /// <summary>Le bouton de recherche, dit avec les mots du système : des lettres sous Windows, des disques montés ailleurs.</summary>
    public static string SearchLabel => OperatingSystem.IsWindows()
        ? Tr.T("Chercher sur les autres lettres de lecteur")
        : Tr.T("Chercher sur les autres disques montés");

    /// <summary>
    /// Cherche le même chemin sous une autre racine : « E:\Films\Heat.mkv » sous F:, G:… ; ou
    /// « /Volumes/FILMS1/Heat.mkv » sous les autres dossiers de /Volumes, /media, /mnt.
    /// </summary>
    public static string? FindElsewhere(string fullPath, IReadOnlyList<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var relative = RelativeToVolume(fullPath);
        if (relative.Length == 0)
        {
            return null;
        }

        foreach (var root in roots)
        {
            var candidate = Path.Combine(root, relative);
            if (!string.Equals(candidate, fullPath, StringComparison.Ordinal) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// La partie du chemin qui ne dépend pas du branchement : sans la lettre de lecteur sous Windows, sans
    /// le dossier de montage ailleurs. Un chemin UNC ou hors des points de montage connus perd sa seule racine.
    /// </summary>
    public static string RelativeToVolume(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        var path = fullPath.Replace('\\', '/');

        // Lettre de lecteur, même lue sur un autre système que Windows (catalogue partagé entre deux machines).
        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/')
        {
            return Native(path[3..]);
        }

        foreach (var (prefix, depth) in MountPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                var parts = path[prefix.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > depth ? Native(string.Join('/', parts.Skip(depth))) : string.Empty;
            }
        }

        // Chemin réseau « //serveur/partage/… » : on garde ce qui suit le partage.
        if (path.StartsWith("//", StringComparison.Ordinal))
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 2 ? Native(string.Join('/', parts.Skip(2))) : string.Empty;
        }

        return Native(path.TrimStart('/'));
    }

    /// <summary>Dossiers de montage usuels, et le nombre de dossiers qui nomment le disque sous chacun.</summary>
    private static readonly (string Prefix, int Depth)[] MountPrefixes =
    [
        ("/Volumes/", 1),
        ("/run/media/", 2),
        ("/media/", 2),
        ("/mnt/", 1),
    ];

    /// <summary>Les racines où un disque peut être branché sur ce système.</summary>
    public static IReadOnlyList<string> MountRoots()
    {
        var roots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.IsReady)
                    {
                        roots.Add(drive.RootDirectory.FullName);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Lecteur qui disparaît pendant l'énumération : ignoré.
                }
            }

            return roots;
        }

        // macOS : /Volumes/NOM. Linux : /media/utilisateur/NOM, /run/media/utilisateur/NOM, parfois /media/NOM ou /mnt/NOM.
        AddChildren(roots, "/Volumes", depth: 1);
        AddChildren(roots, "/run/media", depth: 2);
        AddChildren(roots, "/media", depth: 2);
        AddChildren(roots, "/media", depth: 1);
        AddChildren(roots, "/mnt", depth: 1);
        return roots.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void AddChildren(List<string> roots, string parent, int depth)
    {
        try
        {
            if (!Directory.Exists(parent))
            {
                return;
            }

            foreach (var child in Directory.GetDirectories(parent))
            {
                if (depth <= 1)
                {
                    roots.Add(child);
                }
                else
                {
                    AddChildren(roots, child, depth - 1);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dossier de montage illisible : il n'y a rien à y chercher.
        }
    }

    private static string Message(IReadOnlyList<MediaCandidate> candidates, string note)
    {
        var first = candidates[0];
        var disc = candidates.Select(c => c.Label).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        var ask = disc is null
            ? Tr.T("Le fichier est introuvable : son disque n'est sans doute pas connecté.")
            : Tr.F("Le fichier est introuvable. Connectez le disque « {0} », puis réessayez.", disc);
        var text = ask + "\n\n" + first.Path;
        return note.Length == 0 ? text : text + "\n\n" + note;
    }

    private static string Native(string relative) => relative.Replace('/', Path.DirectorySeparatorChar);
}
