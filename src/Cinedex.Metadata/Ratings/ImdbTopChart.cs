using System.Globalization;
using System.IO.Compression;
using System.Net;

namespace Cinedex.Metadata.Ratings;

/// <summary>Un film du classement calculé : son rang, son identifiant IMDb, son titre, sa note et ses votes.</summary>
public sealed record ChartEntry(int Rank, string ImdbId, string Title, int? Year, double Rating, int Votes, double Weighted);

/// <summary>
/// Classement des meilleurs films recalculé depuis les fichiers publics d'IMDb (usage personnel et non
/// commercial).
/// <para>
/// IMDb ne publie pas son Top 250 de façon exploitable : il n'y a pas d'API, et extraire la page du site
/// serait contraire à leurs conditions et casserait à la première refonte. La formule du classement, elle,
/// est publique : une moyenne bayésienne qui privilégie les films largement vus. Elle est reproductible à
/// partir des notes et du nombre de votes, que le fichier officiel donne.
/// </para>
/// <para>
/// Le résultat ressemble beaucoup à la vraie liste sans lui être identique : IMDb applique en plus des
/// pondérations non publiées contre la manipulation des votes. C'est pourquoi la liste produite est
/// étiquetée « estimé » partout où elle s'affiche.
/// </para>
/// </summary>
public sealed class ImdbTopChart(HttpClient http, string cacheDirectory)
{
    /// <summary>Type, titre et année de tous les titres IMDb (~200 Mo compressés), nécessaire pour ne garder que les longs métrages.</summary>
    public const string BasicsUrl = "https://datasets.imdbws.com/title.basics.tsv.gz";

    private const long MaxBytes = 600L * 1024 * 1024;

    /// <summary>Votes minimum pour entrer au classement, comme IMDb l'annonce pour son Top 250.</summary>
    public const int MinimumVotes = 25_000;

    public string BasicsPath { get; } = Path.Combine(cacheDirectory, "title.basics.tsv.gz");

    public DateTime? LastDownloadUtc => File.Exists(BasicsPath) ? File.GetLastWriteTimeUtc(BasicsPath) : null;

    /// <summary>Télécharge le fichier des titres s'il est absent ou trop vieux ; renvoie vrai s'il a été (re)téléchargé.</summary>
    public async Task<bool> RefreshAsync(TimeSpan maxAge, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (LastDownloadUtc is { } last && DateTime.UtcNow - last < maxAge)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(BasicsPath)!);
        using var request = new HttpRequestMessage(HttpMethod.Get, BasicsUrl);
        if (LastDownloadUtc is { } previous)
        {
            request.Headers.IfModifiedSince = new DateTimeOffset(previous, TimeSpan.Zero);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            File.SetLastWriteTimeUtc(BasicsPath, DateTime.UtcNow);
            return false;
        }

        response.EnsureSuccessStatusCode();
        var expected = response.Content.Headers.ContentLength;
        if (expected > MaxBytes)
        {
            throw new InvalidDataException("Fichier IMDb anormalement volumineux.");
        }

        var temp = BasicsPath + ".part";
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = File.Create(temp))
        {
            var buffer = new byte[81920];
            long total = 0;
            var reported = 0L;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaxBytes)
                {
                    throw new InvalidDataException("Fichier IMDb anormalement volumineux.");
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                if (total - reported > 8L * 1024 * 1024)
                {
                    reported = total;
                    progress?.Report(Describe(total, expected));
                }
            }
        }

        if (await HeaderOfAsync(temp, cancellationToken).ConfigureAwait(false) is not { } header || !header.StartsWith("tconst\ttitleType", StringComparison.Ordinal))
        {
            File.Delete(temp);
            throw new InvalidDataException("Le fichier IMDb n'a pas le format attendu.");
        }

        File.Move(temp, BasicsPath, overwrite: true);
        return true;
    }

    /// <summary>
    /// Calcule le classement. Les notes viennent du fichier des notes, le type et le titre du fichier des
    /// titres ; seuls les longs métrages non pornographiques assez vus entrent dans le calcul.
    /// </summary>
    public async Task<IReadOnlyList<ChartEntry>> ComputeAsync(string ratingsPath, int count = 250, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ratingsPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (!File.Exists(ratingsPath) || !File.Exists(BasicsPath))
        {
            throw new InvalidOperationException("Les fichiers IMDb ne sont pas téléchargés.");
        }

        progress?.Report("Lecture des notes IMDb…");
        var eligible = await ReadRatingsAsync(ratingsPath, cancellationToken).ConfigureAwait(false);

        progress?.Report("Lecture des titres IMDb…");
        var films = await ReadFilmsAsync(eligible, progress, cancellationToken).ConfigureAwait(false);
        if (films.Count == 0)
        {
            return [];
        }

        // Moyenne bayésienne : C est la note moyenne de l'ensemble retenu, m le seuil de votes.
        var mean = films.Average(f => eligible[f.Key].Rating);
        return films
            .Select(f =>
            {
                var (rating, votes) = eligible[f.Key];
                var weighted = ((double)votes / (votes + MinimumVotes) * rating) + ((double)MinimumVotes / (votes + MinimumVotes) * mean);
                return new ChartEntry(0, f.Key, f.Value.Title, f.Value.Year, rating, votes, weighted);
            })
            .OrderByDescending(e => e.Weighted)
            .ThenByDescending(e => e.Votes)
            .Take(count)
            .Select((e, i) => e with { Rank = i + 1 })
            .ToList();
    }

    private static async Task<Dictionary<string, (double Rating, int Votes)>> ReadRatingsAsync(string path, CancellationToken cancellationToken)
    {
        var eligible = new Dictionary<string, (double, int)>(StringComparer.Ordinal);
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            var parts = line.Split('\t');
            if (parts.Length == 3 &&
                int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var votes) && votes >= MinimumVotes &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var rating))
            {
                eligible[parts[0]] = (rating, votes);
            }
        }

        return eligible;
    }

    private async Task<Dictionary<string, (string Title, int? Year)>> ReadFilmsAsync(
        Dictionary<string, (double Rating, int Votes)> eligible, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var films = new Dictionary<string, (string, int?)>(StringComparer.Ordinal);
        await using var file = File.OpenRead(BasicsPath);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        string? line;
        var seen = 0L;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (++seen % 1_000_000 == 0)
            {
                progress?.Report(string.Create(CultureInfo.CurrentCulture, $"Lecture des titres IMDb : {seen / 1_000_000} M lignes…"));
            }

            // tconst, titleType, primaryTitle, originalTitle, isAdult, startYear, endYear, runtimeMinutes, genres
            var parts = line.Split('\t');
            if (parts.Length < 6 || parts[1] != "movie" || parts[4] == "1" || !eligible.ContainsKey(parts[0]))
            {
                continue;
            }

            films[parts[0]] = (parts[2], int.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture, out var year) ? year : null);
        }

        return films;
    }

    private static async Task<string?> HeaderOfAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var check = File.OpenRead(path);
            await using var gzip = new GZipStream(check, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            return await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static string Describe(long done, long? total) => total is { } expected && expected > 0
        ? string.Create(CultureInfo.CurrentCulture, $"Téléchargement du fichier IMDb : {done / (1024 * 1024)} Mo sur {expected / (1024 * 1024)} Mo")
        : string.Create(CultureInfo.CurrentCulture, $"Téléchargement du fichier IMDb : {done / (1024 * 1024)} Mo");
}
