using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Cinedex.App.Views;

/// <summary>Boîte de message ou de confirmation simple, construite en code.</summary>
internal static class MessageWindow
{
    public static async Task<bool> ShowAsync(Window owner, string title, string message, bool confirm, string? okLabel = null, string? cancelLabel = null)
    {
        var ok = new Button { Content = okLabel ?? (confirm ? "Confirmer" : "Fermer"), IsDefault = true, Classes = { "primary" } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Token("Space2", 8d), HorizontalAlignment = HorizontalAlignment.Right };
        var window = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = Token("WindowPadding", new Thickness(24)),
                Spacing = Token("Space4", 16d),
                Children =
                {
                    new SelectableTextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    buttons,
                },
            },
        };
        if (confirm)
        {
            var cancel = new Button { Content = cancelLabel ?? "Annuler", IsCancel = true, Classes = { "tool" } };
            cancel.Click += (_, _) => window.Close(false);
            buttons.Children.Add(cancel);
        }

        ok.Click += (_, _) => window.Close(true);
        buttons.Children.Add(ok);
        return await window.ShowDialog<bool>(owner);
    }

    /// <summary>Jeton de Styles/Tokens.axaml (même source que les vues XAML) ; la valeur de repli sert hors application.</summary>
    private static T Token<T>(string key, T fallback) =>
        Application.Current?.TryFindResource(key, out var value) == true && value is T token ? token : fallback;
}
