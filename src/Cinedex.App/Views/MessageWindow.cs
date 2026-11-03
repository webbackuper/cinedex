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
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var window = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
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
}
