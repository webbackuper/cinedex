using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

/// <summary>
/// Enchaîner les fiches : depuis une fiche, passer au titre voisin l'enregistre et ouvre aussitôt la suivante.
/// Et le raccourci d'« Ajouter à une liste », qui reprend la dernière liste utilisée.
/// </summary>
public sealed class EditChainingTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TitleRepository _repo;
    private readonly MainWindowViewModelTests.FakeDialogs _dialogs = new();
    private readonly MainWindowViewModel _vm;
    private readonly long _alien;
    private readonly long _casino;
    private readonly long _heat;

    public EditChainingTests()
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

    [Fact]
    public async Task The_record_knows_its_neighbours_in_the_list()
    {
        _vm.SelectedTitles.Add(_vm.Titles.Single(t => t.Id == _casino));

        await _vm.EditCommand.ExecuteAsync(null);

        var editor = _dialogs.Edited!;
        Assert.Equal(_alien, editor.PreviousTitleId);
        Assert.Equal(_heat, editor.NextTitleId);

        // Au bout de la liste, il n'y a pas de voisin de ce côté.
        _vm.SelectedTitles.Clear();
        _vm.SelectedTitles.Add(_vm.Titles.Single(t => t.Id == _alien));
        await _vm.EditCommand.ExecuteAsync(null);
        Assert.False(_dialogs.Edited!.HasPrevious);
        Assert.True(_dialogs.Edited.HasNext);
    }

    [Fact]
    public async Task Stepping_to_the_next_record_saves_this_one_and_opens_the_next()
    {
        _vm.SelectedTitles.Add(_vm.Titles.Single(t => t.Id == _alien));
        var opened = new List<long>();
        _dialogs.Answer = true;
        _dialogs.OnEdit = editor =>
        {
            opened.Add(editor.TitleId);
            if (opened.Count == 1)
            {
                // Sur la première fiche : une correction, puis « Suivant ».
                editor.IsFavorite = true;
                editor.NavigateToTitleId = editor.NextTitleId;
                editor.ReopenAfterNavigation = true;
            }
        };

        await _vm.EditCommand.ExecuteAsync(null);

        // La seconde fiche s'est ouverte d'elle-même, sur le voisin, et la première a été enregistrée.
        Assert.Equal([_alien, _casino], opened);
        Assert.True(_repo.Get(_alien)!.IsFavorite);
        Assert.Equal([_casino], _vm.SelectedTitles.Select(t => t.Id));
    }

    [Fact]
    public async Task Leaving_for_another_title_without_reopening_just_goes_there()
    {
        _vm.SelectedTitles.Add(_vm.Titles.Single(t => t.Id == _alien));
        var opened = 0;
        _dialogs.Answer = false;
        _dialogs.OnEdit = editor =>
        {
            opened++;
            editor.NavigateToTitleId = _heat;
        };

        await _vm.EditCommand.ExecuteAsync(null);

        Assert.Equal(1, opened);
        Assert.Equal([_heat], _vm.SelectedTitles.Select(t => t.Id));
    }

    [Fact]
    public void The_list_shortcut_reuses_the_last_list()
    {
        _repo.ApplyBulk([_alien], new BulkEdit { AddGroups = ["Soirée", "Noël"] });
        _vm.ReloadCommand.Execute(null);
        _vm.SelectedTitles.Add(_vm.Titles.Single(t => t.Id == _casino));

        // Deux listes et aucune encore utilisée : le raccourci ne devine pas.
        _vm.AddToLastListCommand.Execute(null);
        Assert.Empty(_repo.Get(_casino)!.Groups);
        Assert.Contains("clic droit", _vm.Status, StringComparison.Ordinal);

        _vm.AddToListCommand.Execute("Noël");
        Assert.Equal(["Noël"], _repo.Get(_casino)!.Groups);

        // La même liste, d'une touche, pour le film suivant.
        _vm.SelectedTitles.Clear();
        _vm.SelectedTitles.Add(_vm.Titles.Single(t => t.Id == _heat));
        _vm.AddToLastListCommand.Execute(null);
        Assert.Equal(["Noël"], _repo.Get(_heat)!.Groups);
    }
}
