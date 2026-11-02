using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

/// <summary>Économiseur d'écran : réglages, inactivité, ordre du diaporama, affiches retenues.</summary>
public sealed class ScreenSaverTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;

    public ScreenSaverTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Settings_have_defaults_and_are_clamped()
    {
        var store = new SettingsStore(_db);
        var defaults = ScreenSaverSettings.Load(store);
        Assert.Equal((true, 5, 8, true), (defaults.Enabled, defaults.IdleMinutes, defaults.IntervalSeconds, defaults.ShowTitles));

        new ScreenSaverSettings { Enabled = false, IdleMinutes = 1000, IntervalSeconds = 0, ShowTitles = false }.Save(store);
        var saved = ScreenSaverSettings.Load(store);
        Assert.Equal((false, 240, 2, false), (saved.Enabled, saved.IdleMinutes, saved.IntervalSeconds, saved.ShowTitles));
    }

    [Fact]
    public void Starts_only_when_enabled_active_and_idle_long_enough()
    {
        var clock = new ManualClock();
        var idle = new IdleMonitor(clock);
        var settings = new ScreenSaverSettings { IdleMinutes = 5 };

        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.False(idle.ShouldStart(settings, windowActive: true));

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(idle.ShouldStart(settings, windowActive: true));
        Assert.False(idle.ShouldStart(settings, windowActive: false));
        Assert.False(idle.ShouldStart(settings with { Enabled = false }, windowActive: true));

        idle.IsShowing = true;
        Assert.False(idle.ShouldStart(settings, windowActive: true));

        idle.IsShowing = false;
        idle.Touch();
        Assert.False(idle.ShouldStart(settings, windowActive: true));
    }

    [Fact]
    public void Every_poster_is_shown_once_per_round_without_immediate_repeat()
    {
        var slides = Enumerable.Range(1, 5).Select(i => new ScreenSaverSlide(i, $"Film {i}", 2000 + i, $"p{i}.jpg", null)).ToList();
        var vm = new ScreenSaverViewModel(slides, TimeSpan.FromSeconds(8), showTitles: true, new Random(42));

        var shown = new List<long> { vm.Current!.TitleId };
        for (var i = 0; i < 14; i++)
        {
            vm.Next();
            Assert.NotEqual(shown[^1], vm.Current!.TitleId);
            shown.Add(vm.Current.TitleId);
        }

        foreach (var round in shown.Chunk(5))
        {
            Assert.Equal([1L, 2, 3, 4, 5], round.Order());
        }

        Assert.Equal("p1.jpg", slides[0].BackgroundPath);
    }

    [Fact]
    public void Slides_are_the_titles_with_a_poster_and_respect_the_adult_filter()
    {
        var titles = new TitleRepository(_db);
        var images = new ImageStore(_db, Path.Combine(_temp.Path, "images"));
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0, 0, 0, 0, 0];
        var poster = images.Save(png);
        var withPoster = titles.Add(new NewTitle(TitleKind.Movie, "Avec affiche", 1999));
        var adult = titles.Add(new NewTitle(TitleKind.Movie, "Adulte", 2001));
        titles.Add(new NewTitle(TitleKind.Movie, "Sans affiche", 2002));
        images.Attach(withPoster, poster, "poster");
        images.Attach(adult, poster, "poster");
        titles.Update(titles.Get(adult)! with { IsAdult = true });

        Assert.Equal(["Adulte", "Avec affiche"], titles.PosterSlides(hideAdult: false).Select(s => s.Title).Order());
        Assert.Equal("Avec affiche", Assert.Single(titles.PosterSlides(hideAdult: true)).Title);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
