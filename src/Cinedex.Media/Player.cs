using System.Diagnostics;

namespace Cinedex.Media;

/// <summary>
/// Lance la lecture d'un fichier vidéo ou ouvre son dossier. Seuls des fichiers vidéo existants sont ouverts
/// avec l'application par défaut ; un lecteur externe reçoit le chemin comme argument unique, sans shell.
/// </summary>
public static class Player
{
    public const string FilePathToken = "%FILEPATH%";

    public static void Play(string file, string? externalPlayer = null, string? parameters = null)
    {
        EnsurePlayable(file);
        if (!string.IsNullOrWhiteSpace(externalPlayer))
        {
            if (!File.Exists(externalPlayer))
            {
                throw new FileNotFoundException("Lecteur externe introuvable.", externalPlayer);
            }

            var info = new ProcessStartInfo(externalPlayer) { UseShellExecute = false };
            foreach (var arg in BuildArguments(parameters, file))
            {
                info.ArgumentList.Add(arg);
            }

            Process.Start(info)?.Dispose();
            return;
        }

        OpenWithSystem(file);
    }

    /// <summary>Ouvre le gestionnaire de fichiers sur le dossier du fichier, fichier sélectionné quand c'est possible.</summary>
    public static void RevealInFolder(string file)
    {
        var full = Path.GetFullPath(file);
        if (OperatingSystem.IsWindows())
        {
            var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            info.ArgumentList.Add("/select," + full);
            Process.Start(info)?.Dispose();
            return;
        }

        var dir = Path.GetDirectoryName(full)!;
        var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        var unix = new ProcessStartInfo(opener) { UseShellExecute = false };
        unix.ArgumentList.Add(dir);
        Process.Start(unix)?.Dispose();
    }

    /// <summary>
    /// Découpe les paramètres sur les espaces (les guillemets regroupent) et remplace %FILEPATH% par le chemin,
    /// qui reste toujours un argument entier même s'il contient des espaces.
    /// </summary>
    internal static IReadOnlyList<string> BuildArguments(string? parameters, string file)
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(parameters))
        {
            args.Add(file);
            return args;
        }

        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in parameters)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                Flush();
            }
            else
            {
                current.Append(c);
            }
        }

        Flush();
        if (!parameters.Contains(FilePathToken, StringComparison.Ordinal))
        {
            args.Add(file);
        }

        return args;

        void Flush()
        {
            if (current.Length > 0)
            {
                args.Add(current.ToString().Replace(FilePathToken, file, StringComparison.Ordinal));
                current.Clear();
            }
        }
    }

    private static void EnsurePlayable(string file)
    {
        if (!File.Exists(file))
        {
            throw new FileNotFoundException("Fichier vidéo introuvable : le disque est peut-être débranché.", file);
        }

        var name = Path.GetFileName(file);
        if (!FileNameParser.IsVideo(name) && !name.Equals("VIDEO_TS.IFO", StringComparison.OrdinalIgnoreCase) && !name.Equals("index.bdmv", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Seuls les fichiers vidéo peuvent être lus depuis Cinedex.");
        }
    }

    private static void OpenWithSystem(string file)
    {
        ProcessStartInfo info;
        if (OperatingSystem.IsWindows())
        {
            // Ouverture par association de fichier ; l'extension a été vérifiée (pas d'exécutable).
            info = new ProcessStartInfo(file) { UseShellExecute = true };
        }
        else
        {
            info = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
            info.ArgumentList.Add(file);
        }

        Process.Start(info)?.Dispose();
    }
}
