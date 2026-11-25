using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

/// <summary>
/// La fusion des doublons n'a qu'une implémentation, et elle sauvegarde avant d'agir. Les trois écrans qui
/// fusionnent passent par elle : ces tests gardent la sauvegarde unique, et faite avant.
/// </summary>
public sealed class DuplicateMergerTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _titles;
    private readonly MaintenanceRepository _maintenance;
    private readonly string _backups;

    public DuplicateMergerTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _titles = new TitleRepository(_db);
        _maintenance = new MaintenanceRepository(_db);
        _backups = Path.Combine(_temp.Path, "sauvegardes");
    }

    public void Dispose() => _temp.Dispose();

    private (long First, long Second) TwoDuplicates()
    {
        var first = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var second = _titles.Add(new NewTitle(TitleKind.Movie, "Heat - version longue", 1995));
        _titles.Update(_titles.Get(first)! with { TmdbId = 949, TmdbKind = TitleKind.Movie });
        _titles.Update(_titles.Get(second)! with { TmdbId = 949, TmdbKind = TitleKind.Movie });
        return (first, second);
    }

    private string[] Backups() => Directory.Exists(_backups) ? Directory.GetFiles(_backups, "*.db") : [];

    [Fact]
    public void Every_kind_of_merge_backs_up_before_it_acts()
    {
        var merger = new DuplicateMerger(_maintenance, _backups);

        var (first, _) = TwoDuplicates();
        Assert.Equal(1, merger.MergeGroupOf(first).Merged);
        Assert.Single(Backups());

        TwoDuplicatesNamed("Alien", 348);
        Assert.Equal(1, merger.MergeAll().Merged);
        Assert.Equal(2, Backups().Length);

        var a = _titles.Add(new NewTitle(TitleKind.Movie, "Casino", 1995));
        var b = _titles.Add(new NewTitle(TitleKind.Movie, "Casino (autre copie)", 1995));
        Assert.Equal(1, merger.MergeChosen([a, b]).Merged);
        Assert.Equal(3, Backups().Length);

        Assert.All(Backups(), path => Assert.Contains("avant-fusion", Path.GetFileName(path), StringComparison.Ordinal));
    }

    [Fact]
    public void The_settings_tab_sends_to_the_main_window_instead_of_merging_itself()
    {
        TwoDuplicates();
        var vm = new MaintenanceViewModel(_maintenance, new CatalogManager(new AppPaths(_temp.Path, isPortable: true)), "tests", _backups, Path.Combine(_temp.Path, "images"));
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        vm.ShowDuplicatesCommand.Execute(null);

        // Une seule interface de gestion : les réglages se ferment, rien n'est fusionné ici.
        Assert.True(closed);
        Assert.True(vm.PendingShowDuplicates);
        Assert.Equal(2, _titles.ListAll().Count);
        Assert.Empty(Backups());
    }
    [Fact]
    public void The_result_is_told_in_one_sentence()
    {
        Assert.Equal("Aucune fiche en double à fusionner.", DuplicateMerger.Describe(new TitleMerge(0, 0, 0)));
        var told = DuplicateMerger.Describe(new TitleMerge(2, 5, 1));
        Assert.Contains("2 fiche(s) fusionnée(s)", told, StringComparison.Ordinal);
        Assert.Contains("5 fichier(s)", told, StringComparison.Ordinal);
    }

    private void TwoDuplicatesNamed(string title, long tmdbId)
    {
        var first = _titles.Add(new NewTitle(TitleKind.Movie, title, 1979));
        var second = _titles.Add(new NewTitle(TitleKind.Movie, title + " bis", 1979));
        _titles.Update(_titles.Get(first)! with { TmdbId = tmdbId, TmdbKind = TitleKind.Movie });
        _titles.Update(_titles.Get(second)! with { TmdbId = tmdbId, TmdbKind = TitleKind.Movie });
    }
}
