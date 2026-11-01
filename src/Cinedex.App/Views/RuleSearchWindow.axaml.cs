using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Cinedex.App.Views;

public sealed partial class RuleSearchWindow : Window
{
    public RuleSearchWindow() => InitializeComponent();

    private void OnSearch(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
