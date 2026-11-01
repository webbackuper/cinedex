using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Cinedex.App.ViewModels;
using Cinedex.Media;

namespace Cinedex.App.Views;

public sealed partial class ImportFolderWindow : Window
{
    public ImportFolderWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

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

    /// <summary>Un ou plusieurs fichiers vidéo, ajoutés à la liste sans analyser tout un dossier.</summary>
    private async void OnPickFiles(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Fichiers vidéo à ajouter",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Fichiers vidéo")
                {
                    Patterns = ((DataContext as ImportFolderViewModel)?.VideoExtensions ?? [.. FileNameParser.VideoExtensions]).Select(x => "*" + x).ToList(),
                },
                FilePickerFileTypes.All,
            ],
        });
        await AddAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList());
    }

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    /// <summary>Fichiers vidéo ou dossiers glissés depuis l'explorateur.</summary>
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var items = e.DataTransfer.TryGetFiles() ?? [];
        await AddAsync(items.Select(i => i.TryGetLocalPath()).OfType<string>().ToList());
    }

    private async Task AddAsync(List<string> paths)
    {
        if (paths.Count > 0 && DataContext is ImportFolderViewModel vm)
        {
            await vm.AddPathsAsync(paths);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) =>
        Close((DataContext as ImportFolderViewModel)?.Imported ?? false);
}
