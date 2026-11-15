using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Metadata.Tmdb;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cinedex.Tests;

/// <summary>
/// Collections et genres dans la vue centrale : une galerie de vignettes, on entre dans l'une d'elles pour
/// voir ses films dans la liste ordinaire, et l'on revient d'un clic. Le sous-menu Genres du bandeau et la
/// vue centrale disent la même chose.
/// </summary>
public sealed class GalleryTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _titles;
    private readonly MainWindowViewModel _vm;
    private readonly long _saga;
    private readonly long _first;
    private readonly long _second;
    private readonly long _alone;

    public GalleryTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _titles = new TitleRepository(_db);
        var collections = new CollectionRepository(_db);
        var tmdb = new TmdbClient(new HttpClient(new Offline()), _ => ValueTask.FromResult<string?>("jeton"));
        var images = new ImageStore(_db, Path.Combine(_temp.Path, "img"));
        var settings = new SettingsStore(_db);
        var service = new CollectionService(collections, tmdb, images, NullLogger<CollectionService>.Instance, settings);
        var metadata = new MetadataService(tmdb, new MetadataRepository(_db), images, NullLogger<MetadataService>.Instance, settings: settings, collections: service);

        _saga = _titles.CreateCollection("Retour vers le futur - Saga");
        collections.SaveDetails(_saga, new CollectionRecord(264, "Retour vers le futur - Saga", null, null, null,
            [new CollectionPart(105, "Retour vers le futur", 1985, null), new CollectionPart(165, "Retour vers le futur 2", 1989, null),
             new CollectionPart(196, "Retour vers le futur 3", 1990, null)]));
        _first = _titles.Add(new NewTitle(TitleKind.Movie, "Retour vers le futur", 1985));
        _second = _titles.Add(new NewTitle(TitleKind.Movie, "Retour vers le futur 2", 1989));
        _alone = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        _titles.ApplyBulk([_first, _second], new BulkEdit { CollectionId = _saga });

        // Genres : deux films de science-fiction, un policier.
        using (var connection = _db.Open())
        {
            var codes = connection.Query<(long Id, string Code)>("SELECT id, code FROM genre ORDER BY id LIMIT 2;").ToList();
            connection.Execute("INSERT INTO title_genre (title_id, genre_id) VALUES (@t, @g);",
                new[] { new { t = _first, g = codes[0].Id }, new { t = _second, g = codes[0].Id }, new { t = _alone, g = codes[1].Id } });
        }

        _vm = new MainWindowViewModel(_titles, new AppPaths(_temp.Path, isPortable: false), new MainWindowViewModelTests.FakeDialogs(), metadata, images);
        _vm.Load();
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_collections_show_as_cards_one_enters_and_comes_back()
    {
        Assert.True(_vm.IsTitlesView);

        _vm.ShowCollectionGalleryCommand.Execute(null);

        Assert.True(_vm.IsGalleryView);
        Assert.False(_vm.IsTitlesView);
        Assert.True(_vm.IsCollectionsView);
        var card = Assert.Single(_vm.Gallery!.Cards);
        Assert.Equal("Retour vers le futur - Saga", card.Title);
        Assert.Equal("2 / 3 films", card.Subtitle);

        // On entre : la liste ordinaire, avec ses seuls films, et de quoi revenir.
        _vm.OpenGalleryCardCommand.Execute(card);

        Assert.True(_vm.IsTitlesView);
        Assert.Equal([_first, _second], _vm.Titles.Select(t => t.Id).Order());
        Assert.True(_vm.HasTrail);
        Assert.True(_vm.IsInCollection);
        Assert.True(_vm.IsCollectionsView);
        Assert.Equal("Collections", _vm.Trail!.BackLabel);
        Assert.Equal("Retour vers le futur - Saga", _vm.Trail.Title);

        // On revient : la galerie, et plus de fil d'Ariane.
        _vm.BackToGalleryCommand.Execute(null);

        Assert.True(_vm.IsGalleryView);
        Assert.False(_vm.HasTrail);

        // Quitter la galerie par un filtre rend la liste entière.
        _vm.LeaveSideViews();
        Assert.True(_vm.IsTitlesView);
        Assert.False(_vm.IsCollectionsView);
        Assert.Equal(3, _vm.Titles.Count);
    }

    [Fact]
    public void Each_genre_has_a_card_and_stays_in_step_with_the_sidebar()
    {
        var genres = _vm.SideBar.Facets.Single(f => f.Key == SideBarViewModel.GenresKey);

        // Déplier « Genres » dans le bandeau montre leur galerie au centre.
        genres.ToggleCommand.Execute(null);

        Assert.True(_vm.IsGalleryView);
        Assert.Equal(GalleryKind.Genres, _vm.Gallery!.Kind);
        Assert.Equal(2, _vm.Gallery.Cards.Count);
        var two = _vm.Gallery.Cards.Single(c => c.Subtitle.StartsWith("2 ", StringComparison.Ordinal));

        // Entrer par la vignette choisit le genre dans le bandeau.
        _vm.OpenGalleryCardCommand.Execute(two);

        Assert.True(_vm.IsTitlesView);
        Assert.Equal([_first, _second], _vm.Titles.Select(t => t.Id).Order());
        Assert.Equal(two.Title, genres.SelectedItem!.Label);
        Assert.Equal("Genres", _vm.Trail!.BackLabel);
        Assert.Equal(two.Title, _vm.Trail.Title);

        // Choisir l'autre genre dans le bandeau change la liste et le fil d'Ariane ensemble.
        var other = genres.Items.Single(i => i.Label != two.Title);
        genres.SelectedItem = other;

        Assert.Equal([_alone], _vm.Titles.Select(t => t.Id));
        Assert.Equal(other.Label, _vm.Trail!.Title);

        // Revenir à la galerie relâche le genre dans le bandeau.
        _vm.BackToGalleryCommand.Execute(null);

        Assert.True(_vm.IsGalleryView);
        Assert.Null(genres.SelectedItem);
        Assert.False(_vm.HasTrail);
    }

    [Fact]
    public void Another_view_takes_the_place_of_the_gallery_and_of_its_trail()
    {
        _vm.ShowCollectionGalleryCommand.Execute(null);
        _vm.OpenGalleryCardCommand.Execute(_vm.Gallery!.Cards[0]);
        Assert.True(_vm.HasTrail);

        // Une recherche multicritère, par exemple : ce n'est plus la collection qu'on regarde.
        _vm.Rules = new RuleSearch([new SearchRule("year", RuleOperator.Equals, "1995")]);

        Assert.False(_vm.HasTrail);
        Assert.True(_vm.IsTitlesView);
        Assert.Equal([_alone], _vm.Titles.Select(t => t.Id));
    }

    [Fact]
    public void A_genre_showcase_counts_its_titles()
    {
        var showcases = _titles.GenreShowcases();

        Assert.Equal(2, showcases.Count);
        Assert.Equal([1, 2], showcases.Select(s => s.Titles).Order());
        Assert.All(showcases, s => Assert.Empty(s.PosterRelativePaths));
    }

    private sealed class Offline : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Aucun appel réseau attendu.");
    }
}
