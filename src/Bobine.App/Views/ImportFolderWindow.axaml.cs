using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Bobine.App.ViewModels;

namespace Bobine.App.Views;

public sealed partial class ImportFolderWindow : Window
{
    public ImportFolderWindow() => InitializeComponent();

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Dossier de films",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path && DataContext is ImportFolderViewModel vm)
        {
            vm.Folder = path;
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) =>
        Close((DataContext as ImportFolderViewModel)?.Imported ?? false);
}
