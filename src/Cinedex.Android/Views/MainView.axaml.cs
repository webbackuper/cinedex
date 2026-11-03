using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Cinedex.Android.ViewModels;
using Cinedex.Core.Localization;
using Cinedex.Core.Titles;

namespace Cinedex.Android.Views;

public sealed partial class MainView : UserControl
{
    public MainView() => InitializeComponent();

    private CatalogViewModel? ViewModel => DataContext as CatalogViewModel;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Bouton Retour d'Android : ferme la fiche avant de quitter l'application.
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            top.BackRequested += (_, args) =>
            {
                if (ViewModel is { HasDetail: true } vm)
                {
                    vm.BackCommand.Execute(null);
                    args.Handled = true;
                }
            };
        }
    }

    private void OnTitleChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (TitleList.SelectedItem is TitleSummary title)
        {
            ViewModel?.OpenCommand.Execute(title);
            TitleList.SelectedItem = null;
        }
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Tr.T("Sauvegarde Cinedex (.zip)"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Tr.T("Sauvegarde Cinedex (.zip)")) { Patterns = ["*.zip"], MimeTypes = ["application/zip"] }],
        });
        if (files.Count == 0)
        {
            return;
        }

        // Copie locale d'abord : le flux du sélecteur Android ne permet pas toujours de se positionner dans l'archive.
        var local = Path.Combine(Path.GetTempPath(), "cinedex-import.zip");
        await using (var source = await files[0].OpenReadAsync())
        await using (var copy = File.Create(local))
        {
            await source.CopyToAsync(copy);
        }

        await using (var archive = File.OpenRead(local))
        {
            vm.Import(archive);
        }

        File.Delete(local);
    }
}
