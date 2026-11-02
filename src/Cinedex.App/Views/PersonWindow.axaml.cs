using Avalonia.Controls;
using Avalonia.Interactivity;
using Cinedex.App.ViewModels;
using Cinedex.Core.People;

namespace Cinedex.App.Views;

public sealed partial class PersonWindow : Window
{
    public PersonWindow() => InitializeComponent();

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is PersonViewModel vm)
        {
            await vm.LoadDetailsAsync();
        }
    }

    private void OnTitleChosen(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PersonViewModel vm && sender is ListBox { SelectedItem: PersonCredit credit })
        {
            vm.Choose(credit);
            Close(true);
        }
    }

    private void OnOpenTmdb(object? sender, RoutedEventArgs e) => WebLinks.Open((DataContext as PersonViewModel)?.TmdbUrl);

    private void OnOpenImdb(object? sender, RoutedEventArgs e) => WebLinks.Open((DataContext as PersonViewModel)?.ImdbUrl);

    private void OnClose(object? sender, RoutedEventArgs e) => Close(false);
}
