using Cinedex.App.ViewModels;
using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

public sealed class SeriesTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TitleRepository _titles;
    private readonly SeriesRepository _series;
    private readonly MediaFileRepository _files;
    private readonly long _show;

    public SeriesTests()
    {
        var db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        db.Initialize(_temp.Path);
        _titles = new TitleRepository(db);
        _series = new SeriesRepository(db);
        _files = new MediaFileRepository(db);
        _show = _titles.Add(new NewTitle(TitleKind.Series, "Breaking Bad", 2008));
        _series.Merge(
            _show,
            [new SeasonRecord(0, "Spéciaux", null, null, 1, null), new SeasonRecord(1, "Saison 1", null, new DateOnly(2008, 1, 20), 2, null)],
            new Dictionary<int, IReadOnlyList<EpisodeRecord>>
            {
                [0] = [new EpisodeRecord(1, "Bonus", null, null, null, 9)],
                [1] = [new EpisodeRecord(1, "Pilot", null, new DateOnly(2008, 1, 20), 58, 1), new EpisodeRecord(2, "Cat's in the Bag", null, new DateOnly(2008, 1, 27), 48, 2)],
            });
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Merge_is_idempotent_and_respects_locked_seasons()
    {
        var season1 = _series.Seasons(_show).Single(s => s.Number == 1);
        _series.SetSeasonLocked(season1.Id, true);

        var added = _series.Merge(
            _show,
            [new SeasonRecord(1, "Renommée", null, null, 3, null)],
            new Dictionary<int, IReadOnlyList<EpisodeRecord>> { [1] = [new EpisodeRecord(3, "Nouveau", null, null, null, 3)] });

        Assert.Equal(0, added);
        Assert.Equal("Saison 1", _series.Seasons(_show).Single(s => s.Number == 1).Name);
        Assert.Equal(2, _series.Episodes(season1.Id).Count);
    }

    [Fact]
    public void Series_is_seen_when_every_regular_episode_is_seen()
    {
        var season1 = _series.Seasons(_show).Single(s => s.Number == 1);
        var episodes = _series.Episodes(season1.Id);

        _series.SetEpisodesSeen([episodes[0].Id], true);
        Assert.False(Assert.Single(_titles.ListAll()).IsSeen);

        _series.SetEpisodesSeen([episodes[1].Id], true);
        Assert.True(Assert.Single(_titles.ListAll()).IsSeen);
        Assert.Equal("2/2 vus", _series.Seasons(_show).Single(s => s.Number == 1).Progress);

        _series.SetSeasonSeen(season1.Id, false);
        Assert.False(Assert.Single(_titles.ListAll()).IsSeen);
    }

    [Fact]
    public void View_model_toggles_episodes_and_attaches_folder_files()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "BB")).FullName;
        File.WriteAllBytes(Path.Combine(folder, "Breaking.Bad.S01E02.mkv"), [1]);
        File.WriteAllBytes(Path.Combine(folder, "Breaking.Bad.S02E01.mkv"), [1]);
        var vm = new SeriesViewModel(_show, "Breaking Bad", null, _series, null, _files, null);

        Assert.Equal(1, vm.SelectedSeason!.Number);
        Assert.Equal(2, vm.Episodes.Count);

        vm.Episodes[0].IsSeen = true;
        Assert.Equal("1/2 vus", vm.Seasons.Single(s => s.Number == 1).Progress);
        Assert.True(vm.Changed);

        Assert.Equal(2, vm.AttachFolder(folder));
        Assert.Equal(3, vm.Seasons.Count);
        Assert.True(vm.Episodes.Single(e => e.Episode.Number == 2).HasFile);
        Assert.Contains(Path.Combine(folder, "Breaking.Bad.S02E01.mkv"), _files.KnownPaths());
    }
}
