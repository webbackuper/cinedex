using Avalonia.Controls;
using Avalonia.Interactivity;
using Cinedex.App.ViewModels;

namespace Cinedex.App.Views;

public sealed partial class PeopleWindow : Window
{
    public PeopleWindow() => InitializeComponent();

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        QueryBox.Focus();
    }

    /// <summary>Ouvre la fiche de la personne ; si un de ses titres y est choisi, l'annuaire se ferme sur ce titre.</summary>
    private async void OnPersonChosen(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PeopleBrowserViewModel vm || vm.Selected is not { } person)
        {
            return;
        }

        var page = new PersonViewModel(vm.Service, person.Id, vm.Role);
        if (await new PersonWindow { DataContext = page }.ShowDialog<bool>(this) && page.ChosenTitleId is { } titleId)
        {
            vm.ChosenTitleId = titleId;
            Close(true);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(false);
}
