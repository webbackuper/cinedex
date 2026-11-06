using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Cinedex.App.ViewModels;
using Cinedex.Core.Localization;
using Cinedex.Core.People;
using Cinedex.Data;
using Cinedex.App.Services;

namespace Cinedex.App.Views;

public sealed partial class PersonWindow : Window
{
    public PersonWindow() => InitializeComponent();

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is PersonViewModel vm)
        {
            await vm.LoadDetailsAsync();
        }
    }

    private void OnTitleChosen(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PersonViewModel vm && sender is ListBox { SelectedItem: PersonCredit credit })
        {
            vm.Choose(credit);
            Close(true);
        }
    }

    private void OnOpenTmdb(object? sender, RoutedEventArgs e) => WebLinks.Open((DataContext as PersonViewModel)?.TmdbUrl);

    private void OnOpenImdb(object? sender, RoutedEventArgs e) => WebLinks.Open((DataContext as PersonViewModel)?.ImdbUrl);

    private void OnClose(object? sender, RoutedEventArgs e) => Close(false);

    /// <summary>Enregistre la filmographie affichée : le format suit l'extension choisie, Excel par défaut.</summary>
    private async void OnExportFilmography(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PersonViewModel vm)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exporter la filmographie",
            SuggestedFileName = vm.ExportFileName,
            DefaultExtension = "xlsx",
            FileTypeChoices =
            [
                new FilePickerFileType("Excel (.xlsx)") { Patterns = ["*.xlsx"] },
                new FilePickerFileType("CSV (Excel, LibreOffice…)") { Patterns = ["*.csv"] },
                new FilePickerFileType("TSV (texte séparé par des tabulations)") { Patterns = ["*.tsv"] },
            ],
        });
        if (file?.TryGetLocalPath() is not { Length: > 0 } path)
        {
            return;
        }

        try
        {
            var format = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".csv" => ExportFormat.Csv,
                ".tsv" => ExportFormat.Tsv,
                _ => ExportFormat.Xlsx,
            };
            var count = vm.ExportTo(path, format);
            vm.ExportMessage = Tr.F("{0} titre(s) exporté(s) vers {1}", count, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            vm.ExportMessage = ex.Message;
        }
    }
}
