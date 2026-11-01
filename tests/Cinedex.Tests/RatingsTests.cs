using System.IO.Compression;
using System.Net;
using System.Text;
using Cinedex.App.Services;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Metadata.Ratings;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cinedex.Tests;

public sealed class RatingsTests
{
    private const string OmdbHeat = """
        {"Title":"Heat","Year":"1995","imdbRating":"8.3","imdbVotes":"756,312","Metascore":"76",
         "Ratings":[{"Source":"Internet Movie Database","Value":"8.3/10"},{"Source":"Rotten Tomatoes","Value":"88%"},{"Source":"Metacritic","Value":"76/100"}],
         "imdbID":"tt0113277","Response":"True"}
        """;

    [Fact]
    public async Task Omdb_returns_imdb_rotten_tomatoes_and_metacritic()
    {
        var handler = new Handler(_ => Json(OmdbHeat));
        var omdb = new OmdbClient(new HttpClient(handler), _ => ValueTask.FromResult<string?>("secretkey"));

        var r = await omdb.GetRatingsAsync("tt0113277", TestContext.Current.CancellationToken);

        Assert.Equal(new ExternalRatings(8.3, 756312, 88, 76), r);
        var uri = Assert.Single(handler.Requests).RequestUri!;
        Assert.Equal(("https", OmdbClient.Host), (uri.Scheme, uri.Host));
    }

    [Fact]
    public async Task Omdb_na_values_and_errors_are_handled_without_leaking_the_key()
    {
        var na = new OmdbClient(new HttpClient(new Handler(_ => Json("""{"imdbRating":"N/A","imdbVotes":"N/A","Metascore":"N/A","Ratings":[],"Response":"True"}"""))), _ => ValueTask.FromResult<string?>("k123456"));
        Assert.True((await na.GetRatingsAsync("tt0113277", TestContext.Current.CancellationToken))!.IsEmpty);

        var quota = new OmdbClient(new HttpClient(new Handler(_ => Json("""{"Response":"False","Error":"Request limit reached!"}"""))), _ => ValueTask.FromResult<string?>("k123456"));
        var ex = await Assert.ThrowsAsync<OmdbException>(() => quota.GetRatingsAsync("tt0113277", TestContext.Current.CancellationToken));
        Assert.True(ex.IsQuota);

        var down = new OmdbClient(new HttpClient(new Handler(_ => throw new HttpRequestException("https://www.omdbapi.com/?apikey=k123456"))), _ => ValueTask.FromResult<string?>("k123456"));
        var downEx = await Assert.ThrowsAsync<OmdbException>(() => down.GetRatingsAsync("tt0113277", TestContext.Current.CancellationToken));
        Assert.DoesNotContain("k123456", downEx.ToString(), StringComparison.Ordinal);

        var noKey = new OmdbClient(new HttpClient(new Handler(_ => Json(OmdbHeat))), _ => ValueTask.FromResult<string?>(null));
        Assert.Null(await noKey.GetRatingsAsync("tt0113277", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => noKey.GetRatingsAsync("tt0113277&apikey=x", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Dataset_is_downloaded_once_validated_and_looked_up()
    {
        using var temp = new TempDirectory();
        var gz = Gzip("tconst\taverageRating\tnumVotes\ntt0000001\t5.7\t2100\ntt0113277\t8.3\t756312\n");
        var calls = 0;
        var dataset = new ImdbRatingsDataset(new HttpClient(new Handler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(gz) };
        })), temp.Path);

        Assert.True(await dataset.RefreshAsync(TimeSpan.FromDays(1), TestContext.Current.CancellationToken));
        Assert.False(await dataset.RefreshAsync(TimeSpan.FromDays(1), TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);

        var found = await dataset.LookupAsync(["tt0113277", "tt9999999"], TestContext.Current.CancellationToken);
        Assert.Equal((8.3, 756312), Assert.Single(found).Value);

        var bad = new ImdbRatingsDataset(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Gzip("<html>")) })), Path.Combine(temp.Path, "b"));
        await Assert.ThrowsAsync<InvalidDataException>(() => bad.RefreshAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Service_uses_omdb_then_falls_back_to_the_dataset_for_imdb()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        var heat = titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        titles.Update(titles.Get(heat)! with { ImdbId = "tt0113277" });
        var dataset = new ImdbRatingsDataset(
            new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Gzip("tconst\taverageRating\tnumVotes\ntt0113277\t8.4\t800000\n")) })),
            temp.Path);

        // Avec OMDb : les trois notes.
        var withOmdb = new RatingsService(new OmdbClient(new HttpClient(new Handler(_ => Json(OmdbHeat))), _ => ValueTask.FromResult<string?>("k123456")),
            dataset, titles, new MetadataRepository(db), new SettingsStore(db), NullLogger<RatingsService>.Instance);
        await withOmdb.EnrichAsync(heat, TestContext.Current.CancellationToken);
        var d = titles.Get(heat)!;
        Assert.Equal((8.3, 756312, 88, 76), (d.ImdbRating, d.ImdbVotes, d.Tomatometer, d.Metascore));

        // Sans clé OMDb : la note IMDb vient du fichier officiel, les autres notes connues sont conservées.
        var withoutOmdb = new RatingsService(new OmdbClient(new HttpClient(new Handler(_ => Json(OmdbHeat))), _ => ValueTask.FromResult<string?>(null)),
            dataset, titles, new MetadataRepository(db), new SettingsStore(db), NullLogger<RatingsService>.Instance);
        await withoutOmdb.EnrichAsync(heat, TestContext.Current.CancellationToken);
        d = titles.Get(heat)!;
        Assert.Equal((8.4, 800000, 88, 76), (d.ImdbRating, d.ImdbVotes, d.Tomatometer, d.Metascore));

        Assert.Equal(1, await withoutOmdb.RefreshAllImdbAsync(null, TestContext.Current.CancellationToken));
    }

    private static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        {
            gzip.Write(Encoding.UTF8.GetBytes(text));
        }

        return output.ToArray();
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
