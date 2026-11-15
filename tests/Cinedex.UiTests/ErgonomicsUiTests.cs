using Avalonia.Controls;
using Cinedex.App.ViewModels;
using Cinedex.App.Views;
using Cinedex.Core.Titles;

namespace Cinedex.UiTests;

/// <summary>
/// Ergonomie : la fenêtre qui se replie sur petit écran, les actions au survol qui laissent la sélection en
/// paix, et la fiche qui sait si elle a été modifiée.
/// </summary>
public sealed class ErgonomicsUiTests(UiSession ui)
{
    [Fact]
    public Task A_narrow_window_folds_the_record_then_the_banner_and_gives_them_back() => ui.Run(() =>
    {
        using var catalog = new UiCatalog(30);
        var vm = catalog.ViewModel;
        var grid = catalog.Find<Grid>(g => g.Name == "MainGrid");
        Assert.False(vm.DetailCollapsed);
        Assert.False(vm.IsBannerRail);
        Assert.Equal(232, grid.ColumnDefinitions[0].Width.Value);

        // Sous 1 150 px : la fiche se replie, le bandeau reste.
        catalog.Window.Width = 1100;
        UiCatalog.Pump(50);
        Assert.True(vm.DetailCollapsed);
        Assert.False(vm.IsBannerRail);

        // Sous 1 000 px : le bandeau passe en colonne d'icônes.
        catalog.Window.Width = 980;
        UiCatalog.Pump(50);
        Assert.True(vm.IsBannerRail);
        Assert.Equal(64, grid.ColumnDefinitions[0].Width.Value);
        UiCatalog.Shot(catalog.Window, "fenetre-etroite");

        // La place revient : ce que la largeur avait replié se rouvre.
        catalog.Window.Width = 1600;
        UiCatalog.Pump(50);
        Assert.False(vm.IsBannerRail);
        Assert.False(vm.DetailCollapsed);
        Assert.Equal(232, grid.ColumnDefinitions[0].Width.Value);
    });

    [Fact]
    public Task What_the_user_folded_himself_stays_folded_when_the_window_widens() => ui.Run(() =>
    {
        using var catalog = new UiCatalog(30);
        var vm = catalog.ViewModel;

        // Replié à la main, sur une fenêtre large.
        vm.ToggleDetailCommand.Execute(null);
        vm.ToggleBannerCommand.Execute(null);
        Assert.True(vm.DetailCollapsed);
        Assert.True(vm.IsBannerRail);

        catalog.Window.Width = 980;
        UiCatalog.Pump(50);
        catalog.Window.Width = 1600;
        UiCatalog.Pump(50);

        // La largeur n'y était pour rien : elle ne rouvre pas ce qu'elle n'a pas fermé.
        Assert.True(vm.DetailCollapsed);
        Assert.True(vm.IsBannerRail);
    });

    [Fact]
    public Task Row_actions_act_on_the_row_and_leave_the_selection_alone() => ui.Run(() =>
    {
        using var catalog = new UiCatalog(30);
        var vm = catalog.ViewModel;
        var selected = vm.Titles[2];
        var hovered = vm.Titles[7];
        vm.SelectedTitles.Add(selected);
        UiCatalog.Pump();
        var seen = hovered.IsSeen;
        var favorite = hovered.IsFavorite;

        vm.ToggleSeenOfCommand.Execute(hovered);
        vm.ToggleFavoriteOfCommand.Execute(vm.Titles.Single(t => t.Id == hovered.Id));
        UiCatalog.Pump();

        var after = vm.Titles.Single(t => t.Id == hovered.Id);
        Assert.Equal(!seen, after.IsSeen);
        Assert.Equal(!favorite, after.IsFavorite);
        Assert.Equal([selected.Id], vm.SelectedTitles.Select(t => t.Id));
        Assert.Equal(selected.Id, vm.Detail!.Id);
    });

    [Fact]
    public Task The_record_knows_whether_it_was_changed() => ui.Run(() =>
    {
        using var catalog = new UiCatalog(0);
        var id = catalog.Titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var editor = new TitleEditorViewModel(
            catalog.Titles.Get(id)!,
            catalog.Titles.Genres(),
            catalog.Titles.RefItems("medium"),
            catalog.Titles.RefItems("case"),
            catalog.Titles.RefItems("version"),
            extras: new TitleEditorExtras(PreviousTitleId: null, NextTitleId: 42));
        var window = new TitleEditorWindow { DataContext = editor };
        window.Show();
        UiCatalog.Pump();

        // Ouverte et laissée telle quelle : rien à enregistrer, donc rien à demander en la quittant.
        Assert.False(editor.HasUnsavedChanges);
        Assert.False(editor.HasPrevious);
        Assert.True(editor.HasNext);

        editor.Title = "Heat (version longue)";
        Assert.True(editor.HasUnsavedChanges);

        editor.Title = "Heat";
        Assert.False(editor.HasUnsavedChanges);

        // Une saisie invalide compte comme une modification, et ne laisse pas de message d'erreur derrière elle.
        editor.Year = "pas une année";
        Assert.True(editor.HasUnsavedChanges);
        Assert.Null(editor.LastError);

        window.Close();
    });
}
