using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Cinedex.App.ViewModels;

namespace Cinedex.App.Views;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    /// <summary>Ouvre directement l'onglet Statistiques (le dernier).</summary>
    public void ShowStatistics() => Tabs.SelectedIndex = Tabs.ItemCount - 1;

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel settings)
        {
            try
            {
                if (settings.Save())
                {
                    // Nouvelle langue : elle s'applique au redémarrage, proposé tout de suite.
                    if (settings.LanguageChanged &&
                        await MessageWindow.ShowAsync(this, "Langue de l'interface", "Redémarrer Cinedex maintenant pour changer de langue ?", confirm: true) &&
                        Environment.ProcessPath is { } exe)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false })?.Dispose();
                        (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
                        return;
                    }

                    Close(true);
                }
            }
            catch (InvalidOperationException ex)
            {
                settings.Error = ex.Message;
            }
        }
    }

    private void OnResetNoiseWords(object? sender, RoutedEventArgs e) => (DataContext as SettingsViewModel)?.ResetNoiseWords();

    /// <summary>Copie le journal des actions du jour à l'endroit choisi.</summary>
    private async void OnExportJournal(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Cinedex.Core.Localization.Tr.T("Exporter le journal du jour…"),
            SuggestedFileName = Path.GetFileName(Services.ActivityJournal.Current.TodayPath),
            DefaultExtension = "log",
        });
        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            var lines = Services.ActivityJournal.Current.Export(path);
            vm.JournalMessage = Cinedex.Core.Localization.Tr.F("{0} ligne(s) exportée(s) vers {1}", lines, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            vm.JournalMessage = ex.Message;
        }
    }

    private void OnOpenJournalFolder(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SettingsViewModel.JournalFolder);
        _ = Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(SettingsViewModel.JournalFolder));
    }

    private void OnResetExtensions(object? sender, RoutedEventArgs e) => (DataContext as SettingsViewModel)?.ResetExtensions();

    /// <summary>Ouvre le dossier des données dans l'explorateur de fichiers du système.</summary>
    private void OnOpenData(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel { DataLocation: var folder } && Directory.Exists(folder))
        {
            _ = Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
        }
    }

    private void OnForget(object? sender, RoutedEventArgs e) => (DataContext as SettingsViewModel)?.ForgetToken();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
