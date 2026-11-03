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

public sealed class PeopleTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _titles;
    private readonly MetadataRepository _metadata;
    private readonly PeopleRepository _people;
    private readonly long _heat;
    private readonly long _insider;

    public PeopleTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _titles = new TitleRepository(_db);
        _metadata = new MetadataRepository(_db);
        _people = new PeopleRepository(_db);
        _heat = _metadata.Create(Movie(949, "Heat", 1995, [
            new MetadataCredit(CreditRole.Director, 638, "Michael Mann", null, 0, "/mann.jpg"),
            new MetadataCredit(CreditRole.Actor, 1158, "Al Pacino", "Vincent Hanna", 0, "/pacino.jpg"),
            new MetadataCredit(CreditRole.Actor, 380, "Robert De Niro", "Neil McCauley", 1, null),
        ]), ImportOptions.Default);
        _insider = _metadata.Create(Movie(9008, "The Insider", 1999, [
            new MetadataCredit(CreditRole.Director, 638, "Michael Mann", null, 0, "/mann.jpg"),
            new MetadataCredit(CreditRole.Actor, 1158, "Al Pacino", "Lowell Bergman", 0, "/pacino.jpg"),
        ]), ImportOptions.Default);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Credits_filmography_and_search_link_people_to_catalogue_titles()
    {
        var credits = _people.Credits(_heat);
        Assert.Equal(["Michael Mann", "Al Pacino", "Robert De Niro"], credits.Select(c => c.Name));
        Assert.Equal("/pacino.jpg", credits[1].ProfilePath);

        var pacino = credits[1].PersonId;
        var films = _people.Filmography(pacino);
        Assert.Equal([_insider, _heat], films.Select(f => f.TitleId));
        Assert.Equal("1999 · rôle : Lowell Bergman", films[0].Subtitle);

        var found = _people.Search("pac");
        Assert.Equal(("Al Pacino", 2), (found[0].Name, found[0].TitleCount));
        Assert.Equal(3, _people.Search(null).Count);
    }

    [Fact]
    public async Task Person_page_fetches_tmdb_details_once_with_english_fallback_and_photo()
    {
        var calls = new List<string>();
        var handler = new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath + request.RequestUri.Query;
            calls.Add(path);
            if (request.RequestUri.Host == TmdbClient.ImageHost)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, 1, 2]) };
            }

            return Json(path.Contains("language=en-US", StringComparison.Ordinal)
                ? """{"id":1158,"name":"Al Pacino","biography":"Alfredo James Pacino is an American actor."}"""
                : """{"id":1158,"name":"Al Pacino","biography":"","birthday":"1940-04-25","place_of_birth":"New York City, USA","known_for_department":"Acting","profile_path":"/pacino.jpg","imdb_id":"nm0000199"}""");
        });
        var tmdb = new TmdbClient(new HttpClient(handler), _ => ValueTask.FromResult<string?>("token"));
        var service = new PeopleService(_people, tmdb, new ImageStore(_db, Path.Combine(_temp.Path, "img")), NullLogger<PeopleService>.Instance);
        var pacino = _people.Credits(_heat)[1].PersonId;

        var page = new PersonViewModel(service, pacino);
        await page.LoadDetailsAsync();

        Assert.Equal("Alfredo James Pacino is an American actor.", page.Biography);
        Assert.Equal("Interprétation", page.KnownFor);
        Assert.StartsWith("Né(e) le", page.Dates, StringComparison.Ordinal);
        Assert.EndsWith("New York City, USA", page.Dates, StringComparison.Ordinal);
        Assert.Equal("https://www.imdb.com/name/nm0000199/", page.ImdbUrl);
        Assert.True(File.Exists(page.PhotoPath));
        Assert.Equal(2, page.Titles.Count);

        var before = calls.Count;
        await new PersonViewModel(service, pacino).LoadDetailsAsync();
        Assert.Equal(before, calls.Count);

        page.Choose(page.Titles[1]);
        Assert.Equal(_heat, page.ChosenTitleId);
    }

    [Fact]
    public async Task Cast_photos_are_downloaded_for_people_with_a_tmdb_picture()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) });
        var service = new PeopleService(_people, new TmdbClient(new HttpClient(handler), _ => ValueTask.FromResult<string?>("token")), new ImageStore(_db, Path.Combine(_temp.Path, "img")), NullLogger<PeopleService>.Instance);

        Assert.Equal(1, await service.EnsureCreditPhotosAsync(_people.Credits(_heat).Where(c => c.Role == "actor"), cancellationToken: TestContext.Current.CancellationToken));
        Assert.NotNull(_people.Credits(_heat).Single(c => c.Name == "Al Pacino").PhotoRelativePath);
        Assert.Null(_people.Credits(_heat).Single(c => c.Name == "Robert De Niro").PhotoRelativePath);
    }

    [Fact]
    public async Task Actors_and_directors_replace_the_posters_until_a_library_filter_is_chosen()
    {
        var service = new PeopleService(_people, new TmdbClient(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))), _ => ValueTask.FromResult<string?>(null)),
            new ImageStore(_db, Path.Combine(_temp.Path, "img")), NullLogger<PeopleService>.Instance);
        var files = new MediaFileRepository(_db);
        var import = new LibraryImportService(_titles, files, new ImageStore(_db, Path.Combine(_temp.Path, "img")), null,
            () => new Cinedex.Media.MediaProbe("absent", "absent"), NullLogger<LibraryImportService>.Instance);
        var dialogs = new MainWindowViewModelTests.FakeDialogs();
        var main = new MainWindowViewModel(_titles, new Cinedex.Core.AppPaths(_temp.Path, isPortable: false), dialogs,
            media: new MediaContext(files, import, new SettingsStore(_db), People: service));
        main.Load();
        Assert.True(main.IsTitlesView);

        // Acteurs : galerie dans la zone centrale, avec sa recherche.
        main.OpenPeopleCommand.Execute(null);
        Assert.True(main.IsActorsView);
        Assert.False(main.IsTitlesView);
        Assert.Equal(["Al Pacino", "Michael Mann", "Robert De Niro"], main.PeopleBrowser!.People.Select(p => p.Name).Order(StringComparer.Ordinal));
        main.PeopleBrowser.Query = "niro";
        Assert.Equal("Robert De Niro", Assert.Single(main.PeopleBrowser.People).Name);

        // Réalisateurs, puis retour aux affiches par un filtre de la bibliothèque (même celui déjà choisi).
        main.OpenDirectorsCommand.Execute(null);
        Assert.True(main.IsDirectorsView);
        Assert.Equal("Michael Mann", Assert.Single(main.PeopleBrowser!.People).Name);
        main.SelectedMainFilter = main.Filters[0];
        Assert.True(main.IsTitlesView);
        Assert.Null(main.PeopleBrowser);

        // Un clic sur une personne : ses informations et ses films dans le bandeau droit ; un film y ramène aux affiches.
        main.OpenPeopleCommand.Execute(null);
        var pacino = main.PeopleBrowser!.People.Single(p => p.Name == "Al Pacino");
        await main.SelectPersonCommand.ExecuteAsync(pacino);
        Assert.True(main.HasPersonPage);
        Assert.Equal("Al Pacino", main.PersonPage!.Name);
        Assert.Contains(pacino.Id, main.SelectedPersonIds);
        Assert.Equal(2, main.PersonPage.Titles.Count);
        main.ChoosePersonTitleCommand.Execute(main.PersonPage.Titles.Single(t => t.TitleId == _heat));
        Assert.True(main.IsTitlesView);
        Assert.False(main.HasPersonPage);
        Assert.Equal(_heat, main.Detail!.Id);

        // Changer de galerie efface la personne choisie.
        main.OpenPeopleCommand.Execute(null);
        await main.SelectPersonCommand.ExecuteAsync(main.PeopleBrowser!.People[0]);
        main.OpenDirectorsCommand.Execute(null);
        Assert.False(main.HasPersonPage);

        // Double-clic : la fiche complète ; un film choisi dans la fiche ramène aux affiches, sur ce film.
        dialogs.PersonPage = page =>
        {
            page.Choose(page.Titles.Single(t => t.TitleId == _insider));
            return true;
        };
        await main.OpenBrowsedPersonCommand.ExecuteAsync(main.PeopleBrowser!.People[0]);
        Assert.True(main.IsTitlesView);
        Assert.Equal(_insider, main.Detail!.Id);
    }

    private static MetadataRecord Movie(long id, string title, int year, IReadOnlyList<MetadataCredit> credits) => new()
    {
        Kind = TitleKind.Movie,
        SourceId = id,
        Title = title,
        Year = year,
        Credits = credits,
    };

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    [Fact]
    public void Directors_are_listed_with_only_the_films_they_directed()
    {
        // Al Pacino réalise aussi un film du catalogue : il apparaît parmi les réalisateurs, compté sur ce film seul.
        var looking = _metadata.Create(Movie(1, "Looking for Richard", 1996, [new MetadataCredit(CreditRole.Director, 1158, "Al Pacino", null, 0, null)]), ImportOptions.Default);

        var directors = _people.Search(null, role: "director");
        Assert.Equal([("Michael Mann", 2), ("Al Pacino", 1)], directors.Select(d => (d.Name, d.TitleCount)));
        Assert.Empty(_people.Search("niro", role: "director"));

        var pacino = directors[1].Id;
        Assert.Equal([looking], _people.Filmography(pacino, "director").Select(f => f.TitleId));
        Assert.Equal(3, _people.Filmography(pacino).Count);
    }

    [Fact]
    public async Task Director_pages_and_directory_download_missing_records()
    {
        var calls = new List<string>();
        var handler = new Handler(request =>
        {
            calls.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.Host == TmdbClient.ImageHost
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) }
                : Json("""{"id":638,"name":"Michael Mann","biography":"Réalisateur de Heat.","birthday":"1943-02-05","known_for_department":"Directing","profile_path":"/mann.jpg"}""");
        });
        var service = new PeopleService(_people, new TmdbClient(new HttpClient(handler), _ => ValueTask.FromResult<string?>("token")), new ImageStore(_db, Path.Combine(_temp.Path, "img")), NullLogger<PeopleService>.Instance);

        var directory = new PeopleBrowserViewModel(service, "director");
        Assert.True(directory.IsDirectors);
        var mann = Assert.Single(directory.People);
        Assert.Equal("Michael Mann", mann.Name);

        await directory.DownloadDetailsCommand.ExecuteAsync(null);
        Assert.Contains("/3/person/638", calls);
        Assert.Equal("Réalisateur de Heat.", _people.Get(mann.Id)!.Biography);
        Assert.NotNull(_people.Get(mann.Id)!.PhotoRelativePath);

        var page = new PersonViewModel(service, mann.Id, "director");
        Assert.Equal("Films réalisés", page.Heading);
        Assert.Equal([_insider, _heat], page.Titles.Select(t => t.TitleId));
        Assert.Contains("2 film(s) réalisé(s)", page.Count, StringComparison.Ordinal);
    }
}
