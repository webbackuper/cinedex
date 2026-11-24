using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

/// <summary>
/// Enchaîner les fiches : Précédent / Suivant parcourent la vue d'où vient la fiche (liste principale, bande
/// de l'Accueil, collection…), figée à l'ouverture, dans la même fenêtre. La fiche quittée est enregistrée
/// si elle a changé ; une saisie invalide retient. Et le raccourci d'« Ajouter à une liste ».
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

    /// <summary>La fiche telle que la fenêtre la montre à la fermeture : la dernière de la chaîne.</summary>
    private static TitleEditorViewModel Last(TitleEditorViewModel editor)
    {
        while (editor.Successor is { } next)
        {
            editor = next;
        }

        return editor;
    }

    [Fact]
    public void The_sequence_stops_at_both_ends_skips_deleted_titles_and_counts()
    {
        var navigation = TitleNavigation.For("Accueil › Derniers ajouts", [10, 20, 30, 40], 20);

        Assert.Equal(20, navigation.Current);
        Assert.Equal("2 / 4 · Accueil › Derniers ajouts", navigation.CounterText);
        Assert.Equal(10, navigation.Step(-1)!.Current);
        Assert.Null(navigation.Step(-1)!.Step(-1));

        // 30 a été supprimé depuis l'ouverture : Suivant passe directement à 40, puis s'arrête (pas de bouclage).
        var next = navigation.Step(1, id => id != 30)!;
        Assert.Equal(40, next.Current);
        Assert.Equal("4 / 4 · Accueil › Derniers ajouts", next.CounterText);
        Assert.Null(next.Step(1));

        // Un titre absent de la vue n'a pas de voisin : on n'en invente pas.
        var alone = TitleNavigation.For("Liste", [1, 2], 99);
        Assert.Null(alone.Step(1));
        Assert.Null(alone.Step(-1));
    }

    [Fact]
    public async Task The_record_knows_its_neighbours_in_the_main_list()
    {
        _vm.SelectedTitles.Add(_vm.Titles.Single(t => t.Id == _casino));

        await _vm.EditCommand.ExecuteAsync(null);

        var editor = _dialogs.Edited!;
        Assert.Equal(_alien, editor.PreviousTitleId);
        Assert.Equal(_heat, editor.NextTitleId);
        Assert.Equal("2 / 3 · Tous les titres", editor.NavigationText);

        // Au bout de la liste, il n'y a pas de voisin de ce côté.
        _vm.SelectedTitles.Clear();
        _vm.SelectedTitles.Add(_vm.Titles.Single(t => t.Id == _alien));
        await _vm.EditCommand.ExecuteAsync(null);
        Assert.False(_dialogs.Edited!.HasPrevious);
        Assert.True(_dialogs.Edited.HasNext);
    }

    [Fact]
    public async Task Stepping_saves_this_record_and_replaces_it_in_the_same_window()
    {
        _vm.SelectedTitles.Add(_vm.Titles.Single(t => t.Id == _alien));
        _dialogs.Answer = true;
        TitleEditorViewModel? second = null;
        _dialogs.OnEdit = editor =>
        {
            // Sur la première fiche : une correction, puis « Suivant ». La fenêtre reste ouverte.
            editor.IsFavorite = true;
            second = editor.Step(1);
        };

        await _vm.EditCommand.ExecuteAsync(null);

        Assert.NotNull(second);
        Assert.Same(second, _dialogs.Edited!.Successor);
        Assert.Equal(_casino, second!.TitleId);
        Assert.True(_repo.Get(_alien)!.IsFavorite);

        // La sélection derrière suit le film, puisqu'il est affiché dans la liste.
        Assert.Equal([_casino], _vm.SelectedTitles.Select(t => t.Id));
    }

    [Fact]
    public async Task An_invalid_entry_keeps_the_record_open_on_the_same_title()
    {
        _vm.SelectedTitles.Add(_vm.Titles.Single(t => t.Id == _alien));
        TitleEditorViewModel? next = null;
        _dialogs.OnEdit = editor =>
        {
            editor.Year = "pas une année";
            next = editor.Step(1);
        };

        await _vm.EditCommand.ExecuteAsync(null);

        Assert.Null(next);
        Assert.Null(_dialogs.Edited!.Successor);
        Assert.NotNull(_dialogs.Edited.LastError);
        Assert.Equal(1979, _repo.Get(_alien)!.Year);
    }

    [Fact]
    public async Task In_the_unseen_view_marking_seen_then_next_continues_and_the_filter_stays()
    {
        _vm.SelectedFilter = _vm.Filters.Single(f => f.Value == QuickFilter.Unseen);
        _vm.SearchText = string.Empty;
        _vm.SelectedTitles.Add(_vm.Titles.Single(t => t.Id == _alien));
        _dialogs.Answer = true;
        var visited = new List<long>();
        _dialogs.OnEdit = editor =>
        {
            Assert.EndsWith("Pas encore vus", editor.NavigationText, StringComparison.Ordinal);
            var current = editor;
            visited.Add(current.TitleId);
            current.IsSeen = true;
            while (current.Step(1) is { } next)
            {
                current = next;
                visited.Add(current.TitleId);
                current.IsSeen = true;
            }
        };

        await _vm.EditCommand.ExecuteAsync(null);

        // Chaque film marqué vu sort de la liste « Pas encore vus », mais la séquence figée continue.
        Assert.Equal([_alien, _casino, _heat], visited);
        Assert.All([_alien, _casino, _heat], id => Assert.True(_repo.Get(id)!.IsSeen));
        Assert.Equal(QuickFilter.Unseen, _vm.SelectedFilter.Value);
        Assert.Empty(_vm.Titles);
        Assert.Equal(_heat, Last(_dialogs.Edited!).TitleId);
    }

    [Fact]
    public async Task Next_from_a_home_band_stays_in_the_band()
    {
        var db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        var vm = new MainWindowViewModel(_repo, new AppPaths(_temp.Path, isPortable: false), _dialogs,
            extras: new Cinedex.App.Services.CatalogExtras(Home: new HomeRepository(db)));
        vm.Load();
        vm.ShowHomeCommand.Execute(null);
        var band = vm.Home!.Bands.Single(b => b.Title == "Derniers ajouts");

        // Les derniers ajouts d'abord : Heat, Casino, Alien. L'ordre alphabétique de la liste est l'inverse.
        Assert.Equal([_heat, _casino, _alien], band.Titles.Select(t => t.Id));
        var visited = new List<long>();
        _dialogs.OnEdit = editor =>
        {
            visited.Add(editor.TitleId);
            Assert.Equal("2 / 3 · Accueil › Derniers ajouts", editor.NavigationText);
            if (editor.Step(1) is { } next)
            {
                visited.Add(next.TitleId);
                Assert.Null(next.NextTitleId);
            }
        };

        await vm.OpenFromHomeCommand.ExecuteAsync(band.Titles[1]);

        // Casino puis Alien, l'ordre de la bande ; pas Heat, le voisin alphabétique.
        Assert.Equal([_casino, _alien], visited);
    }

    [Fact]
    public async Task A_film_chosen_from_the_record_starts_a_new_sequence_from_its_list()
    {
        _vm.SelectedTitles.Add(_vm.Titles.Single(t => t.Id == _alien));
        TitleEditorViewModel? moved = null;
        _dialogs.OnEdit = editor =>
            moved = editor.MoveTo(TitleEditorViewModel.ContextOf("Al Pacino", [_heat, _casino], _heat), save: false);

        await _vm.EditCommand.ExecuteAsync(null);

        Assert.Equal(_heat, moved!.TitleId);
        Assert.Equal("1 / 2 · Al Pacino", moved.NavigationText);
        Assert.Equal(_casino, moved.NextTitleId);
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
