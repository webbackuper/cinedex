using System.Diagnostics;
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

    private void OnOpenTmdb(object? sender, RoutedEventArgs e) => OpenUrl((DataContext as PersonViewModel)?.TmdbUrl);

    private void OnOpenImdb(object? sender, RoutedEventArgs e) => OpenUrl((DataContext as PersonViewModel)?.ImdbUrl);

    private void OnClose(object? sender, RoutedEventArgs e) => Close(false);

    /// <summary>Ouvre uniquement des liens construits par Cinedex vers TMDb ou IMDb (https).</summary>
    private static void OpenUrl(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            uri.Host is "www.themoviedb.org" or "www.imdb.com")
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
    }
}
