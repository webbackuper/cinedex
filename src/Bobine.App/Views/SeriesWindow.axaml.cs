using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Bobine.App.ViewModels;

namespace Bobine.App.Views;

public sealed partial class SeriesWindow : Window
{
    public SeriesWindow() => InitializeComponent();

    private async void OnAttachFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Dossier des épisodes", AllowMultiple = false });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path && DataContext is SeriesViewModel vm)
        {
            vm.AttachFolder(path);
        }
    }

    private void OnEpisodeDoubleTapped(object? sender, RoutedEventArgs e) =>
        (DataContext as SeriesViewModel)?.PlayEpisodeCommand.Execute(null);

    private void OnClose(object? sender, RoutedEventArgs e) => Close((DataContext as SeriesViewModel)?.Changed ?? false);
}
