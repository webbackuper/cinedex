namespace Cinedex.Data;

/// <summary>
/// Chemin complet d'un fichier vidéo à partir de ce que la base enregistre (dossier surveillé et chemin
/// relatif, ou chemin seul). Un chemin qui reste relatif — ce n'est le cas que dans un coffret exporté, où
/// tout est relatif à la racine du disque — est rapporté à <see cref="RelativeBase"/> : le dossier de
/// l'exécutable du coffret, quelle que soit la lettre du disque ce jour-là.
/// </summary>
public static class MediaPaths
{
    /// <summary>Base des chemins relatifs ; nulle hors coffret (les catalogues ordinaires n'en ont pas).</summary>
    public static string? RelativeBase { get; set; }

    public static string Full(string? root, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (root is null && Path.IsPathRooted(path))
        {
            return path;
        }

        var combined = root is null ? path : Path.Combine(root, path);
        if (!Path.IsPathRooted(combined) && RelativeBase is { } baseDirectory)
        {
            combined = Path.Combine(baseDirectory, combined);
        }

        return Path.GetFullPath(combined);
    }
}
