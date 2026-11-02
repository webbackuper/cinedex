using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cinedex.Core.Localization;

namespace Cinedex.Metadata.Ratings;

/// <summary>Notes externes d'un film. Toute valeur absente ou « N/A » reste nulle.</summary>
public sealed record ExternalRatings(double? ImdbRating, int? ImdbVotes, int? Tomatometer, int? Metascore)
{
    public bool IsEmpty => ImdbRating is null && Tomatometer is null && Metascore is null;
}

public sealed class OmdbException(string message, bool isQuota = false, bool isAuth = false) : Exception(message)
{
    public bool IsQuota { get; } = isQuota;

    public bool IsAuth { get; } = isAuth;
}

/// <summary>
/// Client OMDb (omdbapi.com) : à partir de l'identifiant IMDb fourni par TMDb, un seul appel renvoie la note IMDb
/// et ses votes, le Tomatometer de Rotten Tomatoes et le Metascore de Metacritic.
/// OMDb n'accepte la clé que dans l'URL : l'URL n'est donc jamais journalisée ni incluse dans un message d'erreur.
/// </summary>
public sealed partial class OmdbClient(HttpClient http, Func<CancellationToken, ValueTask<string?>> apiKey)
{
    public const string Host = "www.omdbapi.com";

    public async Task<ExternalRatings?> GetRatingsAsync(string imdbId, CancellationToken cancellationToken = default)
    {
        if (!ImdbId().IsMatch(imdbId ?? string.Empty))
        {
            throw new ArgumentException("Identifiant IMDb invalide.", nameof(imdbId));
        }

        var key = await apiKey(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var uri = new Uri($"https://{Host}/?i={imdbId}&apikey={Uri.EscapeDataString(key.Trim())}");
        OmdbResponse? body;
        try
        {
            using var response = await http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new OmdbException("La clé OMDb est refusée. Vérifiez-la dans les réglages.", isAuth: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new OmdbException(Tr.F("OMDb a répondu {0}.", (int)response.StatusCode));
            }

            body = await response.Content.ReadFromJsonAsync<OmdbResponse>(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // Message volontairement générique : l'exception d'origine peut contenir l'URL, donc la clé.
            throw new OmdbException("OMDb est injoignable.");
        }

        if (body is null)
        {
            return null;
        }

        if (!string.Equals(body.Response, "True", StringComparison.OrdinalIgnoreCase))
        {
            var error = body.Error ?? string.Empty;
            if (error.Contains("limit", StringComparison.OrdinalIgnoreCase))
            {
                throw new OmdbException("Quota OMDb du jour atteint (1 000 requêtes pour une clé gratuite).", isQuota: true);
            }

            if (error.Contains("API key", StringComparison.OrdinalIgnoreCase))
            {
                throw new OmdbException("La clé OMDb est refusée. Vérifiez-la dans les réglages.", isAuth: true);
            }

            return null; // Film inconnu d'OMDb.
        }

        return Parse(body);
    }

    internal static ExternalRatings Parse(OmdbResponse body)
    {
        string? Source(string name) => body.Ratings?.FirstOrDefault(r => string.Equals(r.Source, name, StringComparison.OrdinalIgnoreCase))?.Value;

        var imdb = ParseDouble(body.ImdbRating) ?? ParseDouble(Source("Internet Movie Database")?.Split('/')[0]);
        var votes = int.TryParse((body.ImdbVotes ?? string.Empty).Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : (int?)null;
        var rotten = ParseInt(Source("Rotten Tomatoes")?.TrimEnd('%'));
        var meta = ParseInt(body.Metascore) ?? ParseInt(Source("Metacritic")?.Split('/')[0]);
        return new ExternalRatings(
            imdb is >= 0 and <= 10 ? imdb : null,
            votes,
            rotten is >= 0 and <= 100 ? rotten : null,
            meta is >= 0 and <= 100 ? meta : null);
    }

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    [GeneratedRegex("^tt[0-9]{7,10}$")]
    private static partial Regex ImdbId();

#pragma warning disable CA1812 // Instanciés par System.Text.Json.
    internal sealed record OmdbRating(string? Source, string? Value);

    internal sealed record OmdbResponse(
        [property: JsonPropertyName("Response")] string? Response,
        [property: JsonPropertyName("Error")] string? Error,
        [property: JsonPropertyName("imdbRating")] string? ImdbRating,
        [property: JsonPropertyName("imdbVotes")] string? ImdbVotes,
        [property: JsonPropertyName("Metascore")] string? Metascore,
        [property: JsonPropertyName("Ratings")] IReadOnlyList<OmdbRating>? Ratings);
#pragma warning restore CA1812
}
