using System.Net;
using System.Text;
using Cinedex.App.Services;
using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Metadata.Tmdb;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cinedex.Tests;

/// <summary>« Mettre à jour via TMDb » doit compléter les entrées existantes et ne jamais en créer.</summary>
public sealed class UpdateViaTmdbTests
{
    private const string Heat = """
        {"id":949,"title":"Heat","original_title":"Heat","overview":"Un braqueur et un flic.","release_date":"1995-12-15",
         "runtime":170,"vote_average":7.9,"vote_count":7000,"poster_path":"/p.jpg","imdb_id":"tt0113277",
         "genres":[{"id":80,"name":"Crime"}],"credits":{"cast":[],"crew":[{"id":638,"name":"Michael Mann","job":"Director"}]}}
        """;

    [Fact]
    public async Task Scanned_titles_are_updated_in_place_without_creating_entries()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        var files = new MediaFileRepository(db);
        var service = NewService(db);

        // Deux entrées issues d'un scan (sans métadonnées) qui désignent le même film, et une introuvable.
        var heat = titles.Add(new NewTitle(TitleKind.Movie, "heat", 1995));
        files.Attach(heat, Path.Combine(temp.Path, "Heat.1995.mkv"), 10);
        var heatCopy = titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1996));
        var unknown = titles.Add(new NewTitle(TitleKind.Movie, "Zzz inconnu", 2001));
        var locked = titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        titles.Update(titles.Get(locked)! with { IsLocked = true });

        var targets = new[] { heat, heatCopy, unknown, locked }.Select(id => titles.Get(id)!).ToList();
        var result = await service.UpdateTitlesAsync(targets, ImportOptions.Default, null, TestContext.Current.CancellationToken);

        Assert.Equal(4, titles.Count());
        Assert.Equal((1, 1, 1, 1), (result.Updated, result.Duplicates, result.NotFound, result.Skipped));

        var updated = titles.Get(heat)!;
        Assert.Equal("Heat", updated.Title);
        Assert.Equal(949, updated.TmdbId);
        Assert.Equal(170, updated.RuntimeMinutes);
        Assert.Equal("tt0113277", updated.ImdbId);
        Assert.Single(files.FilesOf(heat));
        Assert.Null(titles.Get(heatCopy)!.TmdbId);

        // Un second passage rafraîchit la même entrée, toujours sans en créer.
        var again = await service.UpdateTitlesAsync([titles.Get(heat)!], ImportOptions.Default, null, TestContext.Current.CancellationToken);
        Assert.Equal(1, again.Updated);
        Assert.Equal(4, titles.Count());
    }

    private static MetadataService NewService(CatalogDatabase db)
    {
        var handler = new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            if (path.EndsWith("/search/movie", StringComparison.Ordinal))
            {
                return Json(query.Contains("query=Heat", StringComparison.OrdinalIgnoreCase)
                    ? """{"results":[{"id":949,"title":"Heat","release_date":"1995-12-15"}],"total_results":1}"""
                    : """{"results":[],"total_results":0}""");
            }

            if (path.EndsWith("/search/tv", StringComparison.Ordinal))
            {
                return Json("""{"results":[],"total_results":0}""");
            }

            return path.EndsWith("/movie/949", StringComparison.Ordinal) ? Json(Heat) : new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var tmdb = new TmdbClient(new HttpClient(handler), _ => ValueTask.FromResult<string?>("token"));
        return new MetadataService(tmdb, new MetadataRepository(db), new ImageStore(db, Path.Combine(Path.GetDirectoryName(db.DatabasePath)!, "images")), NullLogger<MetadataService>.Instance);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
