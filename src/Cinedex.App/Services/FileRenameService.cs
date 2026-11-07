using Cinedex.Core.Localization;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Media;

namespace Cinedex.App.Services;

/// <summary>
/// Un renommage prévu : le fichier d'origine, le nom qu'il prendrait, et la raison qui l'en empêche.
/// Un <see cref="Problem"/> non nul veut dire que rien ne sera fait pour ce fichier.
/// </summary>
public sealed record PlannedRename(MediaFileEntry File, string NewPath, string? Problem)
{
    public string OldName => Path.GetFileName(File.FullPath);

    public string NewName => Path.GetFileName(NewPath);

    public bool CanRename => Problem is null;
}

/// <summary>
/// Renomme les fichiers vidéo d'après la fiche : « Titre (Année).mkv ». Le .nfo qui accompagne le fichier
/// suit, sans quoi un lecteur comme Kodi ne le retrouverait plus. Le renommage est préparé entièrement avant
/// d'être appliqué, pour que l'utilisateur voie ce qui va se passer et que rien ne se fasse à moitié.
/// </summary>
public sealed class FileRenameService(TitleRepository titles, MediaFileRepository files)
{
    /// <summary>Caractères qu'un nom de fichier ne peut pas porter, remplacés par un espace.</summary>
    private static readonly char[] Forbidden = [.. Path.GetInvalidFileNameChars(), ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>
    /// Prépare le renommage des fichiers de ces titres. Rien n'est touché : le résultat dit ce qui serait
    /// renommé et ce qui ne le serait pas, avec sa raison.
    /// </summary>
    public IReadOnlyList<PlannedRename> Plan(IEnumerable<long> titleIds)
    {
        ArgumentNullException.ThrowIfNull(titleIds);
        var planned = new List<PlannedRename>();
        var taken = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var titleId in titleIds)
        {
            if (titles.Get(titleId) is not { } detail)
            {
                continue;
            }

            var ordered = files.FilesOf(titleId).OrderBy(f => f.Part).ThenBy(f => f.Id).ToList();
            foreach (var file in ordered)
            {
                // Plusieurs fichiers pour un même film : le numéro les distingue, sans quoi ils se chasseraient.
                var suffix = ordered.Count > 1 ? Tr.F(" - {0}", file.Part) : string.Empty;
                var target = Path.Combine(
                    Path.GetDirectoryName(file.FullPath) ?? string.Empty,
                    NameOf(detail) + suffix + Path.GetExtension(file.FullPath));
                planned.Add(new PlannedRename(file, target, ProblemWith(file, target, taken)));
                taken.Add(target);
            }
        }

        return planned;
    }

    /// <summary>Applique les renommages possibles ; renvoie le nombre de fichiers vidéo effectivement renommés.</summary>
    public int Apply(IEnumerable<PlannedRename> planned, out IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(planned);
        var failures = new List<string>();
        var done = 0;
        foreach (var rename in planned.Where(p => p.CanRename))
        {
            try
            {
                // Le .nfo d'abord : si le film est renommé et que le .nfo échoue, le lecteur perd sa fiche.
                MoveSidecar(NfoReader.NfoPathFor(rename.File.FullPath), NfoReader.NfoPathFor(rename.NewPath));
                File.Move(rename.File.FullPath, rename.NewPath);
                files.SetPath(rename.File.Id, rename.NewPath);
                done++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{rename.OldName} : {ex.Message}");
            }
        }

        problems = failures;
        return done;
    }

    /// <summary>« Titre (Année) », débarrassé de ce qu'un système de fichiers refuse.</summary>
    public static string NameOf(TitleDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var name = detail.Year is { } year
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{detail.Title} ({year})")
            : detail.Title;
        foreach (var c in Forbidden)
        {
            name = name.Replace(c, ' ');
        }

        // Un nom ne se termine pas par un point ni par une espace : Windows le refuse.
        return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
    }

    private static string? ProblemWith(MediaFileEntry file, string target, HashSet<string> taken)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(file.FullPath, target, comparison))
        {
            return Tr.T("déjà au bon nom");
        }

        if (!File.Exists(file.FullPath))
        {
            return Tr.T("fichier introuvable");
        }

        if (Path.GetFileNameWithoutExtension(target).Length == 0)
        {
            return Tr.T("le titre ne donne aucun nom de fichier");
        }

        return File.Exists(target) || taken.Contains(target) ? Tr.T("un fichier porte déjà ce nom") : null;
    }

    private static void MoveSidecar(string from, string to)
    {
        if (File.Exists(from) && !File.Exists(to))
        {
            File.Move(from, to);
        }
    }
}
