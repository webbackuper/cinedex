using Avalonia.Controls;
using Avalonia.Interactivity;
using Cinedex.App.ViewModels;

namespace Cinedex.App.Views;

public sealed partial class SeriesTrackingWindow : Window
{
    public SeriesTrackingWindow() => InitializeComponent();

    /// <summary>Double-clic (ou Entrée) sur une série : la fenêtre se ferme et la série s'affiche dans le catalogue.</summary>
    private void OnOpenSeries(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SeriesTrackingViewModel { Selected: { } row } vm)
        {
            vm.Choose(row);
            Close(true);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(false);
}
