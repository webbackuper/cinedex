using System.Diagnostics;

namespace Cinedex.App.Services;

/// <summary>
/// Ouverture dans le navigateur, limitée à des hôtes connus et au schéma https. Une URL vient d'un service tiers
/// (TMDb) ou d'un fichier .nfo : la liste blanche évite qu'une donnée rapatriée fasse ouvrir n'importe quoi.
/// </summary>
public static class WebLinks
{
    private static readonly string[] Pages = ["www.themoviedb.org", "www.imdb.com"];

    private static readonly string[] Videos =
        ["www.youtube.com", "youtube.com", "m.youtube.com", "youtu.be", "vimeo.com", "www.vimeo.com",
         "www.dailymotion.com", "dailymotion.com"];

    public static void Open(string? url) => Start(url, Pages);

    /// <summary>Ouvre une bande-annonce ; renvoie faux si l'URL est absente ou hors des plateformes connues.</summary>
    public static bool OpenVideo(string? url) => Start(url, Videos);

    /// <summary>Vrai si l'URL est une bande-annonce ouvrable : https et plateforme vidéo connue.</summary>
    public static bool IsVideo(string? url) => Allowed(url, Videos) is not null;

    private static Uri? Allowed(string? url, string[] hosts) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase) ? uri : null;

    private static bool Start(string? url, string[] hosts)
    {
        if (Allowed(url, hosts) is not { } uri)
        {
            return false;
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        return true;
    }
}
