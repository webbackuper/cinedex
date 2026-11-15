using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

/// <summary>
/// Les doublons depuis le bandeau gauche : on vient les consulter après un import, et on tranche groupe par
/// groupe sans détour par les réglages. Choisir un groupe n'affiche que ses fiches, pour les comparer.
/// </summary>
public sealed class DuplicateBrowsingTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TitleRepository _repo;
    private readonly MainWindowViewModelTests.FakeDialogs _dialogs = new();
    private readonly MainWindowViewModel _vm;

    public DuplicateBrowsingTests()
    {
        var db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        db.Initialize(_temp.Path);
        _repo = new TitleRepository(db);
        _vm = new MainWindowViewModel(
            _repo,
            new AppPaths(_temp.Path, isPortable: false),
            _dialogs,
            extras: new CatalogExtras(Maintenance: new MaintenanceRepository(db)));
    }

    public void Dispose() => _temp.Dispose();

    /// <summary>Deux fiches du même numéro TMDb, plus un film sans rapport pour vérifier qu'il reste dehors.</summary>
    private (long First, long Second, long Other) TwoDuplicatesAndOneOther()
    {
        var first = _repo.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var second = _repo.Add(new NewTitle(TitleKind.Movie, "Heat - version longue", 1995));
        var other = _repo.Add(new NewTitle(TitleKind.Movie, "Alien", 1979));
        _repo.Update(_repo.Get(first)! with { TmdbId = 949, TmdbKind = TitleKind.Movie });
        _repo.Update(_repo.Get(second)! with { TmdbId = 949, TmdbKind = TitleKind.Movie });
        _repo.Update(_repo.Get(other)! with { TmdbId = 348, TmdbKind = TitleKind.Movie });
        return (first, second, other);
    }

    [Fact]
    public async Task Opening_the_menu_looks_for_duplicates_and_naming_is_plain()
    {
        var (first, second, _) = TwoDuplicatesAndOneOther();
        _vm.Load();

        // Avant toute recherche, on ne prétend pas qu'il n'y a aucun doublon.
        Assert.Empty(_vm.Duplicates!.Groups);
        Assert.Contains("Cherchez", _vm.Duplicates!.StatusText, StringComparison.Ordinal);

        await _vm.Duplicates!.ToggleCommand.ExecuteAsync(null);

        Assert.True(_vm.Duplicates!.IsExpanded);
        var group = Assert.Single(_vm.Duplicates!.Groups);
        Assert.Equal([first, second], group.TitleIds.Order());
        Assert.Contains("Heat (1995)", group.Label, StringComparison.Ordinal);
        Assert.Contains("2 fiches", group.Label, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(group.Reason));
    }

    [Fact]
    public async Task Choosing_a_group_shows_only_its_records()
    {
        var (first, second, other) = TwoDuplicatesAndOneOther();
        _vm.Load();
        Assert.Equal(3, _vm.Titles.Count);

        await _vm.Duplicates!.ToggleCommand.ExecuteAsync(null);
        _vm.Duplicates!.Selected = _vm.Duplicates!.Groups[0];

        Assert.Equal([first, second], _vm.Titles.Select(t => t.Id).Order());
        Assert.DoesNotContain(other, _vm.Titles.Select(t => t.Id));
        Assert.True(_vm.Duplicates!.HasSelected);

        // Revenir à une vue du bandeau relâche le groupe : la liste n'en reste pas filtrée sans qu'on le voie.
        // « Films seulement » les garde tous les trois, donc seul le relâchement du groupe explique le compte.
        _vm.SelectedFilter = MainWindowViewModel.AllMainFilters.First(f => f.Value == QuickFilter.Movies);
        Assert.Null(_vm.Duplicates!.Selected);
        Assert.Equal(3, _vm.Titles.Count);
    }

    [Fact]
    public async Task Merging_the_shown_group_leaves_the_others_alone()
    {
        var first = _repo.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var second = _repo.Add(new NewTitle(TitleKind.Movie, "Heat - version longue", 1995));
        var thirdA = _repo.Add(new NewTitle(TitleKind.Movie, "Alien", 1979));
        var thirdB = _repo.Add(new NewTitle(TitleKind.Movie, "Alien - director's cut", 1979));
        _repo.Update(_repo.Get(first)! with { TmdbId = 949, TmdbKind = TitleKind.Movie });
        _repo.Update(_repo.Get(second)! with { TmdbId = 949, TmdbKind = TitleKind.Movie });
        _repo.Update(_repo.Get(thirdA)! with { TmdbId = 348, TmdbKind = TitleKind.Movie });
        _repo.Update(_repo.Get(thirdB)! with { TmdbId = 348, TmdbKind = TitleKind.Movie });

        _vm.Load();
        await _vm.Duplicates!.ToggleCommand.ExecuteAsync(null);
        Assert.Equal(2, _vm.Duplicates!.Groups.Count);

        // Sans confirmation, rien ne bouge : la fusion ne se défait pas.
        _dialogs.Answer = false;
        _vm.Duplicates!.Selected = _vm.Duplicates!.Groups.Single(g => g.TitleIds.Contains(first));
        await _vm.Duplicates!.MergeSelectedCommand.ExecuteAsync(null);
        Assert.Equal(4, _repo.ListAll().Count);

        _dialogs.Answer = true;
        await _vm.Duplicates!.MergeSelectedCommand.ExecuteAsync(null);

        // Le groupe de Heat est réuni, celui d'Alien est intact et reste à trancher.
        Assert.Equal(3, _repo.ListAll().Count);
        var left = Assert.Single(_vm.Duplicates!.Groups);
        Assert.Equal([thirdA, thirdB], left.TitleIds.Order());
        Assert.Null(_vm.Duplicates!.Selected);
    }

    /// <summary>Sauvegardes faites avant une fusion, reconnues à leur nom.</summary>
    private string[] BackupsBeforeMerge() =>
        Directory.Exists(new AppPaths(_temp.Path, isPortable: false).BackupsDirectory)
            ? Directory.GetFiles(new AppPaths(_temp.Path, isPortable: false).BackupsDirectory, "*avant-fusion*")
            : [];

    /// <summary>
    /// Une fusion ne se défait pas : la sauvegarde que la question annonce doit exister. Le bandeau et le clic
    /// droit l'annonçaient sans la faire, tant que chacun fusionnait de son côté.
    /// </summary>
    [Fact]
    public async Task Merging_a_group_from_the_banner_backs_the_database_up_first()
    {
        TwoDuplicatesAndOneOther();
        _vm.Load();
        await _vm.Duplicates!.ToggleCommand.ExecuteAsync(null);
        _vm.Duplicates!.Selected = _vm.Duplicates!.Groups[0];

        // Refusée, la fusion ne laisse rien : ni fiche retirée, ni sauvegarde inutile.
        _dialogs.Answer = false;
        await _vm.Duplicates!.MergeSelectedCommand.ExecuteAsync(null);
        Assert.Empty(BackupsBeforeMerge());

        _dialogs.Answer = true;
        await _vm.Duplicates!.MergeSelectedCommand.ExecuteAsync(null);

        var backup = Assert.Single(BackupsBeforeMerge());
        // La sauvegarde est celle d'avant : elle contient encore les trois fiches.
        Assert.Equal(3, new TitleRepository(new CatalogDatabase(backup)).ListAll().Count);
        Assert.Equal(2, _repo.ListAll().Count);
    }

    [Fact]
    public async Task Merging_a_selection_by_right_click_backs_the_database_up_first()
    {
        var (first, second, _) = TwoDuplicatesAndOneOther();
        _vm.Load();
        foreach (var title in _vm.Titles.Where(t => t.Id == first || t.Id == second))
        {
            _vm.SelectedTitles.Add(title);
        }

        _dialogs.Answer = true;
        await _vm.MergeSelectionCommand.ExecuteAsync(null);

        Assert.Single(BackupsBeforeMerge());
        Assert.Equal(2, _repo.ListAll().Count);
        Assert.Contains("fusionnée", _vm.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Merging_every_group_at_once_clears_the_list()
    {
        TwoDuplicatesAndOneOther();
        _vm.Load();
        await _vm.Duplicates!.ToggleCommand.ExecuteAsync(null);
        _dialogs.Answer = true;

        await _vm.Duplicates!.MergeAllCommand.ExecuteAsync(null);

        Assert.Empty(_vm.Duplicates!.Groups);
        Assert.Equal("Aucun film en double au catalogue.", _vm.Duplicates!.StatusText);
        Assert.Equal(2, _repo.ListAll().Count);
        Assert.Single(BackupsBeforeMerge());
        // La fenêtre dit ce que la fusion a fait, et sa liste est à jour.
        Assert.Contains("fusionnée", _vm.Status, StringComparison.Ordinal);
        Assert.Equal(2, _vm.Titles.Count);
    }
}
