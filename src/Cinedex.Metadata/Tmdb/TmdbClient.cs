using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Cinedex.Core.Localization;

namespace Cinedex.Metadata.Tmdb;

public sealed class TmdbOptions
{
    /// <summary>Langue des titres, synopsis et images (ex. fr-FR).</summary>
    public string Language { get; init; } = "fr-FR";

    /// <summary>Pays des certifications (ex. FR), repli sur US.</summary>
    public string CertificationCountry { get; init; } = "FR";

    public bool IncludeAdult { get; init; }
}

public sealed class TmdbAuthenticationException(string message) : Exception(message);

public class TmdbUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Limite de débit de TMDb toujours atteinte après les nouveaux essais.</summary>
public sealed class TmdbRateLimitException(string message) : TmdbUnavailableException(message);

/// <summary>
/// Client de l'API TMDb v3. Le jeton est envoyé en en-tête Authorization (jamais dans l'URL),
/// les hôtes sont limités à api.themoviedb.org et image.tmdb.org, la taille des réponses est bornée.
/// </summary>
public sealed partial class TmdbClient
{
    public const string ApiHost = "api.themoviedb.org";
    public const string ImageHost = "image.tmdb.org";
    public const int MaxImageBytes = 15 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _http;
    private readonly Func<CancellationToken, ValueTask<string?>> _token;
    private readonly TmdbOptions _options;
    private readonly ILogger _logger;
    // Partagé par toutes les instances : TMDb limite le débit par adresse, pas par client.
    private static readonly SemaphoreSlim _throttle = new(8);

    public TmdbClient(HttpClient http, Func<CancellationToken, ValueTask<string?>> token, TmdbOptions? options = null, ILogger<TmdbClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(token);
        _http = http;
        _token = token;
        _options = options ?? new TmdbOptions();
        _logger = logger ?? NullLogger<TmdbClient>.Instance;
    }

    /// <summary>Crée le HttpClient recommandé : TLS validé par le système, pas de cookies, délai de 30 s.</summary>
    public static HttpClient CreateHttpClient(IWebProxy? proxy = null)
    {
#pragma warning disable CA2000 // Le handler appartient au HttpClient retourné.
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            Proxy = proxy,
            UseProxy = true,
            ConnectCallback = ConnectAsync,
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = MaxImageBytes,
            DefaultRequestHeaders = { { "User-Agent", "Cinedex/0.1 (+https://github.com/webbackuper/cinedex)" } },
        };
#pragma warning restore CA2000
    }

    /// <summary>
    /// Connexion TCP en essayant IPv4 d'abord, chaque adresse avec un délai court : sur certains réseaux, IPv6 est
    /// annoncé par le DNS mais injoignable, et chaque requête attendrait sinon le délai complet avant d'échouer.
    /// </summary>
    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
        Exception? last = null;
        foreach (var address in addresses.OrderBy(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 0 : 1))
        {
            var socket = new System.Net.Sockets.Socket(address.AddressFamily, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp) { NoDelay = true };
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attempt.CancelAfter(TimeSpan.FromSeconds(6));
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), attempt.Token).ConfigureAwait(false);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                socket.Dispose();
                last = ex;
            }
        }

        throw new HttpRequestException($"Connexion impossible à {context.DnsEndPoint.Host}.", last);
    }

    /// <summary>
    /// Recherche films et séries. Accepte aussi un identifiant : tt1234567 (IMDb), movie/123 ou tv/123 (TMDb).
    /// </summary>
    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, int? year = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var text = query.Trim();

        if (ImdbIdPattern().IsMatch(text))
        {
            var found = await GetAsync<TmdbFindResult>($"find/{text}?external_source=imdb_id&language={Lang}", cancellationToken).ConfigureAwait(false);
            return (found.MovieResults ?? []).Select(r => ToResult(r, TitleKind.Movie))
                .Concat((found.TvResults ?? []).Select(r => ToResult(r, TitleKind.Series)))
                .ToList();
        }

        if (SourceIdPattern().Match(text) is { Success: true } direct)
        {
            var kind = direct.Groups["kind"].Value == "tv" ? TitleKind.Series : TitleKind.Movie;
            var record = await GetDetailsAsync(kind, long.Parse(direct.Groups["id"].Value, CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
            return [new MetadataSearchResult(kind, record.SourceId, record.Title, record.OriginalTitle, record.Year, record.Overview, record.PosterPath, record.Rating)];
        }

        var q = Uri.EscapeDataString(text);
        var adult = _options.IncludeAdult ? "true" : "false";
        var movieYear = year is null ? string.Empty : $"&year={year.Value.ToString(CultureInfo.InvariantCulture)}";
        var tvYear = year is null ? string.Empty : $"&first_air_date_year={year.Value.ToString(CultureInfo.InvariantCulture)}";
        var movies = GetAsync<TmdbPage<TmdbSearchItem>>($"search/movie?query={q}&include_adult={adult}&language={Lang}{movieYear}", cancellationToken);
        var shows = GetAsync<TmdbPage<TmdbSearchItem>>($"search/tv?query={q}&include_adult={adult}&language={Lang}{tvYear}", cancellationToken);
        await Task.WhenAll(movies, shows).ConfigureAwait(false);

        // Films d'abord (cas le plus fréquent), puis séries ; chaque liste garde l'ordre de pertinence TMDb.
        return (movies.Result.Results ?? []).Select(r => ToResult(r, TitleKind.Movie))
            .Concat((shows.Result.Results ?? []).Select(r => ToResult(r, TitleKind.Series)))
            .ToList();
    }

    /// <summary>
    /// Suggestions de TMDb pour un titre (« les spectateurs ont aussi aimé »). Appelé à la demande, jamais à
    /// l'ouverture d'une fiche : le résultat est ensuite gardé en base, un appel par titre suffit.
    /// </summary>
    public async Task<IReadOnlyList<MetadataSearchResult>> GetRecommendationsAsync(TitleKind kind, long id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        var path = kind == TitleKind.Movie ? "movie" : "tv";
        var idText = id.ToString(CultureInfo.InvariantCulture);
        var page = await GetAsync<TmdbPage<TmdbSearchItem>>(
            $"{path}/{idText}/recommendations?language={Lang}", cancellationToken).ConfigureAwait(false);
        return (page.Results ?? []).Select(r => ToResult(r, kind)).ToList();
    }

    public async Task<MetadataRecord> GetDetailsAsync(TitleKind kind, long id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        var idText = id.ToString(CultureInfo.InvariantCulture);
        if (kind == TitleKind.Movie)
        {
            var movie = await GetAsync<TmdbMovie>(
                $"movie/{idText}?language={Lang}&append_to_response=credits,release_dates,keywords,videos&include_video_language={VideoLanguages}",
                cancellationToken).ConfigureAwait(false);
            return TmdbMapper.ToRecord(movie, _options);
        }

        var tv = await GetAsync<TmdbTv>(
            $"tv/{idText}?language={Lang}&append_to_response=aggregate_credits,content_ratings,external_ids,keywords,videos&include_video_language={VideoLanguages}",
            cancellationToken).ConfigureAwait(false);
        return TmdbMapper.ToRecord(tv, _options);
    }

    /// <summary>
    /// Fiche d'une personne (biographie, dates, lieu de naissance, photo, identifiant IMDb). La biographie est
    /// demandée dans la langue choisie, puis en anglais si TMDb n'en a pas de traduction.
    /// </summary>
    public async Task<Core.People.PersonDetail> GetPersonAsync(long personId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(personId);
        var id = personId.ToString(CultureInfo.InvariantCulture);
        var person = await GetAsync<TmdbPerson>($"person/{id}?language={Lang}", cancellationToken).ConfigureAwait(false);
        var biography = person.Biography;
        if (string.IsNullOrWhiteSpace(biography) && !_options.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            biography = (await GetAsync<TmdbPerson>($"person/{id}?language=en-US", cancellationToken).ConfigureAwait(false)).Biography;
        }

        return new Core.People.PersonDetail
        {
            Name = person.Name ?? string.Empty,
            TmdbId = person.Id,
            ImdbId = person.ImdbId is { } imdb && imdb.StartsWith("nm", StringComparison.Ordinal) && imdb[2..].All(char.IsAsciiDigit) ? imdb : null,
            Biography = string.IsNullOrWhiteSpace(biography) ? null : biography.Trim(),
            Birthday = TmdbMapper.DateOf(person.Birthday),
            Deathday = TmdbMapper.DateOf(person.Deathday),
            PlaceOfBirth = string.IsNullOrWhiteSpace(person.PlaceOfBirth) ? null : person.PlaceOfBirth.Trim(),
            KnownFor = person.KnownForDepartment switch
            {
                "Acting" => "Interprétation",
                "Directing" => "Réalisation",
                "Writing" => "Scénario",
                "Sound" => "Musique",
                "Production" => "Production",
                null or "" => null,
                var other => other,
            },
            ProfilePath = person.ProfilePath,
        };
    }

    /// <summary>Épisodes d'une saison (0 = épisodes spéciaux).</summary>
    public async Task<IReadOnlyList<EpisodeRecord>> GetSeasonAsync(long tvId, int seasonNumber, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tvId);
        ArgumentOutOfRangeException.ThrowIfNegative(seasonNumber);
        var season = await GetAsync<TmdbSeason>(
            $"tv/{tvId.ToString(CultureInfo.InvariantCulture)}/season/{seasonNumber.ToString(CultureInfo.InvariantCulture)}?language={Lang}",
            cancellationToken).ConfigureAwait(false);
        return TmdbMapper.ToEpisodes(season);
    }

    /// <summary>
    /// Fiche d'une collection (saga) : présentation, affiche, décor et films, du plus ancien au plus récent.
    /// La présentation est demandée en anglais si TMDb n'en a pas dans la langue choisie.
    /// </summary>
    public async Task<CollectionRecord> GetCollectionAsync(long collectionId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(collectionId);
        var id = collectionId.ToString(CultureInfo.InvariantCulture);
        var collection = await GetAsync<TmdbCollection>($"collection/{id}?language={Lang}", cancellationToken).ConfigureAwait(false);
        var overview = collection.Overview;
        if (string.IsNullOrWhiteSpace(overview) && !_options.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            overview = (await GetAsync<TmdbCollection>($"collection/{id}?language=en-US", cancellationToken).ConfigureAwait(false)).Overview;
        }

        var parts = (collection.Parts ?? [])
            .Select(p => new CollectionPart(p.Id, p.Title ?? p.OriginalTitle ?? "?", TmdbMapper.YearOf(p.ReleaseDate), p.PosterPath))
            .OrderBy(p => p.Year ?? int.MaxValue)
            .ThenBy(p => p.Title, StringComparer.CurrentCulture)
            .ToList();
        return new CollectionRecord(collection.Id, collection.Name ?? "?", string.IsNullOrWhiteSpace(overview) ? null : overview.Trim(), collection.PosterPath, collection.BackdropPath, parts);
    }

    /// <summary>Recherche une collection par son nom (pour celles créées à la main ou reprises d'EMDB).</summary>
    public async Task<IReadOnlyList<MetadataSaga>> SearchCollectionsAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var page = await GetAsync<TmdbPage<TmdbSearchItem>>($"search/collection?query={Uri.EscapeDataString(name.Trim())}&language={Lang}", cancellationToken).ConfigureAwait(false);
        return [.. (page.Results ?? []).Select(r => new MetadataSaga(r.Id, r.Name ?? r.OriginalName ?? "?", r.PosterPath, null))];
    }

    /// <summary>Télécharge une image TMDb (affiche, fond, photo). Le chemin doit être un nom de fichier TMDb.</summary>
    public async Task<byte[]> DownloadImageAsync(string path, string size = "w500", CancellationToken cancellationToken = default)
    {
        if (!ImagePathPattern().IsMatch(path ?? string.Empty) || !ImageSizePattern().IsMatch(size ?? string.Empty))
        {
            throw new ArgumentException("Chemin ou taille d'image TMDb invalide.", nameof(path));
        }

        var uri = new Uri($"https://{ImageHost}/t/p/{size}{path}");
        await _throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxImageBytes)
            {
                throw new TmdbUnavailableException("Image trop volumineuse.");
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _throttle.Release();
        }
    }

    private string Lang => Uri.EscapeDataString(_options.Language);

    private string VideoLanguages => Uri.EscapeDataString($"{_options.Language.Split('-')[0]},en,null");

    private async Task<T> GetAsync<T>(string relative, CancellationToken cancellationToken)
    {
        var token = await _token(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new TmdbAuthenticationException("Aucun jeton TMDb n'est configuré. Renseignez-le dans les réglages.");
        }

        var uri = new Uri($"https://{ApiHost}/3/{relative}");
        for (var attempt = 1; ; attempt++)
        {
            await _throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < 3)
            {
                _logger.LogWarning("TMDb injoignable (essai {Attempt}) : {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (HttpRequestException ex)
            {
                throw new TmdbUnavailableException("TMDb est injoignable. Vérifiez la connexion ou le proxy.", ex);
            }
            finally
            {
                _throttle.Release();
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new TmdbAuthenticationException("Le jeton TMDb est refusé (401). Vérifiez-le dans les réglages.");
                }

                if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 3)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt * 2);
                    _logger.LogWarning("TMDb a répondu {Status}, nouvel essai dans {Wait}", (int)response.StatusCode, wait);
                    await Task.Delay(wait < TimeSpan.FromSeconds(30) ? wait : TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new TmdbRateLimitException("TMDb limite le nombre de requêtes et refuse encore après trois essais espacés.");
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new KeyNotFoundException("Titre introuvable sur TMDb.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new TmdbUnavailableException(Tr.F("TMDb a répondu {0}.", (int)response.StatusCode));
                }

                return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false)
                    ?? throw new TmdbUnavailableException("Réponse TMDb vide.");
            }
        }
    }

    private static MetadataSearchResult ToResult(TmdbSearchItem item, TitleKind kind) => new(
        kind,
        item.Id,
        (kind == TitleKind.Movie ? item.Title : item.Name) ?? item.OriginalTitle ?? item.OriginalName ?? "?",
        kind == TitleKind.Movie ? item.OriginalTitle : item.OriginalName,
        TmdbMapper.YearOf(kind == TitleKind.Movie ? item.ReleaseDate : item.FirstAirDate),
        item.Overview,
        item.PosterPath,
        item.VoteAverage);

    [GeneratedRegex("^tt[0-9]{7,10}$")]
    private static partial Regex ImdbIdPattern();

    [GeneratedRegex("^(?<kind>movie|tv)/(?<id>[0-9]{1,10})$")]
    private static partial Regex SourceIdPattern();

    [GeneratedRegex("^/[A-Za-z0-9_-]{1,64}\\.(jpg|jpeg|png|webp)$")]
    private static partial Regex ImagePathPattern();

    [GeneratedRegex("^(w[0-9]{2,4}|h[0-9]{2,4}|original)$")]
    private static partial Regex ImageSizePattern();
}
