using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

/// <summary>
/// Fusion d'une sélection faite à la main. Rien ne vérifie que ces films vont ensemble : c'est l'utilisateur
/// qui l'affirme, d'où l'avertissement. Un refus ne doit évidemment rien changer.
/// </summary>
public sealed class MergeSelectionTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _titles;
    private readonly MediaFileRepository _files;
    private readonly MaintenanceRepository _maintenance;

    public MergeSelectionTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _titles = new TitleRepository(_db);
        _files = new MediaFileRepository(_db);
        _maintenance = new MaintenanceRepository(_db);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Chosen_titles_merge_even_when_nothing_links_them()
    {
        // Deux titres que la recherche de doublons ne rapprocherait jamais : ni identifiant, ni titre commun.
        var one = _titles.Add(new NewTitle(TitleKind.Movie, "Le même film, version longue", 1995));
        var two = _titles.Add(new NewTitle(TitleKind.Movie, "Autre nom du même film", 1995));
        _files.Attach(one, Path.Combine(_temp.Path, "long.mkv"), 900);
        _files.Attach(two, Path.Combine(_temp.Path, "court.mkv"), 700);
        Assert.Empty(_maintenance.FindDuplicateTitles());

        var result = _maintenance.MergeChosenTitles([one, two]);

        Assert.Equal(1, result.Merged);
        var film = Assert.Single(_titles.ListAll());
        Assert.Equal([1, 2], _files.FilesOf(film.Id).Select(f => f.Part).Order());
    }

    [Fact]
    public void One_title_alone_is_not_a_merge()
    {
        var only = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));

        Assert.Equal(0, _maintenance.MergeChosenTitles([only]).Merged);
        Assert.Equal(0, _maintenance.MergeChosenTitles([]).Merged);
        Assert.Single(_titles.ListAll());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_command_warns_before_merging(bool accept)
    {
        var one = _titles.Add(new NewTitle(TitleKind.Movie, "Version longue", 1995));
        var two = _titles.Add(new NewTitle(TitleKind.Movie, "Version courte", 1995));
        _files.Attach(one, Path.Combine(_temp.Path, "long.mkv"), 900);
        _files.Attach(two, Path.Combine(_temp.Path, "court.mkv"), 700);

        var dialogs = new MainWindowViewModelTests.FakeDialogs { Answer = accept };
        var vm = new MainWindowViewModel(
            _titles,
            new AppPaths(_temp.Path, isPortable: false),
            dialogs,
            extras: new CatalogExtras(Maintenance: _maintenance));
        vm.Load();

        // Un seul titre : l'action reste indisponible, il n'y a rien à réunir.
        vm.SelectedTitles.Add(vm.Titles[0]);
        Assert.False(vm.MergeSelectionCommand.CanExecute(null));

        vm.SelectedTitles.Add(vm.Titles[1]);
        Assert.True(vm.MergeSelectionCommand.CanExecute(null));
        await vm.MergeSelectionCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.Confirmations);
        Assert.Equal(accept ? 1 : 2, _titles.ListAll().Count);
    }
}
