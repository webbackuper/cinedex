using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

/// <summary>
/// Trier par les notes des sites (IMDb, TMDb, Metacritic, Rotten Tomatoes) : les titres sans note vont
/// toujours en fin de liste, quel que soit le sens, et choisir une note trie de la meilleure à la moins bonne.
/// </summary>
public sealed class RatingSortTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TitleRepository _repo;
    private readonly long _alien;
    private readonly long _brazil;
    private readonly long _casino;

    public RatingSortTests()
    {
        var db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        db.Initialize(_temp.Path);
        _repo = new TitleRepository(db);
        _alien = _repo.Add(new NewTitle(TitleKind.Movie, "Alien", 1979));
        _brazil = _repo.Add(new NewTitle(TitleKind.Movie, "Brazil", 1985));
        _casino = _repo.Add(new NewTitle(TitleKind.Movie, "Casino", 1995));
        Rate(_alien, imdb: 8.5, tmdb: 7.0, metascore: 89, tomatometer: 98);
        Rate(_brazil, imdb: 7.9, tmdb: null, metascore: null, tomatometer: 98);
        Rate(_casino, imdb: null, tmdb: 8.0, metascore: 73, tomatometer: 80);
    }

    public void Dispose() => _temp.Dispose();

    private void Rate(long id, double? imdb, double? tmdb, int? metascore, int? tomatometer) =>
        _repo.Update(_repo.Get(id)! with { ImdbRating = imdb, TmdbRating = tmdb, Metascore = metascore, Tomatometer = tomatometer });

    private List<long> Sorted(SortField sort, bool descending) =>
        _repo.Query(new TitleQuery(Sort: sort, Descending: descending)).Select(t => t.Id).ToList();

    [Fact]
    public void Each_site_rating_sorts_with_unrated_titles_last_in_both_directions()
    {
        Assert.Equal([_alien, _brazil, _casino], Sorted(SortField.ImdbRating, descending: true));
        Assert.Equal([_brazil, _alien, _casino], Sorted(SortField.ImdbRating, descending: false));

        Assert.Equal([_casino, _alien, _brazil], Sorted(SortField.TmdbRating, descending: true));
        Assert.Equal([_alien, _casino, _brazil], Sorted(SortField.TmdbRating, descending: false));

        Assert.Equal([_alien, _casino, _brazil], Sorted(SortField.Metascore, descending: true));
        Assert.Equal([_casino, _alien, _brazil], Sorted(SortField.Metascore, descending: false));

        // Égalité (98 et 98) : départagée par le titre.
        Assert.Equal([_alien, _brazil, _casino], Sorted(SortField.Tomatometer, descending: true));
    }

    [Fact]
    public void Choosing_a_rating_in_the_window_sorts_best_first_and_keeps_order_after_an_edit()
    {
        var vm = new MainWindowViewModel(_repo, new AppPaths(_temp.Path, isPortable: false), new MainWindowViewModelTests.FakeDialogs());
        vm.Load();
        Assert.Contains(vm.Sorts, s => s.Value == SortField.ImdbRating);
        Assert.Contains(vm.Sorts, s => s.Value == SortField.TmdbRating);

        vm.SelectedSort = vm.Sorts.Single(s => s.Value == SortField.ImdbRating);

        Assert.True(vm.SortDescending);
        Assert.Equal([_alien, _brazil, _casino], vm.Titles.Select(t => t.Id));

        // Une note corrigée replace le titre à son rang (relecture ciblée, puis complète si le rang change).
        vm.SelectedTitles.Add(vm.Titles.Single(t => t.Id == _brazil));
        Rate(_brazil, imdb: 9.0, tmdb: null, metascore: null, tomatometer: 98);
        vm.ToggleFavoriteCommand.Execute(null);
        Assert.Equal([_brazil, _alien, _casino], vm.Titles.Select(t => t.Id));

        vm.SelectedSort = vm.Sorts.Single(s => s.Value == SortField.TmdbRating);
        Assert.Equal([_casino, _alien, _brazil], vm.Titles.Select(t => t.Id));
    }
}
