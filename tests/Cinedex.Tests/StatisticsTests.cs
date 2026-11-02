using Cinedex.App.Security;
using Cinedex.App.ViewModels;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

public sealed class StatisticsTests
{
    [Fact]
    public void Catalog_figures_and_distributions()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        var heat = titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var ran = titles.Add(new NewTitle(TitleKind.Movie, "Ran", 1985));
        titles.Add(new NewTitle(TitleKind.Series, "Breaking Bad", 2008));
        titles.Update(titles.Get(heat)! with { GenreCodes = ["crime", "drama"], RuntimeMinutes = 170, IsFavorite = true });
        titles.Update(titles.Get(ran)! with { GenreCodes = ["drama"], RuntimeMinutes = 160 });
        titles.SetSeen([heat], true);
        new MediaFileRepository(db).Attach(heat, Path.Combine(temp.Path, "heat.mkv"), 3L << 30);

        var stats = new StatisticsRepository(db).Compute();

        Assert.Equal((2, 1, 1, 1, 1), (stats.Films, stats.Series, stats.Seen, stats.Favorites, stats.Files));
        Assert.Equal(330, stats.FilmMinutes);
        Assert.Equal(3L << 30, stats.TotalBytes);
        Assert.Equal(("Drame", 2, 1.0), (stats.Genres[0].Label, stats.Genres[0].Count, stats.Genres[0].Ratio));
        Assert.Equal(0.5, stats.Genres.Single(g => g.Label == "Policier").Ratio);
        Assert.Equal(["1980 – 1989", "1990 – 1999", "2000 – 2009"], stats.Decades.Select(d => d.Label));

        var settings = new SettingsViewModel(new NoVault(), new SettingsStore(db), statistics: stats);
        Assert.True(settings.HasStatistics);
        Assert.Contains(settings.StatCards, c => c.Label == "Films" && c.Value == "2");
        Assert.Contains(settings.StatCards, c => c.Label == "Durée totale des films" && c.Value == "5 h");
        Assert.Contains(settings.StatCards, c => c.Label == "Taille des fichiers" && c.Value.EndsWith("Go", StringComparison.Ordinal));
    }

    private sealed class NoVault : ISecretStore
    {
        public string? Read(string name) => null;

        public void Write(string name, string secret)
        {
        }

        public void Delete(string name)
        {
        }
    }
}
