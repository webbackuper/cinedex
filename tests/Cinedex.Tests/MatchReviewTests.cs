using System.Net;
using System.Text;
using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Metadata.Tmdb;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cinedex.Tests;

/// <summary>Rapprochements TMDb ambigus : jamais devinés, proposés dans « À vérifier ».</summary>
public sealed class MatchReviewTests : IDisposable
{
    private const string HeatDetails = """
        {"id":949,"title":"Heat","original_title":"Heat","overview":"Un braqueur et un flic.","release_date":"1995-12-15",
         "runtime":170,"vote_average":7.9,"vote_count":7000,"imdb_id":"tt0113277","genres":[],"credits":{"cast":[],"crew":[]}}
        """;

    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _titles;
    private readonly MatchReviewRepository _reviews;
    private readonly MetadataService _metadata;

    public MatchReviewTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _titles = new TitleRepository(_db);
        _reviews = new MatchReviewRepository(_db);
        var handler = new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/search/movie", StringComparison.Ordinal))
            {
                // Deux « Heat » : le film de 1995 et un homonyme de 1986.
                return Json("""{"results":[{"id":949,"title":"Heat","release_date":"1995-12-15"},{"id":31,"title":"Heat","release_date":"1986-03-14"}],"total_results":2}""");
            }

            if (path.EndsWith("/find/tt0113277", StringComparison.Ordinal))
            {
                return Json("""{"movie_results":[{"id":949,"title":"Heat","release_date":"1995-12-15"}],"tv_results":[]}""");
            }

            if (path.EndsWith("/search/tv", StringComparison.Ordinal))
            {
                return Json("""{"results":[],"total_results":0}""");
            }

            return path.EndsWith("/movie/949", StringComparison.Ordinal) ? Json(HeatDetails) : new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var tmdb = new TmdbClient(new HttpClient(handler), _ => ValueTask.FromResult<string?>("token"));
        _metadata = new MetadataService(tmdb, new MetadataRepository(_db), new ImageStore(_db, Path.Combine(_temp.Path, "images")),
            NullLogger<MetadataService>.Instance, reviews: _reviews);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Decision_rules()
    {
        MetadataSearchResult R(long id, int? year) => new(TitleKind.Movie, id, "Heat", null, year, null, null, null);
        Assert.Equal(949, Identification.Decide([R(949, 1995), R(31, 1986)], 1995, "q").Match!.SourceId);
        Assert.Equal(2, Identification.Decide([R(949, 1995), R(31, 1986)], null, "q").Candidates.Count);
        Assert.True(Identification.Decide([R(949, 1995)], null, "q").NeedsReview);
        Assert.Equal(2, Identification.Decide([R(1, 1995), R(2, 1996)], 1995, "q").Candidates.Count);
        Assert.Equal(1, Identification.Decide([R(1, 1995), R(2, 1996)], 1995, "q", takeFirst: true).Match!.SourceId);
        Assert.Equal(2, Identification.Decide([R(1, 1970), R(2, 1980)], 2020, "q").Candidates.Count);
        Assert.False(Identification.Decide([], 2020, "q").NeedsReview);
    }

    [Fact]
    public async Task Title_without_year_goes_to_review_then_the_choice_updates_it_in_place()
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", null));

        var result = await _metadata.UpdateTitlesAsync([_titles.Get(id)!], ImportOptions.Default, null, TestContext.Current.CancellationToken);

        Assert.Equal((0, 1), (result.Updated, result.ToReview));
        Assert.Null(_titles.Get(id)!.TmdbId);
        var review = Assert.Single(_reviews.List());
        Assert.Equal([949L, 31L], review.Candidates.Select(c => c.SourceId));

        var vm = new MatchReviewViewModel(_reviews, _metadata);
        var item = Assert.Single(vm.Items);
        Assert.Contains("themoviedb.org/search/movie?query=Heat", item.SearchUrl, StringComparison.Ordinal);
        Assert.Equal("https://www.themoviedb.org/movie/949?language=fr-FR", item.Candidates[0].TmdbUrl);

        await vm.ChooseCommand.ExecuteAsync(item.Candidates[0]);

        Assert.Equal(949, _titles.Get(id)!.TmdbId);
        Assert.Equal(1, _titles.Count());
        Assert.Empty(vm.Items);
        Assert.True(vm.Changed);
        Assert.Equal(0, _reviews.Count());
    }

    [Fact]
    public async Task Dismissing_keeps_the_title_without_metadata()
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", null));
        await _metadata.UpdateTitlesAsync([_titles.Get(id)!], ImportOptions.Default, null, TestContext.Current.CancellationToken);
        var vm = new MatchReviewViewModel(_reviews, _metadata);

        await vm.DismissCommand.ExecuteAsync(null);

        Assert.Equal(0, _reviews.Count());
        Assert.Null(_titles.Get(id)!.TmdbId);
    }

    [Fact]
    public async Task Ambiguous_scan_import_creates_the_title_and_asks()
    {
        var files = new MediaFileRepository(_db);
        var import = new LibraryImportService(_titles, files, new ImageStore(_db, Path.Combine(_temp.Path, "images")), _metadata,
            () => new Cinedex.Media.MediaProbe("absent", "absent"), NullLogger<LibraryImportService>.Instance);
        var folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "Films")).FullName;
        File.WriteAllBytes(Path.Combine(folder, "Heat.mkv"), new byte[10]);

        var candidates = await import.FromPathsAsync([Path.Combine(folder, "Heat.mkv")], new Cinedex.Media.ScanOptions(), TestContext.Current.CancellationToken);
        var result = await import.ImportAsync(candidates, new LibraryImportOptions { ReadTechnicalProperties = false }, null, TestContext.Current.CancellationToken);

        Assert.Equal((0, 1, 1), (result.Added, result.WithoutMetadata, result.ToReview));
        Assert.Equal("Heat.mkv", Path.GetFileName(Assert.Single(_reviews.List()).FilePath));
    }

    [Fact]
    public async Task Titles_with_a_known_identifier_are_processed_first()
    {
        var plain = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", null));
        var known = _titles.Add(new NewTitle(TitleKind.Movie, "Heat bis", null));
        _titles.Update(_titles.Get(known)! with { ImdbId = "tt0113277" });
        var order = new List<string>();

        await _metadata.UpdateTitlesAsync(
            [_titles.Get(plain)!, _titles.Get(known)!], ImportOptions.Default, new SyncProgress(order.Add), TestContext.Current.CancellationToken);

        Assert.Equal(2, order.Count);
        Assert.Contains("Heat bis", order[0], StringComparison.Ordinal);
        Assert.Equal(949, _titles.Get(known)!.TmdbId);
        Assert.Equal(plain, Assert.Single(_reviews.List()).TitleId);
    }

    private sealed class SyncProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
