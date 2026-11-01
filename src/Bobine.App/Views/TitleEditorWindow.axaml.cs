using Avalonia.Controls;
using Avalonia.Interactivity;
using Bobine.App.ViewModels;

namespace Bobine.App.Views;

public sealed partial class TitleEditorWindow : Window
{
    public TitleEditorWindow() => InitializeComponent();

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        // Ne ferme la fenêtre que si la saisie est valide ; sinon l'erreur s'affiche en bas.
        if (DataContext is TitleEditorViewModel editor && editor.TryBuild(out _))
        {
            Close(true);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
