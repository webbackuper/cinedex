using Cinedex.App.ViewModels;
using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

/// <summary>Suivi des séries : épisodes vus sur diffusés, prochain épisode à regarder, prochaines diffusions.</summary>
public sealed class SeriesTrackingTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 2);

    private readonly TempDirectory _temp = new();
    private readonly TitleRepository _titles;
    private readonly SeriesRepository _series;
    private readonly long _running;
    private readonly long _ended;

    public SeriesTrackingTests()
    {
        var db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        db.Initialize(_temp.Path);
        _titles = new TitleRepository(db);
        _series = new SeriesRepository(db);

        _running = _titles.Add(new NewTitle(TitleKind.Series, "Fondation", 2021));
        _series.Merge(
            _running,
            [new SeasonRecord(0, "Spéciaux", null, null, 1, null), new SeasonRecord(1, "Saison 1", null, new DateOnly(2026, 9, 1), 4, null)],
            new Dictionary<int, IReadOnlyList<EpisodeRecord>>
            {
                [0] = [new EpisodeRecord(1, "Coulisses", null, new DateOnly(2026, 8, 1), null, 90)],
                [1] =
                [
                    new EpisodeRecord(1, "Le Mathématicien", null, new DateOnly(2026, 10, 1), 60, 1),
                    new EpisodeRecord(2, "Préparation", null, new DateOnly(2026, 10, 8), 60, 2),
                    new EpisodeRecord(3, "Le Voûte", null, new DateOnly(2026, 11, 10), 60, 3),
                    new EpisodeRecord(4, "Finale", null, new DateOnly(2027, 3, 1), 60, 4),
                ],
            });
        _ended = _titles.Add(new NewTitle(TitleKind.Series, "Twin Peaks", 1990));
        _series.Merge(
            _ended,
            [new SeasonRecord(1, "Saison 1", null, new DateOnly(1990, 4, 8), 1, null)],
            new Dictionary<int, IReadOnlyList<EpisodeRecord>> { [1] = [new EpisodeRecord(1, "Pilote", null, new DateOnly(1990, 4, 8), 90, 10)] });
        _series.SetSeriesSeen(_ended, true);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Tracking_counts_aired_and_seen_episodes_and_finds_the_next_ones()
    {
        var tracking = _series.Tracking(Today);

        var running = tracking[0];
        Assert.Equal("Fondation", running.Title);
        Assert.Equal((2, 0, 2), (running.Aired, running.Seen, running.ToWatch));
        Assert.Equal("S01E01", running.NextToWatch!.Code);
        Assert.Equal("S01E03", running.NextAiring!.Code);

        var ended = tracking[1];
        Assert.Equal((1, 1, 0), (ended.Aired, ended.Seen, ended.ToWatch));
        Assert.Null(ended.NextToWatch);
        Assert.Equal((2, 1), _series.CountToWatch(Today));
    }

    [Fact]
    public void Upcoming_lists_the_next_airings_in_the_period()
    {
        var upcoming = Assert.Single(_series.Upcoming(Today, 60));
        Assert.Equal(("Fondation", "S01E03"), (upcoming.Series, upcoming.Episode.Code));
        Assert.Equal(2, _series.Upcoming(Today, 365).Count);
    }

    [Fact]
    public void Marking_the_next_episode_seen_moves_to_the_following_one()
    {
        var vm = new SeriesTrackingViewModel(_series, metadata: null, _ => null, () => Today);
        var row = Assert.Single(vm.Rows);
        Assert.Contains("S01E01", row.NextText, StringComparison.Ordinal);

        vm.MarkNextSeenCommand.Execute(row);

        Assert.True(vm.Changed);
        Assert.Contains("S01E02", Assert.Single(vm.Rows).NextText, StringComparison.Ordinal);
        vm.MarkNextSeenCommand.Execute(vm.Rows[0]);
        Assert.Empty(vm.Rows); // plus rien à regarder

        vm.SelectedFilter = vm.Filters[2];
        Assert.Equal(2, vm.Rows.Count);
        Assert.Contains("2 série(s), toutes à jour", vm.Summary, StringComparison.Ordinal);
    }
}
