using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Cinedex.App.Controls;
using Cinedex.Core.Titles;

namespace Cinedex.UiTests;

/// <summary>
/// Fenêtre principale pilotée comme par un utilisateur, sur un catalogue de 12 000 films : ouverture, défilement,
/// recherche au clavier, grille d'affiches à la souris et au clavier.
/// </summary>
public sealed class MainWindowUiTests(UiSession ui)
{
    private const int BigCatalog = 12_000;

    [Fact]
    public Task Big_catalog_opens_quickly_and_only_visible_rows_exist() => ui.Run(() =>
    {
        using var catalog = new UiCatalog(BigCatalog);
        UiCatalog.Report($"Ouverture de {BigCatalog} titres : {catalog.OpenedIn.TotalMilliseconds:0} ms");
        Assert.Equal(BigCatalog, catalog.ViewModel.Titles.Count);
        Assert.True(catalog.OpenedIn < TimeSpan.FromSeconds(8), $"ouverture {catalog.OpenedIn}");

        // Vue liste : seules les lignes visibles sont créées.
        var list = catalog.Find<ListBox>(l => l.Classes.Contains("titles"));
        var rows = list.GetRealizedContainers().Count();
        UiCatalog.Report($"Lignes créées dans la liste : {rows}");
        Assert.InRange(rows, 1, 120);
        UiCatalog.Shot(catalog.Window, "liste-12000");

        // Vue affiches : grille virtualisée, quelques dizaines de cartes seulement.
        var watch = Stopwatch.StartNew();
        catalog.ViewModel.IsGridView = true;
        UiCatalog.Pump(300);
        var cards = catalog.All<PosterCard>().Count();
        UiCatalog.Report($"Passage en vue affiches : {watch.ElapsedMilliseconds} ms, {cards} cartes créées");
        Assert.InRange(cards, 1, 200);
        UiCatalog.Shot(catalog.Window, "affiches-12000");

        // Défilement jusqu'au bout : toujours peu de cartes.
        var scroll = catalog.Find<ScrollViewer>(s => s.Name == "PosterScroll");
        scroll.Offset = new Vector(0, scroll.Extent.Height);
        UiCatalog.Pump(300);
        Assert.InRange(catalog.All<PosterCard>().Count(), 1, 250);
    });

    [Fact]
    public Task Marking_a_favorite_in_a_big_catalog_updates_only_that_line() => ui.Run(() =>
    {
        using var catalog = new UiCatalog(BigCatalog);
        var vm = catalog.ViewModel;
        vm.SelectedTitles.Add(vm.Titles[10]);
        var id = vm.Titles[10].Id;
        var before = vm.Titles[10].IsFavorite;

        var watch = Stopwatch.StartNew();
        vm.ToggleFavoriteCommand.Execute(null);
        UiCatalog.Pump();
        UiCatalog.Report($"Favori sur une ligne parmi {BigCatalog} : {watch.ElapsedMilliseconds} ms");

        Assert.Equal(!before, vm.Titles.Single(t => t.Id == id).IsFavorite);
        Assert.Equal(BigCatalog, vm.Titles.Count);
        Assert.True(watch.ElapsedMilliseconds < 400, $"{watch.ElapsedMilliseconds} ms");
    });

    [Fact]
    public Task Typing_in_the_search_box_filters_the_list() => ui.RunAsync(async () =>
    {
        using var catalog = new UiCatalog(2_000);
        var box = catalog.Find<TextBox>(t => t.Name == "SearchBox");
        catalog.Window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control);
        UiCatalog.Pump();
        Assert.True(box.IsFocused, "Ctrl+F place le curseur dans la recherche");

        catalog.Window.KeyTextInput("Retour");
        await UiCatalog.Wait(700);

        var titles = catalog.ViewModel.Titles;
        Assert.Equal("Retour", box.Text);
        Assert.Equal("Retour", catalog.ViewModel.SearchText);
        Assert.NotEmpty(titles);
        Assert.True(titles.Count < 2_000, $"{titles.Count} titres après la recherche");
        Assert.All(titles, t => Assert.Contains("retour", (t.Title + " " + t.Overview).ToLowerInvariant(), StringComparison.Ordinal));
        UiCatalog.Shot(catalog.Window, "recherche");

        // Échap efface la recherche.
        catalog.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await UiCatalog.Wait(700);
        Assert.Equal(2_000, catalog.ViewModel.Titles.Count);
    });

    [Fact]
    public Task Posters_are_selected_with_the_mouse_and_the_keyboard() => ui.Run(() =>
    {
        using var catalog = new UiCatalog(300);
        var vm = catalog.ViewModel;
        vm.IsGridView = true;
        UiCatalog.Pump(200);

        // Clic sur la deuxième affiche.
        var second = catalog.All<PosterCard>().First(c => c.DataContext is TitleSummary t && t.Id == vm.Titles[1].Id);
        var point = second.TranslatePoint(new Point(40, 40), catalog.Window)!.Value;
        catalog.Window.MouseDown(point, MouseButton.Left);
        catalog.Window.MouseUp(point, MouseButton.Left);
        UiCatalog.Pump();
        Assert.Equal(vm.Titles[1].Id, Assert.Single(vm.SelectedTitles).Id);
        Assert.True(second.IsSelected);
        Assert.Equal(vm.Titles[1].Id, vm.Detail?.Id);

        // Flèche droite, puis flèche bas (une rangée plus loin), puis Maj+flèche gauche (deux titres).
        catalog.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        UiCatalog.Pump();
        Assert.Equal(vm.Titles[2].Id, Assert.Single(vm.SelectedTitles).Id);

        catalog.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        UiCatalog.Pump();
        var index = vm.Titles.IndexOf(Assert.Single(vm.SelectedTitles));
        Assert.True(index > 2, $"une rangée plus bas : rang {index}");

        catalog.Window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
        UiCatalog.Pump();
        Assert.Equal(2, vm.SelectedTitles.Count);

        // Ctrl+clic ajoute un titre à la sélection.
        var first = catalog.All<PosterCard>().First(c => c.DataContext is TitleSummary t && t.Id == vm.Titles[0].Id);
        var firstPoint = first.TranslatePoint(new Point(40, 40), catalog.Window)!.Value;
        catalog.Window.MouseDown(firstPoint, MouseButton.Left, RawInputModifiers.Control);
        catalog.Window.MouseUp(firstPoint, MouseButton.Left, RawInputModifiers.Control);
        UiCatalog.Pump();
        Assert.Equal(3, vm.SelectedTitles.Count);
        UiCatalog.Shot(catalog.Window, "affiches-selection");
    });

    [Fact]
    public Task Seen_filter_from_the_sidebar_reduces_the_list() => ui.Run(() =>
    {
        using var catalog = new UiCatalog(1_000);
        var vm = catalog.ViewModel;
        vm.SelectedFilter = vm.Filters.Single(f => f.Value == QuickFilter.Seen);
        UiCatalog.Pump();

        Assert.NotEmpty(vm.Titles);
        Assert.All(vm.Titles, t => Assert.True(t.IsSeen));
        Assert.True(vm.Titles.Count < 1_000);
    });

    [Fact]
    public Task Detail_panel_shows_the_genres_between_the_buttons_and_the_ratings() => ui.Run(() =>
    {
        using var catalog = new UiCatalog(50);
        var vm = catalog.ViewModel;
        vm.SelectedTitles.Add(vm.Titles[3]);
        UiCatalog.Pump(100);

        var expected = catalog.Titles.GenreLabels(vm.Titles[3].Id);
        Assert.NotEmpty(expected);
        Assert.Equal(expected, vm.DetailGenres);

        // Pastilles affichées dans la fiche, après la rangée de boutons et avant les notes.
        var genres = catalog.Find<ItemsControl>(c => Avalonia.Automation.AutomationProperties.GetName(c) == "Genres");
        Assert.True(genres.IsEffectivelyVisible);
        var panel = Assert.IsType<StackPanel>(genres.Parent);
        var index = panel.Children.IndexOf(genres);
        Assert.IsType<WrapPanel>(panel.Children[index - 1]);
        Assert.Equal(vm.Detail, panel.Children[index + 1].DataContext);
        UiCatalog.Shot(catalog.Window, "fiche-genres");

        // Plus de genres quand rien n'est sélectionné.
        vm.SelectedTitles.Clear();
        UiCatalog.Pump();
        Assert.Empty(vm.DetailGenres);
    });
}
