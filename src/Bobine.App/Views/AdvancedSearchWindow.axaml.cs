using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Bobine.App.Views;

public sealed partial class AdvancedSearchWindow : Window
{
    public AdvancedSearchWindow() => InitializeComponent();

    private void OnSearch(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
