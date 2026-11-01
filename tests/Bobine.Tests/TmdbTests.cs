using System.Net;
using System.Text;
using Bobine.Core.Metadata;
using Bobine.Core.Titles;
using Bobine.Data;
using Bobine.Metadata.Tmdb;
using Dapper;

namespace Bobine.Tests;

public sealed class TmdbTests
{
    private const string MovieJson = """
    {
      "id": 949, "title": "Heat", "original_title": "Heat", "overview": "Un braqueur et un flic.",
      "tagline": "A Los Angeles crime saga", "release_date": "1995-12-15", "runtime": 170,
      "vote_average": 7.9, "vote_count": 7000, "poster_path": "/poster.jpg", "backdrop_path": "/back.jpg",
      "homepage": "javascript:alert(1)", "imdb_id": "tt0113277", "adult": false,
      "genres": [{"id": 80, "name": "Crime"}, {"id": 18, "name": "Drama"}],
      "production_countries": [{"iso_3166_1": "US", "name": "United States"}],
      "spoken_languages": [{"iso_639_1": "en", "name": "English"}],
      "production_companies": [{"id": 1, "name": "Regency"}],
      "belongs_to_collection": null,
      "credits": {
        "cast": [
          {"id": 1158, "name": "Al Pacino", "character": "Vincent Hanna", "order": 0, "profile_path": "/al.jpg"},
          {"id": 380, "name": "Robert De Niro", "character": "Neil McCauley", "order": 1, "profile_path": null},
          {"id": 9, "name": "Val Kilmer", "character": "Chris", "order": 2}
        ],
        "crew": [
          {"id": 638, "name": "Michael Mann", "job": "Director", "department": "Directing"},
          {"id": 638, "name": "Michael Mann", "job": "Screenplay", "department": "Writing"},
          {"id": 77, "name": "Elliot Goldenthal", "job": "Original Music Composer", "department": "Sound"},
          {"id": 5, "name": "Someone", "job": "Producer", "department": "Production"}
        ]
      },
      "release_dates": {"results": [
        {"iso_3166_1": "US", "release_dates": [{"certification": "R", "type": 3}]},
        {"iso_3166_1": "FR", "release_dates": [{"certification": "", "type": 1}, {"certification": "12", "type": 3}]}
      ]},
      "keywords": {"keywords": [{"id": 1, "name": "bank robbery"}]},
      "videos": {"results": [
        {"site": "YouTube", "key": "bad key!", "type": "Trailer", "official": true, "iso_639_1": "fr"},
        {"site": "YouTube", "key": "abc123XYZ_-", "type": "Trailer", "official": true, "iso_639_1": "en"},
        {"site": "Vimeo", "key": "zzz", "type": "Trailer", "official": true, "iso_639_1": "fr"}
      ]}
    }
    """;

    [Fact]
    public async Task Details_are_mapped_and_sanitised()
    {
        var handler = new FakeHandler(_ => Json(MovieJson));
        var client = NewClient(handler);

        var record = await client.GetDetailsAsync(TitleKind.Movie, 949, TestContext.Current.CancellationToken);

        Assert.Equal("Heat", record.Title);
        Assert.Equal(1995, record.Year);
        Assert.Equal("12", record.Certification);
        Assert.Equal("tt0113277", record.ImdbId);
        Assert.Null(record.Homepage);
        Assert.Equal("https://www.youtube.com/watch?v=abc123XYZ_-", record.TrailerUrl);
        Assert.Equal([80L, 18L], record.GenreSourceIds);
        Assert.Equal(["Al Pacino", "Robert De Niro", "Val Kilmer"], record.Credits.Where(c => c.Role == CreditRole.Actor).Select(c => c.Name));
        Assert.Single(record.Credits, c => c.Role == CreditRole.Director);
        Assert.Single(record.Credits, c => c.Role == CreditRole.Writer);
        Assert.Single(record.Credits, c => c.Role == CreditRole.Composer);
        Assert.DoesNotContain(record.Credits, c => c.Name == "Someone");
    }

    [Fact]
    public async Task Token_goes_in_the_header_never_in_the_url()
    {
        var handler = new FakeHandler(_ => Json(MovieJson));
        var client = NewClient(handler, token: "secret-token");

        await client.GetDetailsAsync(TitleKind.Movie, 949, TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https", request.RequestUri!.Scheme);
        Assert.Equal(TmdbClient.ApiHost, request.RequestUri.Host);
        Assert.DoesNotContain("secret-token", request.RequestUri.ToString(), StringComparison.Ordinal);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("secret-token", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Missing_or_refused_token_raises_an_authentication_error()
    {
        var none = NewClient(new FakeHandler(_ => Json(MovieJson)), token: null);
        await Assert.ThrowsAsync<TmdbAuthenticationException>(() => none.SearchAsync("Heat", cancellationToken: TestContext.Current.CancellationToken));

        var refused = NewClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        await Assert.ThrowsAsync<TmdbAuthenticationException>(() => refused.SearchAsync("Heat", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Search_queries_movies_and_series_and_escapes_the_text()
    {
        var handler = new FakeHandler(request => Json(request.RequestUri!.AbsolutePath.EndsWith("/movie", StringComparison.Ordinal)
            ? """{"results":[{"id":949,"title":"Heat","release_date":"1995-12-15"}],"total_results":1}"""
            : """{"results":[{"id":1,"name":"Heat Show","first_air_date":""}],"total_results":1}"""));
        var client = NewClient(handler);

        var results = await client.SearchAsync("Heat & co", 1995, TestContext.Current.CancellationToken);

        Assert.Equal([TitleKind.Movie, TitleKind.Series], results.Select(r => r.Kind));
        Assert.Equal(1995, results[0].Year);
        Assert.Null(results[1].Year);
        Assert.All(handler.Requests, r => Assert.Contains("query=Heat%20%26%20co", r.RequestUri!.Query, StringComparison.Ordinal));
        Assert.Contains(handler.Requests, r => r.RequestUri!.Query.Contains("year=1995", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Imdb_id_search_uses_find()
    {
        var handler = new FakeHandler(_ => Json("""{"movie_results":[{"id":949,"title":"Heat"}],"tv_results":[]}"""));
        var client = NewClient(handler);

        var result = Assert.Single(await client.SearchAsync("tt0113277", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(949, result.SourceId);
        Assert.StartsWith("/3/find/tt0113277", Assert.Single(handler.Requests).RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("/a.exe")]
    [InlineData("https://evil.example/x.jpg")]
    public async Task Image_paths_are_validated(string path)
    {
        var client = NewClient(new FakeHandler(_ => Json("{}")));

        await Assert.ThrowsAsync<ArgumentException>(() => client.DownloadImageAsync(path, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Server_errors_are_retried()
    {
        var calls = 0;
        var handler = new FakeHandler(_ => ++calls < 2 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json(MovieJson));
        var client = NewClient(handler);

        var record = await client.GetDetailsAsync(TitleKind.Movie, 949, TestContext.Current.CancellationToken);

        Assert.Equal("Heat", record.Title);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Import_creates_title_credits_genres_and_respects_lock()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "bobine.db"));
        db.Initialize(temp.Path);
        var metadata = new MetadataRepository(db);
        var titles = new TitleRepository(db);
        var record = await NewClient(new FakeHandler(_ => Json(MovieJson))).GetDetailsAsync(TitleKind.Movie, 949, TestContext.Current.CancellationToken);

        var id = metadata.Create(record, ImportOptions.Default with { MaxActors = 2 });

        var detail = titles.Get(id)!;
        Assert.Equal("Heat", detail.Title);
        Assert.Equal(170, detail.RuntimeMinutes);
        Assert.Equal(949, detail.TmdbId);
        Assert.Equal(["crime", "drama"], detail.GenreCodes.Order());
        Assert.Equal(1, detail.Copy!.Number);
        using (var connection = db.Open())
        {
            Assert.Equal(2, connection.ExecuteScalar<int>("SELECT count(*) FROM credit WHERE role = 'actor';"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT count(*) FROM credit WHERE role = 'director';"));
        }

        Assert.Throws<DuplicateTitleException>(() => metadata.Create(record, ImportOptions.Default));

        titles.Update(detail with { IsLocked = true, Title = "Mon titre" });
        Assert.False(metadata.Apply(id, record, ImportOptions.Default));
        Assert.Equal("Mon titre", titles.Get(id)!.Title);

        titles.Update(titles.Get(id)! with { IsLocked = false });
        Assert.True(metadata.Apply(id, record, ImportOptions.Default with { UpdateTitle = false }));
        Assert.Equal("Mon titre", titles.Get(id)!.Title);
    }

    [Fact]
    public void Image_store_accepts_only_real_images_and_deduplicates()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "bobine.db"));
        db.Initialize(temp.Path);
        var titleId = new TitleRepository(db).Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var store = new ImageStore(db, Path.Combine(temp.Path, "images"));
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 1, 0x2C, 0, 0, 1, 0xC2];

        var first = store.Save(png);
        var second = store.Save(png);
        store.Attach(titleId, first, "poster");

        Assert.Equal(first, second);
        Assert.Equal((300, 450), ImageStore.ReadDimensions(png));
        Assert.True(File.Exists(store.PathFor(titleId, "poster")));
        Assert.Throws<InvalidDataException>(() => store.Save("<html>"u8));
        Assert.Null(store.ResolveInside("../../bobine.db"));
    }

    private static TmdbClient NewClient(FakeHandler handler, string? token = "test-token") =>
        new(new HttpClient(handler), _ => ValueTask.FromResult(token));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            return Task.FromResult(respond(request));
        }
    }
}
