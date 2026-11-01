using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;

namespace Cinedex.Metadata.Ratings;

/// <summary>
/// Fichier officiel IMDb title.ratings.tsv.gz (jeu de données pour usage personnel et non commercial, ~9 Mo,
/// régénéré chaque jour). Téléchargé au plus une fois par jour, et seulement s'il a changé (If-Modified-Since),
/// puis lu en flux : seuls les identifiants demandés sont gardés en mémoire.
/// </summary>
public sealed class ImdbRatingsDataset(HttpClient http, string cacheDirectory)
{
    public const string Url = "https://datasets.imdbws.com/title.ratings.tsv.gz";
    private const long MaxBytes = 100L * 1024 * 1024;

    public string CachePath { get; } = Path.Combine(cacheDirectory, "title.ratings.tsv.gz");

    public DateTime? LastDownloadUtc => File.Exists(CachePath) ? File.GetLastWriteTimeUtc(CachePath) : null;

    /// <summary>Télécharge le fichier s'il est absent ou vieux de plus de <paramref name="maxAge"/> ; renvoie vrai s'il a été (re)téléchargé.</summary>
    public async Task<bool> RefreshAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        if (LastDownloadUtc is { } last && DateTime.UtcNow - last < maxAge)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        if (LastDownloadUtc is { } previous)
        {
            request.Headers.IfModifiedSince = new DateTimeOffset(previous, TimeSpan.Zero);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            File.SetLastWriteTimeUtc(CachePath, DateTime.UtcNow);
            return false;
        }

        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxBytes)
        {
            throw new InvalidDataException("Fichier IMDb anormalement volumineux.");
        }

        var temp = CachePath + ".part";
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = File.Create(temp))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaxBytes)
                {
                    throw new InvalidDataException("Fichier IMDb anormalement volumineux.");
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }

        // Contrôle d'intégrité minimal : un flux gzip et la ligne d'en-tête attendue.
        string? header;
        try
        {
            await using var check = File.OpenRead(temp);
            await using var gzip = new GZipStream(check, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            header = null;
        }

        if (header != "tconst\taverageRating\tnumVotes")
        {
            File.Delete(temp);
            throw new InvalidDataException("Le fichier IMDb n'a pas le format attendu.");
        }

        File.Move(temp, CachePath, overwrite: true);
        return true;
    }

    /// <summary>Notes et votes des identifiants demandés, lus dans le fichier en cache.</summary>
    public async Task<IReadOnlyDictionary<string, (double Rating, int Votes)>> LookupAsync(IEnumerable<string> imdbIds, CancellationToken cancellationToken = default)
    {
        var wanted = new HashSet<string>(imdbIds, StringComparer.Ordinal);
        var found = new Dictionary<string, (double, int)>(StringComparer.Ordinal);
        if (wanted.Count == 0 || !File.Exists(CachePath))
        {
            return found;
        }

        await using var file = File.OpenRead(CachePath);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null && found.Count < wanted.Count)
        {
            var tab = line.IndexOf('\t', StringComparison.Ordinal);
            if (tab <= 0 || !wanted.Contains(line[..tab]))
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length == 3 &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var rating) &&
                int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var votes))
            {
                found[parts[0]] = (rating, votes);
            }
        }

        return found;
    }
}
