using Avalonia.Controls;
using Avalonia.Interactivity;
using Cinedex.App.ViewModels;
using Cinedex.Core.Metadata;

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

    private void OnCancel(object? sender, RoutedEventArgs e) => Close((MetadataSearchResult?)null);
}
