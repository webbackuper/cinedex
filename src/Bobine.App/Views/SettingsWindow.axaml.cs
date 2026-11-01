using Avalonia.Controls;
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

    private void OnForget(object? sender, RoutedEventArgs e) => (DataContext as SettingsViewModel)?.ForgetToken();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
