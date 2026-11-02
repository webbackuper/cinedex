using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Cinedex.App.ViewModels;

namespace Cinedex.App.Views;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

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
