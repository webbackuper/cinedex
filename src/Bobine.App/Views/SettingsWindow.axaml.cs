using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Bobine.App.ViewModels;

namespace Bobine.App.Views;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel settings)
        {
            try
            {
                if (settings.Save())
                {
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
