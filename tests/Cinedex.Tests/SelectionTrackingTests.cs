using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

/// <summary>
/// La sélection suivie élément par élément. Elle était reconstruite en entier à chaque titre ajouté, ce qui
/// rendait « tout sélectionner » quadratique : ces tests gardent le résultat juste, quel que soit le chemin.
/// </summary>
public sealed class SelectionTrackingTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TitleRepository _repo;
    private readonly MainWindowViewModel _vm;

    public SelectionTrackingTests()
    {
        var db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        db.Initialize(_temp.Path);
        _repo = new TitleRepository(db);
        for (var i = 1; i <= 40; i++)
        {
            _repo.Add(new NewTitle(TitleKind.Movie, $"Film {i:00}", 1980 + i));
        }

        _vm = new MainWindowViewModel(_repo, new AppPaths(_temp.Path, isPortable: false), new MainWindowViewModelTests.FakeDialogs());
        _vm.Load();
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Adding_and_removing_one_by_one_keeps_the_set_exact()
    {
        foreach (var title in _vm.Titles)
        {
            _vm.SelectedTitles.Add(title);
        }

        Assert.Equal(40, _vm.SelectedIdSet.Count);
        Assert.All(_vm.Titles, t => Assert.True(_vm.IsSelected(t.Id)));

        var removed = _vm.Titles[5];
        _vm.SelectedTitles.Remove(removed);

        Assert.False(_vm.IsSelected(removed.Id));
        Assert.DoesNotContain(removed.Id, _vm.SelectedIdSet);
        Assert.Equal(39, _vm.SelectedIdSet.Count);

        _vm.SelectedTitles.Clear();

        Assert.Empty(_vm.SelectedIdSet);
        Assert.False(_vm.IsSelected(_vm.Titles[0].Id));
        Assert.False(_vm.HasDetail);
    }

    [Fact]
    public void A_single_selection_still_shows_its_record_at_once()
    {
        _vm.SelectedTitles.Add(_vm.Titles[3]);

        Assert.True(_vm.HasDetail);
        Assert.Equal(_vm.Titles[3].Id, _vm.Detail!.Id);
        Assert.Equal([_vm.Titles[3].Id], _vm.SelectedIdSet);
    }

    [Fact]
    public void A_range_in_the_poster_grid_publishes_the_whole_range()
    {
        _vm.SelectInGrid(2, toggle: false, extend: false);
        _vm.SelectInGrid(11, toggle: false, extend: true);

        Assert.Equal(10, _vm.SelectedTitles.Count);
        Assert.Equal(_vm.Titles.Skip(2).Take(10).Select(t => t.Id).Order(), _vm.SelectedIdSet.Order());
        Assert.True(_vm.HasMultiSelection);
    }

    [Fact]
    public void A_refresh_keeps_the_selection_it_was_asked_to_keep()
    {
        var kept = _vm.Titles.Skip(10).Take(15).ToList();
        foreach (var title in kept)
        {
            _vm.SelectedTitles.Add(title);
        }

        _vm.ReloadCommand.Execute(null);

        Assert.Equal(kept.Select(t => t.Id).Order(), _vm.SelectedTitles.Select(t => t.Id).Order());
        Assert.Equal(kept.Select(t => t.Id).Order(), _vm.SelectedIdSet.Order());
    }
}
