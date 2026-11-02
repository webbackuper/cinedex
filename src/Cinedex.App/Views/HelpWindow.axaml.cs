using Avalonia.Controls;
using Avalonia.Input;
using Cinedex.App.ViewModels;

namespace Cinedex.App.Views;

/// <summary>Aide : fenêtre indépendante, qu'on peut garder ouverte à côté de Cinedex.</summary>
public sealed partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Escape), Command = new CommunityToolkit.Mvvm.Input.RelayCommand(Close) });
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.F, KeyModifiers.Control),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => SearchBox.Focus()),
        });
        DataContextChanged += (_, _) =>
        {
            if (DataContext is HelpViewModel vm)
            {
                // Nouvelle rubrique : lecture depuis le haut.
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(HelpViewModel.SelectedSection))
                    {
                        Reader.Offset = default;
                    }
                };
            }
        };
    }
}
