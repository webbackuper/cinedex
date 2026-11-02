using Avalonia.Controls;
using Avalonia.Interactivity;
using Cinedex.App.ViewModels;
using Cinedex.Core.Metadata;

namespace Cinedex.App.Views;

public sealed partial class MatchReviewWindow : Window
{
    public MatchReviewWindow() => InitializeComponent();

    private void OnOpenSearch(object? sender, RoutedEventArgs e) =>
        WebLinks.Open((DataContext as MatchReviewViewModel)?.SelectedItem?.SearchUrl);

    private void OnOpenCandidate(object? sender, RoutedEventArgs e) =>
        WebLinks.Open(((sender as Control)?.DataContext as TmdbCandidate)?.TmdbUrl);

    /// <summary>Recherche TMDb libre pour le titre affiché ; la fiche choisie lui est appliquée.</summary>
    private async void OnSearchOther(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MatchReviewViewModel { SelectedItem: { } item } vm)
        {
            return;
        }

        var search = new MetadataSearchViewModel(vm.Metadata, item.Review.Query);
        if (await new MetadataSearchWindow { DataContext = search }.ShowDialog<MetadataSearchResult?>(this) is { } result)
        {
            await vm.ApplyAsync(result);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close((DataContext as MatchReviewViewModel)?.Changed ?? false);
}
