using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
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
    public Task Actors_and_directors_take_the_place_of_the_posters() => ui.Run(() =>
    {
        using var catalog = new UiCatalog(30, people: true);
        var vm = catalog.ViewModel;
        vm.IsGridView = true;
        UiCatalog.Pump(200);
        var posters = catalog.Find<ScrollViewer>(s => s.Name == "PosterScroll");
        Assert.True(posters.IsEffectivelyVisible);

        // Lien « Acteurs » du bandeau gauche : la galerie remplace les affiches, la recherche a le focus.
        catalog.Find<Button>(b => b.Command == vm.OpenPeopleCommand).Command!.Execute(null);
        UiCatalog.Pump(300);
        Assert.False(posters.IsEffectivelyVisible);
        var search = catalog.Find<TextBox>(t => t.Name == "PeopleQueryBox");
        Assert.True(search.IsEffectivelyVisible);
        Assert.True(search.IsFocused);
        var cards = catalog.All<Button>().Where(b => b.Command == vm.SelectPersonCommand && b.IsEffectivelyVisible && b.Bounds.X >= 0).ToList();
        Assert.Contains(vm.PeopleBrowser!.People, p => p.Name == "Acteur 1");
        Assert.InRange(cards.Count, 6, 200);
        UiCatalog.Shot(catalog.Window, "acteurs");

        // Un clic : la personne dans le bandeau droit, sa carte marquée ; ses films y sont listés.
        cards[0].Command!.Execute(cards[0].CommandParameter);
        UiCatalog.Pump(300);
        Assert.True(vm.HasPersonPage);
        Assert.Contains(cards[0].Classes, c => c == "current");
        var personPanel = catalog.Find<ScrollViewer>(s => AutomationProperties.GetName(s) == "Fiche de la personne");
        Assert.True(personPanel.IsEffectivelyVisible);
        Assert.Contains(catalog.All<Button>(), b => b.Command == vm.ChoosePersonTitleCommand && b.IsEffectivelyVisible);
        UiCatalog.Shot(catalog.Window, "acteur-choisi");

        // Réalisateurs, puis retour aux affiches par le premier filtre de la bibliothèque, déjà choisi.
        vm.OpenDirectorsCommand.Execute(null);
        UiCatalog.Pump(200);
        Assert.Contains(vm.PeopleBrowser!.People, p => p.Name == "Réalisateur 1");
        Assert.DoesNotContain(vm.PeopleBrowser.People, p => p.Name == "Acteur 1");
        UiCatalog.Shot(catalog.Window, "realisateurs");
        var library = catalog.Find<ListBox>(l => l.Classes.Contains("nav"));
        library.RaiseEvent(new TappedEventArgs(InputElement.TappedEvent, new PointerEventArgs(InputElement.PointerReleasedEvent, library, null!, catalog.Window, default, 0, default, default)));
        UiCatalog.Pump(200);
        Assert.True(vm.IsTitlesView);
        Assert.True(posters.IsEffectivelyVisible);
    });

    [Fact]
    public Task Multiple_selection_shows_its_count_and_badges() => ui.Run(() =>
    {
        using var catalog = new UiCatalog(30);
        var vm = catalog.ViewModel;
        vm.IsGridView = true;
        UiCatalog.Pump(200);

        foreach (var title in vm.Titles.Take(3).ToList())
        {
            vm.SelectedTitles.Add(title);
        }

        UiCatalog.Pump(200);

        // Bandeau droit : le nombre de titres sélectionnés et leur liste.
        Assert.True(vm.HasMultiSelection);
        var label = catalog.Find<TextBlock>(t => t.Text == vm.SelectionLabel);
        Assert.True(label.IsEffectivelyVisible);

        // Chaque affiche sélectionnée porte sa croix dans un rond.
        var badges = catalog.All<Button>()
            .Where(b => b.Classes.Contains("selbadge") && b.IsEffectivelyVisible && b.FindAncestorOfType<PosterCard>() is { Bounds.X: >= 0 })
            .ToList();
        Assert.Equal(3, badges.Count);
        UiCatalog.Shot(catalog.Window, "selection-multiple");

        // La croix retire le titre de la sélection.
        badges[0].Command!.Execute(badges[0].CommandParameter);
        UiCatalog.Pump(100);
        Assert.Equal(2, vm.SelectedTitles.Count);
    });

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

    [Fact]
    public Task Widening_the_detail_panel_reflows_the_poster_grid() => ui.Run(() =>
    {
        using var catalog = new UiCatalog(300);
        var vm = catalog.ViewModel;
        vm.IsGridView = true;
        UiCatalog.Pump(300);

        int PostersPerRow()
        {
            var cards = catalog.All<PosterCard>().Where(c => c.IsEffectivelyVisible && c.Bounds.X >= 0).ToList(); // les cartes recyclées sont rangées hors champ
            var top = cards.Min(c => c.Bounds.Y);
            return cards.Count(c => Math.Abs(c.Bounds.Y - top) < 1);
        }

        // Les affiches remplissent toute la largeur : la dernière de la rangée touche le bord droit de la grille.
        void AssertRowIsFilled()
        {
            var repeater = catalog.Find<ItemsRepeater>(r => r.Name == "PosterGrid");
            var cards = catalog.All<PosterCard>().Where(c => c.IsEffectivelyVisible && c.Bounds.X >= 0).ToList(); // les cartes recyclées sont rangées hors champ
            var top = cards.Min(c => c.Bounds.Y);
            var right = cards.Where(c => Math.Abs(c.Bounds.Y - top) < 1).Max(c => c.Bounds.Right);
            Assert.InRange(repeater.Bounds.Width - right, 0, PostersPerRow());
            Assert.All(cards, c => Assert.True(c.Bounds.Width >= 172, $"carte de {c.Bounds.Width} px"));
        }

        AssertRowIsFilled();
        var detail = catalog.Find<GridSplitter>(s => s.Name == "DetailSplitter");
        Assert.Equal("Largeur de la fiche", Avalonia.Automation.AutomationProperties.GetName(detail));
        var before = PostersPerRow();

        // Fiche élargie : la liste perd des colonnes d'affiches, sans rien couper.
        vm.DetailWidth = 700;
        UiCatalog.Pump(300);
        var grid = Assert.IsType<Grid>(detail.Parent);
        Assert.Equal(700, grid.ColumnDefinitions[^1].ActualWidth, 0.5);
        var after = PostersPerRow();
        UiCatalog.Report($"Affiches par rangée : {before} avec la fiche par défaut, {after} avec une fiche de 700 px");
        Assert.True(after < before, $"{before} puis {after} affiches par rangée");
        AssertRowIsFilled();
        UiCatalog.Shot(catalog.Window, "fiche-large");

        // La liste garde au moins 380 px même si la fenêtre rétrécit.
        catalog.Window.Width = 1000;
        UiCatalog.Pump(300);
        Assert.True(grid.ColumnDefinitions[1].ActualWidth >= 379, $"liste {grid.ColumnDefinitions[1].ActualWidth} px");
        Assert.True(PostersPerRow() >= 1);
        AssertRowIsFilled();

        // Retour à la largeur par défaut : autant d'affiches qu'au départ.
        catalog.Window.Width = 1600;
        vm.DetailWidth = Cinedex.App.ViewModels.MainWindowViewModel.DefaultDetailWidth;
        UiCatalog.Pump(300);
        Assert.Equal(before, PostersPerRow());
    });
}
