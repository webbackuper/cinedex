using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

/// <summary>
/// Après une action sur quelques fiches, seules leurs lignes sont relues. Le gain n'a de valeur que si la
/// liste reste juste : une ligne dont le rang change, ou qui sort du filtre, doit finir au bon endroit.
/// </summary>
public sealed class TargetedRefreshTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TitleRepository _repo;
    private readonly MainWindowViewModelTests.FakeDialogs _dialogs = new();
    private readonly MainWindowViewModel _vm;
    private readonly long _alien;
    private readonly long _casino;
    private readonly long _heat;

    public TargetedRefreshTests()
    {
        var db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        db.Initialize(_temp.Path);
        _repo = new TitleRepository(db);
        _alien = _repo.Add(new NewTitle(TitleKind.Movie, "Alien", 1979));
        _casino = _repo.Add(new NewTitle(TitleKind.Movie, "Casino", 1995));
        _heat = _repo.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        _vm = new MainWindowViewModel(_repo, new AppPaths(_temp.Path, isPortable: false), _dialogs);
        _vm.Load();
    }

    public void Dispose() => _temp.Dispose();

    private void Select(long id)
    {
        _vm.SelectedTitles.Clear();
        _vm.SelectedTitles.Add(_vm.Titles.Single(t => t.Id == id));
    }

    [Fact]
    public async Task A_renamed_title_ends_at_its_new_rank()
    {
        Assert.Equal(["Alien", "Casino", "Heat"], _vm.Titles.Select(t => t.Title));
        Select(_alien);
        _dialogs.Answer = true;
        _dialogs.OnEdit = editor => editor.Title = "Zodiac";

        await _vm.EditCommand.ExecuteAsync(null);

        // Le titre renommé change de rang : la relecture ciblée le voit et laisse la main à la complète.
        Assert.Equal(["Casino", "Heat", "Zodiac"], _vm.Titles.Select(t => t.Title));
        Assert.Equal([_alien], _vm.SelectedTitles.Select(t => t.Id));
    }

    [Fact]
    public async Task An_edit_that_keeps_the_rank_updates_the_line_in_place()
    {
        Select(_casino);
        _dialogs.Answer = true;
        _dialogs.OnEdit = editor => editor.IsFavorite = true;

        await _vm.EditCommand.ExecuteAsync(null);

        Assert.Equal(["Alien", "Casino", "Heat"], _vm.Titles.Select(t => t.Title));
        Assert.True(_vm.Titles.Single(t => t.Id == _casino).IsFavorite);
        Assert.Equal([_casino], _vm.SelectedTitles.Select(t => t.Id));
        Assert.True(_vm.Record.Detail!.IsFavorite);
    }

    [Fact]
    public async Task A_title_that_leaves_the_filter_leaves_the_list()
    {
        _repo.SetFavorite([_casino], true);
        _vm.SelectedFilter = MainWindowViewModel.AllMainFilters.First(f => f.Value == QuickFilter.Favorites);
        Assert.Equal(["Casino"], _vm.Titles.Select(t => t.Title));

        Select(_casino);
        _dialogs.Answer = true;
        _dialogs.OnEdit = editor => editor.IsFavorite = false;
        await _vm.EditCommand.ExecuteAsync(null);

        Assert.Empty(_vm.Titles);
        Assert.Empty(_vm.SelectedTitles);
    }

    [Fact]
    public void Going_to_a_title_already_shown_keeps_the_view()
    {
        _vm.SelectedFilter = MainWindowViewModel.AllMainFilters.First(f => f.Value == QuickFilter.Movies);

        _vm.GoToTitle(_heat);

        // Rien n'est remis à zéro : le titre était sous les yeux, il est simplement sélectionné.
        Assert.Equal(QuickFilter.Movies, _vm.SelectedFilter.Value);
        Assert.Equal([_heat], _vm.SelectedTitles.Select(t => t.Id));
        Assert.Equal(_heat, _vm.Record.Detail!.Id);
    }

    [Fact]
    public void Going_to_a_hidden_title_lifts_the_filter()
    {
        _repo.SetFavorite([_casino], true);
        _vm.SelectedFilter = MainWindowViewModel.AllMainFilters.First(f => f.Value == QuickFilter.Favorites);

        _vm.GoToTitle(_heat);

        Assert.Equal(QuickFilter.All, _vm.SelectedFilter.Value);
        Assert.Equal(3, _vm.Titles.Count);
        Assert.Equal([_heat], _vm.SelectedTitles.Select(t => t.Id));
    }

    [Fact]
    public void A_random_pick_comes_from_the_unseen_titles_on_screen()
    {
        _repo.AddViewing(_alien, DateTimeOffset.Now);
        _repo.AddViewing(_casino, DateTimeOffset.Now);
        _vm.ReloadCommand.Execute(null);

        _vm.PickRandomCommand.Execute(null);

        // Un seul titre non vu : c'est forcément lui.
        Assert.Equal([_heat], _vm.SelectedTitles.Select(t => t.Id));

        _repo.AddViewing(_heat, DateTimeOffset.Now);
        _vm.ReloadCommand.Execute(null);
        _vm.PickRandomCommand.Execute(null);
        Assert.Contains("Aucun titre non vu", _vm.Status, StringComparison.Ordinal);
    }
}
