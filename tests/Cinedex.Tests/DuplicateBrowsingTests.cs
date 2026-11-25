using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

/// <summary>
/// Les doublons dans la zone centrale : tous les groupes à la fois, chacun avec ses fiches, et l'on tranche
/// groupe par groupe. Le bandeau gauche n'en porte qu'une entrée, et les réglages y renvoient.
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
    public async Task Opening_the_view_looks_for_duplicates_and_shows_every_group_with_its_records()
    {
        var (first, second, other) = TwoDuplicatesAndOneOther();
        _vm.Load();

        // Avant toute recherche, on ne prétend pas qu'il n'y a aucun doublon, et l'entrée ne compte rien.
        Assert.Empty(_vm.Duplicates!.Groups);
        Assert.Contains("Cherchez", _vm.Duplicates!.StatusText, StringComparison.Ordinal);
        Assert.Equal("Doublons", _vm.Duplicates!.EntryLabel);

        await _vm.Duplicates!.OpenCommand.ExecuteAsync(null);

        // La vue occupe la zone centrale, à la place de la liste.
        Assert.True(_vm.IsDuplicatesView);
        Assert.False(_vm.IsTitlesView);
        Assert.Equal("Doublons (1)", _vm.Duplicates!.EntryLabel);
        var group = Assert.Single(_vm.Duplicates!.Groups);
        Assert.Equal([first, second], group.TitleIds.Order());
        Assert.Equal([first, second], group.Titles.Select(t => t.Id).Order());
        Assert.DoesNotContain(other, group.Titles.Select(t => t.Id));
        Assert.Contains("Heat (1995)", group.Label, StringComparison.Ordinal);
        Assert.Contains("2 fiches", group.Label, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(group.Reason));

        // Revenir à une vue de la bibliothèque ferme celle des doublons ; la liste n'a jamais été filtrée.
        _vm.SelectedFilter = MainWindowViewModel.AllMainFilters.First(f => f.Value == QuickFilter.Movies);
        _vm.LeaveSideViews();
        Assert.False(_vm.IsDuplicatesView);
        Assert.True(_vm.IsTitlesView);
        Assert.Equal(3, _vm.Titles.Count);
    }

    [Fact]
    public async Task A_record_opened_from_the_view_steps_through_its_group()
    {
        var (first, second, _) = TwoDuplicatesAndOneOther();
        _vm.Load();
        await _vm.Duplicates!.OpenCommand.ExecuteAsync(null);
        var group = _vm.Duplicates!.Groups[0];
        string? counter = null;
        _dialogs.OnEdit = editor => counter = editor.NavigationText;

        await _vm.OpenDuplicateCommand.ExecuteAsync(group.Titles[0]);

        Assert.Equal(group.Titles[0].Id, _dialogs.Edited!.TitleId);
        Assert.Contains("Doublons › ", counter, StringComparison.Ordinal);
        Assert.StartsWith("1 / 2", counter, StringComparison.Ordinal);
        Assert.Contains(_dialogs.Edited.NextTitleId!.Value, new[] { first, second });
    }

    [Fact]
    public async Task Merging_one_group_leaves_the_others_alone()
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
        await _vm.Duplicates!.OpenCommand.ExecuteAsync(null);
        Assert.Equal(2, _vm.Duplicates!.Groups.Count);
        var heat = _vm.Duplicates!.Groups.Single(g => g.TitleIds.Contains(first));

        // Sans confirmation, rien ne bouge : la fusion ne se défait pas.
        _dialogs.Answer = false;
        await _vm.Duplicates!.MergeGroupCommand.ExecuteAsync(heat);
        Assert.Equal(4, _repo.ListAll().Count);

        _dialogs.Answer = true;
        await _vm.Duplicates!.MergeGroupCommand.ExecuteAsync(heat);

        // Le groupe de Heat est réuni, celui d'Alien est intact et reste à trancher.
        Assert.Equal(3, _repo.ListAll().Count);
        var left = Assert.Single(_vm.Duplicates!.Groups);
        Assert.Equal([thirdA, thirdB], left.TitleIds.Order());
        Assert.Equal("Doublons (1)", _vm.Duplicates!.EntryLabel);
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
        await _vm.Duplicates!.OpenCommand.ExecuteAsync(null);
        var group = _vm.Duplicates!.Groups[0];

        // Refusée, la fusion ne laisse rien : ni fiche retirée, ni sauvegarde inutile.
        _dialogs.Answer = false;
        await _vm.Duplicates!.MergeGroupCommand.ExecuteAsync(group);
        Assert.Empty(BackupsBeforeMerge());

        _dialogs.Answer = true;
        await _vm.Duplicates!.MergeGroupCommand.ExecuteAsync(group);

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
        await _vm.Duplicates!.OpenCommand.ExecuteAsync(null);
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
