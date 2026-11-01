using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Cinedex.App.Services;
using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Media;
using Cinedex.Metadata.Ratings;
using Cinedex.Metadata.Tmdb;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cinedex.Tests;

public sealed class ApiLimitTests
{
    [Fact]
    public async Task Tmdb_limit_during_scan_asks_once_then_finishes_without_tmdb()
    {
        using var temp = new TempDirectory();
        var prompt = new Prompt(answer: true);
        var (service, titles) = NewImport(temp, new ApiGate(prompt));
        var library = Directory.CreateDirectory(Path.Combine(temp.Path, "Films")).FullName;
        foreach (var name in new[] { "Heat (1995).mkv", "Alien (1979).mkv", "Ran (1985).mkv" })
        {
            File.WriteAllBytes(Path.Combine(library, name), new byte[2048]);
        }

        var candidates = await service.ScanAsync(library, new ScanOptions { MinimumBytes = 1024 }, TestContext.Current.CancellationToken);
        var result = await service.ImportAsync(candidates, new LibraryImportOptions { UseTmdb = true, ReadTechnicalProperties = false }, null, TestContext.Current.CancellationToken);

        Assert.Equal(1, prompt.Calls);
        Assert.Equal(ExternalApi.Tmdb, prompt.LastApi);
        Assert.Equal(3, result.WithoutMetadata);
        Assert.Equal(3, titles.Count());
        Assert.Contains(result.Messages, m => m.StartsWith("TMDb écarté", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Choosing_to_interrupt_stops_the_import()
    {
        using var temp = new TempDirectory();
        var (service, titles) = NewImport(temp, new ApiGate(new Prompt(answer: false)));
        var library = Directory.CreateDirectory(Path.Combine(temp.Path, "Films")).FullName;
        File.WriteAllBytes(Path.Combine(library, "Heat (1995).mkv"), new byte[2048]);

        var candidates = await service.ScanAsync(library, new ScanOptions { MinimumBytes = 1024 }, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ApiLimitAbortedException>(() =>
            service.ImportAsync(candidates, new LibraryImportOptions { UseTmdb = true, ReadTechnicalProperties = false }, null, TestContext.Current.CancellationToken));
        Assert.Equal(0, titles.Count());
    }

    [Fact]
    public async Task Omdb_quota_asks_once_and_the_imdb_file_takes_over()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        string[] imdbIds = ["tt0113277", "tt0078748"];
        var ids = imdbIds.Select(imdb =>
        {
            var id = titles.Add(new NewTitle(TitleKind.Movie, imdb, 1990));
            titles.Update(titles.Get(id)! with { ImdbId = imdb });
            return id;
        }).ToList();
        var omdbCalls = 0;
        var omdb = new OmdbClient(new HttpClient(new Handler(_ =>
        {
            omdbCalls++;
            return Json("""{"Response":"False","Error":"Request limit reached!"}""");
        })), _ => ValueTask.FromResult<string?>("k123456"));
        var gz = new MemoryStream();
        using (var z = new System.IO.Compression.GZipStream(gz, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            z.Write(Encoding.UTF8.GetBytes("tconst\taverageRating\tnumVotes\ntt0113277\t8.3\t9\ntt0078748\t8.5\t9\n"));
        }

        var dataset = new ImdbRatingsDataset(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(gz.ToArray()) })), temp.Path);
        var prompt = new Prompt(answer: true);
        var ratings = new RatingsService(omdb, dataset, titles, new MetadataRepository(db), new SettingsStore(db), NullLogger<RatingsService>.Instance, new ApiGate(prompt));

        foreach (var id in ids)
        {
            await ratings.EnrichAsync(id, TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, omdbCalls);
        Assert.Equal(1, prompt.Calls);
        Assert.Equal(ExternalApi.Omdb, prompt.LastApi);
        Assert.Equal(8.5, titles.Get(ids[1])!.ImdbRating);
    }

    private static (LibraryImportService Service, TitleRepository Titles) NewImport(TempDirectory temp, ApiGate gate)
    {
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        var images = new ImageStore(db, Path.Combine(temp.Path, "img"));
        // TMDb répond toujours 429 (limite de débit), sans délai d'attente pour garder le test rapide.
        var tmdb = new TmdbClient(new HttpClient(new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return response;
        })), _ => ValueTask.FromResult<string?>("token"));
        var metadata = new MetadataService(tmdb, new MetadataRepository(db), images, NullLogger<MetadataService>.Instance, gate: gate);
        var service = new LibraryImportService(titles, new MediaFileRepository(db), images, metadata, () => new MediaProbe("absent", "absent"),
            NullLogger<LibraryImportService>.Instance, gate: gate);
        return (service, titles);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Prompt(bool answer) : IApiLimitPrompt
    {
        public int Calls { get; private set; }

        public ExternalApi? LastApi { get; private set; }

        public Task<bool> ContinueWithoutAsync(ExternalApi api, string reason, string consequence)
        {
            Calls++;
            LastApi = api;
            return Task.FromResult(answer);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
