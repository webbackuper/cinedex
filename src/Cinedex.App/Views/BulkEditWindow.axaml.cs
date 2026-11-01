using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Cinedex.App.Views;

public sealed partial class BulkEditWindow : Window
{
    public BulkEditWindow() => InitializeComponent();

    private void OnApply(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
