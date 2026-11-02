using System.Diagnostics;

namespace Cinedex.App.Views;

/// <summary>Ouverture dans le navigateur, limitée aux liens https vers TMDb et IMDb construits par Cinedex.</summary>
public static class WebLinks
{
    public static void Open(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            uri.Host is "www.themoviedb.org" or "www.imdb.com")
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
    }
}
