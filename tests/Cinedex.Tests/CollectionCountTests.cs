using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Metadata.Tmdb;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cinedex.Tests;

/// <summary>
/// Le compte affiché sous la case « Seulement les collections incomplètes ». Placé juste sous elle, il doit
/// dire ce que la liste montre : un total figé contredisait ce qu'on avait sous les yeux.
/// </summary>
public sealed class CollectionCountTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _titles;
    private readonly CollectionRepository _collections;
    private readonly CollectionService _service;

    public CollectionCountTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _titles = new TitleRepository(_db);
        _collections = new CollectionRepository(_db);
        _service = new CollectionService(
            _collections,
            new TmdbClient(new HttpClient(new Offline()), _ => ValueTask.FromResult<string?>("jeton")),
            new ImageStore(_db, Path.Combine(_temp.Path, "img")),
            NullLogger<CollectionService>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    /// <summary>Une saga dont il manque deux films, et un coffret fait à la main, donc complet.</summary>
    private void TwoCollectionsOneIncomplete()
    {
        // SaveDetails met à jour une collection existante : elle est donc créée d'abord.
        var saga = _titles.CreateCollection("Retour vers le futur - Saga");
        _collections.SaveDetails(saga, new Cinedex.Core.Metadata.CollectionRecord(
            264, "Retour vers le futur - Saga", "Une trilogie.", null, null,
            [new Cinedex.Core.Metadata.CollectionPart(105, "Retour vers le futur", 1985, null),
             new Cinedex.Core.Metadata.CollectionPart(165, "Retour vers le futur 2", 1989, null),
             new Cinedex.Core.Metadata.CollectionPart(196, "Retour vers le futur 3", 1990, null)]));
        var boxed = _titles.CreateCollection("Mon coffret");

        var first = _titles.Add(new NewTitle(TitleKind.Movie, "Retour vers le futur", 1985));
        var second = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        _titles.ApplyBulk([first], new BulkEdit { CollectionId = saga });
        _titles.ApplyBulk([second], new BulkEdit { CollectionId = boxed });
    }

    [Fact]
    public void The_count_follows_the_incomplete_checkbox()
    {
        TwoCollectionsOneIncomplete();
        var vm = new CollectionsViewModel(_service);

        Assert.Equal(2, vm.Collections.Count);
        Assert.Equal("2 collection(s)", vm.CountText);

        vm.OnlyIncomplete = true;

        // La liste filtre, donc le compte aussi, et le total reste rappelé.
        Assert.Single(vm.Collections);
        Assert.Equal("1 collection(s) sur 2", vm.CountText);

        vm.OnlyIncomplete = false;
        Assert.Equal("2 collection(s)", vm.CountText);
    }

    [Fact]
    public void The_count_follows_the_search_box_too()
    {
        TwoCollectionsOneIncomplete();
        var vm = new CollectionsViewModel(_service);

        vm.Query = "coffret";

        Assert.Single(vm.Collections);
        Assert.Equal("1 collection(s) sur 2", vm.CountText);

        // Une recherche sans résultat le dit, au lieu d'annoncer deux collections devant une liste vide.
        vm.Query = "introuvable";
        Assert.Empty(vm.Collections);
        Assert.Equal("0 collection(s) sur 2", vm.CountText);
    }

    [Fact]
    public void The_count_is_notified_so_the_window_redraws_it()
    {
        TwoCollectionsOneIncomplete();
        var vm = new CollectionsViewModel(_service);
        var announced = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CollectionsViewModel.CountText))
            {
                announced++;
            }
        };

        vm.OnlyIncomplete = true;

        // Sans cet avis, la fenêtre garde le texte d'avant : c'était le défaut.
        Assert.True(announced > 0);
    }

    private sealed class Offline : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Aucun appel réseau attendu dans ce test.");
    }
}
