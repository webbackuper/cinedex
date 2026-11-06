using Cinedex.App.ViewModels;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

/// <summary>
/// Historique de visionnage. L'exigence : revoir un film ajoute un événement daté de plus, et l'historique
/// se corrige ligne par ligne — là où le drapeau « vu » ne dit que oui ou non.
/// </summary>
public sealed class ViewingHistoryTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _repo;

    public ViewingHistoryTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _repo = new TitleRepository(_db);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Watching_again_adds_an_event_instead_of_replacing_the_first()
    {
        var id = _repo.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));

        _repo.AddViewing(id, new DateTimeOffset(2026, 1, 10, 20, 0, 0, TimeSpan.Zero));
        _repo.AddViewing(id, new DateTimeOffset(2026, 3, 2, 21, 0, 0, TimeSpan.Zero), ViewingSource.Playback);

        var history = _repo.Viewings(id);
        Assert.Equal(2, history.Count);
        // Le plus récent d'abord.
        Assert.Equal(new DateTimeOffset(2026, 3, 2, 21, 0, 0, TimeSpan.Zero), history[0].ViewedAt);
        Assert.Equal(ViewingSource.Playback, history[0].Source);
        Assert.Equal(ViewingSource.Manual, history[1].Source);
        Assert.Equal(2, _repo.Get(id)!.ViewCount);
    }

    [Fact]
    public void A_viewing_without_a_date_is_kept_and_sorted_last()
    {
        var id = _repo.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        _repo.AddViewing(id, null);
        _repo.AddViewing(id, new DateTimeOffset(2026, 3, 2, 21, 0, 0, TimeSpan.Zero));

        var history = _repo.Viewings(id);

        Assert.NotNull(history[0].ViewedAt);
        Assert.Null(history[1].ViewedAt);
    }

    [Fact]
    public void A_wrong_line_can_be_erased_without_touching_the_others()
    {
        var id = _repo.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var mistake = _repo.AddViewing(id, new DateTimeOffset(2026, 1, 10, 20, 0, 0, TimeSpan.Zero));
        _repo.AddViewing(id, new DateTimeOffset(2026, 3, 2, 21, 0, 0, TimeSpan.Zero));

        _repo.DeleteViewing(mistake);

        var kept = Assert.Single(_repo.Viewings(id));
        Assert.Equal(new DateTimeOffset(2026, 3, 2, 21, 0, 0, TimeSpan.Zero), kept.ViewedAt);
    }

    [Fact]
    public void Marking_not_seen_clears_the_whole_history()
    {
        var id = _repo.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        _repo.AddViewing(id, DateTimeOffset.Now);
        _repo.AddViewing(id, DateTimeOffset.Now);

        _repo.SetSeen([id], false);

        Assert.Empty(_repo.Viewings(id));
        Assert.Equal(0, _repo.Get(id)!.ViewCount);
    }

    [Fact]
    public void The_panel_adds_and_erases_and_reports_the_count()
    {
        var id = _repo.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var vm = new ViewingsViewModel(_repo, id);
        Assert.False(vm.HasHistory);
        Assert.False(vm.Changed);

        vm.Date = new DateTimeOffset(2026, 3, 2, 21, 0, 0, TimeSpan.Zero);
        vm.AddCommand.Execute(null);
        vm.AddCommand.Execute(null);

        Assert.True(vm.Changed);
        Assert.Equal(2, vm.History.Count);
        Assert.Contains("2", vm.CountText, StringComparison.Ordinal);

        vm.DeleteCommand.Execute(vm.History[0]);
        Assert.Single(vm.History);
    }
}
