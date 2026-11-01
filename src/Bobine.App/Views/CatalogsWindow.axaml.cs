using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Bobine.App.ViewModels;

namespace Bobine.App.Views;

public sealed partial class CatalogsWindow : Window
{
    public CatalogsWindow() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is CatalogsViewModel vm)
        {
            // Un autre catalogue vient d'être ouvert : la fenêtre principale est rechargée, celle-ci se ferme.
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(CatalogsViewModel.Switched) && vm.Switched)
                {
                    Close();
                }
            };
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
