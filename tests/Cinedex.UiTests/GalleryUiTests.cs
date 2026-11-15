using Avalonia.Controls;
using Cinedex.App.ViewModels;

namespace Cinedex.UiTests;

/// <summary>La galerie des genres dans la vraie fenêtre : des vignettes dessinées, on entre, on revient.</summary>
public sealed class GalleryUiTests(UiSession ui)
{
    [Fact]
    public Task The_genre_gallery_draws_its_cards_and_lets_one_in_and_back() => ui.Run(() =>
    {
        using var catalog = new UiCatalog(300);
        var vm = catalog.ViewModel;
        var genres = vm.SideBar.Facets.Single(f => f.Key == SideBarViewModel.GenresKey);

        genres.ToggleCommand.Execute(null);
        catalog.Settle();
        UiCatalog.Pump(300);

        Assert.True(vm.IsGalleryView);
        Assert.NotEmpty(vm.Gallery!.Cards);
        // Chaque genre a son visuel : des affiches du catalogue.
        Assert.All(vm.Gallery.Cards, card => Assert.True(card.HasImages));
        var drawn = catalog.All<Button>().Count(b => b.DataContext is GalleryCard);
        Assert.Equal(vm.Gallery.Cards.Count, drawn);
        UiCatalog.Shot(catalog.Window, "galerie-genres");

        var card = vm.Gallery.Cards[0];
        vm.OpenGalleryCardCommand.Execute(card);
        catalog.Settle();

        Assert.True(vm.IsTitlesView);
        Assert.NotEmpty(vm.Titles);
        Assert.Equal(card.Title, vm.Trail!.Title);
        Assert.Equal(card.Title, genres.SelectedItem!.Label);
        UiCatalog.Shot(catalog.Window, "dans-un-genre");

        vm.BackToGalleryCommand.Execute(null);
        catalog.Settle();
        Assert.True(vm.IsGalleryView);
    });
}
