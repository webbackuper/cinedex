using Avalonia.Controls;
using Avalonia.Interactivity;
using Cinedex.App.ViewModels;
using Cinedex.Core.Metadata;
using Cinedex.App.Services;

namespace Cinedex.App.Views;

public sealed partial class MetadataSearchWindow : Window
{
    public MetadataSearchWindow() => InitializeComponent();

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        QueryBox.Focus();
        if (DataContext is MetadataSearchViewModel { AutoSearch: true } vm)
        {
            await vm.SearchCommand.ExecuteAsync(null);
        }
    }

    private void OnChoose(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MetadataSearchViewModel { Selected: { } row })
        {
            Close(row.Result);
        }
    }

    private void OnOpenSearch(object? sender, RoutedEventArgs e) => WebLinks.Open((DataContext as MetadataSearchViewModel)?.SearchUrl);

    private void OnOpenResult(object? sender, RoutedEventArgs e) => WebLinks.Open(((sender as Control)?.DataContext as SearchRow)?.TmdbUrl);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close((MetadataSearchResult?)null);
}
