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

/// <summary>Collections (sagas TMDb) : fiche et décors téléchargés, films manquants, identification par le nom.</summary>
public sealed class CollectionTests : IDisposable
{
    private const string MovieDetails = """
        {"id":105,"title":"Retour vers le futur","release_date":"1985-07-03","genres":[],"credits":{"cast":[],"crew":[]},
         "belongs_to_collection":{"id":264,"name":"Retour vers le futur - Saga","poster_path":"/p.jpg","backdrop_path":"/b.jpg"}}
        """;

    private const string CollectionDetails = """
        {"id":264,"name":"Retour vers le futur - Saga","overview":"Une trilogie.","poster_path":"/p.jpg","backdrop_path":"/b.jpg",
         "parts":[{"id":165,"title":"Retour vers le futur 2","release_date":"1989-11-22"},
                  {"id":105,"title":"Retour vers le futur","release_date":"1985-07-03"},
                  {"id":196,"title":"Retour vers le futur 3","release_date":"1990-05-25"}]}
        """;

    // En-tête PNG minimal (signature et bloc IHDR de 1 × 1 pixel) : reconnu par ImageStore.
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0, 0, 0, 0, 0];

    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _titles;
    private readonly CollectionRepository _collections;
    private readonly SettingsStore _settings;
    private readonly CollectionService _service;
    private readonly MetadataService _metadata;
    private readonly List<string> _requests = [];

    public CollectionTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _titles = new TitleRepository(_db);
        _collections = new CollectionRepository(_db);
        _settings = new SettingsStore(_db);
        var handler = new Handler(request =>
        {
            var uri = request.RequestUri!;
            lock (_requests)
            {
                _requests.Add(uri.AbsolutePath);
            }

            if (uri.Host == TmdbClient.ImageHost)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };
            }

            return uri.AbsolutePath switch
            {
                "/3/movie/105" => Json(MovieDetails),
                "/3/collection/264" => Json(CollectionDetails),
                "/3/search/collection" => Json("""{"results":[{"id":264,"name":"Retour vers le futur - Saga"},{"id":999,"name":"Retour vers le futur (dessin animé)"}],"total_results":2}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });
        var tmdb = new TmdbClient(new HttpClient(handler), _ => ValueTask.FromResult<string?>("token"));
        var images = new ImageStore(_db, Path.Combine(_temp.Path, "images"));
        _service = new CollectionService(_collections, tmdb, images, NullLogger<CollectionService>.Instance, _settings);
        _metadata = new MetadataService(tmdb, new MetadataRepository(_db), images, NullLogger<MetadataService>.Instance, settings: _settings, collections: _service);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Importing_a_film_downloads_its_collection_details_and_images()
    {
        await _metadata.ImportAsync(new MetadataSearchResult(TitleKind.Movie, 105, "Retour vers le futur", null, 1985, null, null, null), ImportOptions.Default, TestContext.Current.CancellationToken);

        var summary = Assert.Single(_collections.List());
        Assert.Equal((264L, 1, 3), (summary.TmdbId!.Value, summary.Owned, summary.Parts));
        var detail = _collections.Get(summary.Id)!;
        Assert.Equal("Une trilogie.", detail.Overview);
        Assert.Equal([1985, 1989, 1990], detail.Parts.Select(p => p.Year!.Value));
        Assert.NotNull(detail.PosterRelativePath);
        Assert.NotNull(detail.BackdropRelativePath);
        Assert.NotNull(detail.FetchedAt);
    }

    [Fact]
    public async Task Disabled_setting_skips_the_collection_download()
    {
        new DisplaySettings { ManageCollections = false }.Save(_settings);

        await _metadata.ImportAsync(new MetadataSearchResult(TitleKind.Movie, 105, "Retour vers le futur", null, 1985, null, null, null), ImportOptions.Default, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("/3/collection/264", _requests);
        Assert.Null(_collections.Get(Assert.Single(_collections.List()).Id)!.FetchedAt);
    }

    [Fact]
    public void Collections_are_managed_by_default()
    {
        Assert.True(DisplaySettings.Load(_settings).ManageCollections);
        new DisplaySettings { ManageCollections = false }.Save(_settings);
        Assert.False(DisplaySettings.Load(_settings).ManageCollections);
    }

    [Fact]
    public async Task Window_lists_owned_and_missing_films_in_release_order()
    {
        await _metadata.ImportAsync(new MetadataSearchResult(TitleKind.Movie, 105, "Retour vers le futur", null, 1985, null, null, null), ImportOptions.Default, TestContext.Current.CancellationToken);
        var extra = _titles.Add(new NewTitle(TitleKind.Movie, "Making-of", 2010));
        _titles.ApplyBulk([extra], new BulkEdit { CollectionId = _collections.List()[0].Id });

        var vm = new CollectionsViewModel(_service);

        Assert.Equal("Retour vers le futur - Saga", vm.Name);
        Assert.Equal(
            ["Retour vers le futur", "Retour vers le futur 2", "Retour vers le futur 3", "Making-of"],
            vm.Films.Select(f => f.Title));
        Assert.Equal([true, false, false, true], vm.Films.Select(f => f.IsOwned));
        Assert.Equal("https://www.themoviedb.org/collection/264?language=fr-FR", vm.TmdbUrl);
        Assert.Contains("2 manquant", vm.Summary, StringComparison.Ordinal);

        vm.Choose(vm.Films[0]);
        Assert.Equal(new MetadataRepository(_db).FindBySource(TitleKind.Movie, 105), vm.ChosenTitleId);
    }

    [Fact]
    public async Task Manual_collection_is_identified_by_name_and_merged_with_the_tmdb_one()
    {
        await _metadata.ImportAsync(new MetadataSearchResult(TitleKind.Movie, 105, "Retour vers le futur", null, 1985, null, null, null), ImportOptions.Default, TestContext.Current.CancellationToken);
        var manual = _titles.CreateCollection("Retour vers le futur");
        var other = _titles.Add(new NewTitle(TitleKind.Movie, "Retour vers le futur 2", 1989));
        _titles.ApplyBulk([other], new BulkEdit { CollectionId = manual });

        var result = await _service.SyncAsync(all: false, null, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Identified);
        var merged = Assert.Single(_collections.List());
        Assert.Equal((264L, 2), (merged.TmdbId!.Value, merged.Owned));
    }

    [Fact]
    public void Rename_and_delete_keep_the_films()
    {
        var id = _titles.CreateCollection("Coffret");
        var title = _titles.Add(new NewTitle(TitleKind.Movie, "Film", 2000));
        _titles.ApplyBulk([title], new BulkEdit { CollectionId = id });
        var vm = new CollectionsViewModel(_service) { EditedName = "Mon coffret" };

        vm.RenameCommand.Execute(null);
        Assert.Equal("Mon coffret", Assert.Single(_collections.List()).Name);

        vm.DeleteCommand.Execute(null);
        Assert.Empty(_collections.List());
        Assert.Null(_titles.Get(title)!.CollectionId);
        Assert.True(vm.Changed);
    }

    [Theory]
    [InlineData("Star Wars - Saga", "Star Wars Collection")]
    [InlineData("Le Seigneur des anneaux", "Le Seigneur des Anneaux - Saga")]
    [InlineData("Les Bronzés", "Les bronzes : la collection")]
    public void Names_are_compared_without_noise_words(string a, string b) =>
        Assert.Equal(CollectionService.Normalize(a), CollectionService.Normalize(b));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
