using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Cinedex.App.ViewModels;
using Cinedex.App.Views;
using Cinedex.Core.People;
using Cinedex.Core.Titles;

namespace Cinedex.UiTests;

/// <summary>Fiche d'un film, onglet Distribution : réalisateurs et acteurs sont des boutons nommés vers leur fiche.</summary>
public sealed class TitleEditorUiTests(UiSession ui)
{
    [Fact]
    public Task Directors_and_actors_are_clickable_in_the_cast_tab() => ui.Run(() =>
    {
        using var catalog = new UiCatalog(0);
        var id = catalog.Titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var editor = new TitleEditorViewModel(
            catalog.Titles.Get(id)!,
            catalog.Titles.Genres(),
            catalog.Titles.RefItems("medium"),
            catalog.Titles.RefItems("case"),
            catalog.Titles.RefItems("version"),
            extras: new TitleEditorExtras(
                Cast: [new CastMember(2, "Al Pacino", "actor", "Vincent Hanna", 0, null, null)],
                Directors: [new CastMember(1, "Michael Mann", "director", null, 0, null, null)]));
        var window = new TitleEditorWindow { DataContext = editor };
        window.Show();
        var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedIndex = tabs.Items.OfType<TabItem>().ToList().FindIndex(t => t.Header as string == "Distribution");
        UiCatalog.Pump(100);
        UiCatalog.Shot(window, "fiche-distribution");

        var people = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && b.DataContext is CastMember)
            .ToList();
        Assert.Equal(["Michael Mann", "Al Pacino"], people.Select(b => ((CastMember)b.DataContext!).Name));
        Assert.All(people, b => Assert.StartsWith(((CastMember)b.DataContext!).Name, ControlAutomationPeer.CreatePeerForElement(b).GetName(), StringComparison.Ordinal));
        window.Close();
    });
}
