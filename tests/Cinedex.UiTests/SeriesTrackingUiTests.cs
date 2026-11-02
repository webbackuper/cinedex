using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Cinedex.App.ViewModels;
using Cinedex.App.Views;
using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.UiTests;

/// <summary>Fenêtre du suivi des séries : un clic sur « Vu » passe à l'épisode suivant.</summary>
public sealed class SeriesTrackingUiTests(UiSession ui)
{
    [Fact]
    public Task Clicking_seen_moves_to_the_next_episode() => ui.Run(() =>
    {
        using var catalog = new UiCatalog(0);
        var series = new SeriesRepository(catalog.Database);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var show = catalog.Titles.Add(new NewTitle(TitleKind.Series, "Fondation", 2021));
        series.Merge(
            show,
            [new SeasonRecord(1, "Saison 1", null, today.AddDays(-60), 3, null)],
            new Dictionary<int, IReadOnlyList<EpisodeRecord>>
            {
                [1] =
                [
                    new EpisodeRecord(1, "Le Mathématicien", null, today.AddDays(-14), 60, 1),
                    new EpisodeRecord(2, "Préparation", null, today.AddDays(-7), 60, 2),
                    new EpisodeRecord(3, "La Voûte", null, today.AddDays(7), 60, 3),
                ],
            });

        var vm = new SeriesTrackingViewModel(series, metadata: null, _ => null);
        var window = new SeriesTrackingWindow { DataContext = vm };
        window.Show();
        UiCatalog.Pump(100);
        UiCatalog.Shot(window, "suivi-series");

        var seen = window.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.Command == vm.MarkNextSeenCommand);
        var point = seen.TranslatePoint(new Point(seen.Bounds.Width / 2, seen.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        UiCatalog.Pump(100);

        Assert.Contains("S01E02", Assert.Single(vm.Rows).NextText, StringComparison.Ordinal);
        Assert.Contains("S01E03", Assert.Single(vm.Upcoming).Episode, StringComparison.Ordinal);
        window.Close();
    });
}
