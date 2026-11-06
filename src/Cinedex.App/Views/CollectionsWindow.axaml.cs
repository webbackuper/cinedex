using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Interactivity;
using Cinedex.App.ViewModels;
using Cinedex.Core.Localization;
using Cinedex.App.Services;

namespace Cinedex.App.Views;

public sealed partial class CollectionsWindow : Window
{
    public CollectionsWindow() => InitializeComponent();

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is CollectionsViewModel vm)
        {
            await vm.CompleteMissingAsync();
        }
    }

    /// <summary>Film du catalogue : la fenêtre se ferme sur ce titre ; film manquant : sa fiche TMDb s'ouvre.</summary>
    private void OnFilmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CollectionsViewModel vm || (sender as Control)?.DataContext is not CollectionFilm film)
        {
            return;
        }

        if (film.IsOwned)
        {
            vm.Choose(film);
            Close(true);
        }
        else
        {
            WebLinks.Open(film.TmdbUrl);
        }
    }

    /// <summary>Referme le menu de renommage une fois le nouveau nom appliqué.</summary>
    private void OnRenamed(object? sender, RoutedEventArgs e) =>
        ((sender as Control)?.FindLogicalAncestorOfType<Popup>() ?? null)?.SetCurrentValue(Popup.IsOpenProperty, false);

    private void OnOpenTmdb(object? sender, RoutedEventArgs e) => WebLinks.Open((DataContext as CollectionsViewModel)?.TmdbUrl);

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CollectionsViewModel { Selected: { } row } vm &&
            await MessageWindow.ShowAsync(this, Tr.T("Supprimer la collection"), Tr.F("Supprimer la collection « {0} » ? Ses films restent dans le catalogue.", row.Name), confirm: true))
        {
            vm.DeleteCommand.Execute(null);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(false);
}
