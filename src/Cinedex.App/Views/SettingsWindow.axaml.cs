using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Cinedex.App.ViewModels;

namespace Cinedex.App.Views;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    /// <summary>Branche l'onglet Maintenance : confirmations et fermeture demandée (renommage, titre à afficher).</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SettingsViewModel vm)
        {
            vm.CloseRequested += (_, _) => Close(false);
        }

        if (DataContext is SettingsViewModel { Maintenance: { } maintenance })
        {
            maintenance.Confirm = (title, message) => MessageWindow.ShowAsync(this, title, message, confirm: true);
            maintenance.CloseRequested += (_, _) => Close(false);
        }

        if (DataContext is SettingsViewModel { Backups: { } backups })
        {
            backups.Confirm = (title, message) => MessageWindow.ShowAsync(this, title, message, confirm: true);
            backups.CloseRequested += (_, _) => Close(false);
        }
    }

    private void OnExportAll(object? sender, RoutedEventArgs e) => (DataContext as SettingsViewModel)?.Export?.SelectAll(true);

    private void OnExportNone(object? sender, RoutedEventArgs e) => (DataContext as SettingsViewModel)?.Export?.SelectAll(false);

    /// <summary>Choix du fichier, puis export de la liste dans le format choisi.</summary>
    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel { Export: { } export })
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Cinedex.Core.Localization.Tr.T("Exporter la liste des films"),
            SuggestedFileName = export.SuggestedFileName,
            DefaultExtension = export.Extension.TrimStart('.'),
            FileTypeChoices = [new FilePickerFileType(export.SelectedFormat.Label) { Patterns = ["*" + export.Extension] }],
        });
        if (file?.TryGetLocalPath() is { } path)
        {
            await export.ExportAsync(path);
        }
    }

    private async void OnChooseBackupFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel { Backups: { } backups })
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Cinedex.Core.Localization.Tr.T("Dossier des sauvegardes"),
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            backups.Folder = path;
        }
    }

    private void OnOpenBackupFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel { Backups: { } backups } && !string.IsNullOrWhiteSpace(backups.Folder))
        {
            Directory.CreateDirectory(backups.Folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(backups.Folder) { UseShellExecute = true })?.Dispose();
        }
    }

    private void OnOpenBackups(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel { Maintenance: { } maintenance })
        {
            Directory.CreateDirectory(maintenance.BackupsDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(maintenance.BackupsDirectory) { UseShellExecute = true })?.Dispose();
        }
    }

    private void OnDuplicateChosen(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel { Maintenance: { SelectedDuplicate: { } row } maintenance })
        {
            maintenance.ShowDuplicate(row);
        }
    }

    /// <summary>Ouvre directement l'onglet Statistiques (le dernier).</summary>
    public void ShowStatistics() => Tabs.SelectedIndex = Tabs.ItemCount - 1;

    /// <summary>Onglet choisi par son rang (capture de vérification).</summary>
    public void ShowTab(int index) => Tabs.SelectedIndex = Math.Clamp(index, 0, Tabs.ItemCount - 1);

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
