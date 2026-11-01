using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Bobine.App.ViewModels;

namespace Bobine.App.Views;

public sealed partial class MainWindow : Window, IDialogs
{
    public MainWindow()
    {
        InitializeComponent();
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.F, KeyModifiers.Control),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => SearchBox.Focus()),
        });
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.A, KeyModifiers.Alt),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => NewTitleBox.Focus()),
        });
    }

    public async Task<bool> EditTitleAsync(TitleEditorViewModel editor)
    {
        var window = new TitleEditorWindow { DataContext = editor };
        return await window.ShowDialog<bool>(this);
    }

    public Task<bool> ConfirmAsync(string title, string message) => MessageWindow.ShowAsync(this, title, message, confirm: true);

    public Task InformAsync(string title, string message) => MessageWindow.ShowAsync(this, title, message, confirm: false);

    private void OnListDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.EditCommand.CanExecute(null))
        {
            vm.EditCommand.Execute(null);
        }
    }
}
