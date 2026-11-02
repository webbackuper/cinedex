using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Cinedex.App.ViewModels;

namespace Cinedex.App.Views;

/// <summary>
/// Diaporama plein écran des affiches. La moindre action (touche, clic, molette, mouvement de souris
/// au-delà de quelques pixels) le ferme et rend la main à la fenêtre principale.
/// </summary>
public sealed partial class ScreenSaverWindow : Window
{
    private const double MoveTolerance = 12;

    private readonly DispatcherTimer _timer = new();
    private Point? _firstPointer;

    public ScreenSaverWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => (DataContext as ScreenSaverViewModel)?.Next();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is ScreenSaverViewModel vm)
        {
            _timer.Interval = vm.Interval;
            _timer.Start();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        Close();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        e.Handled = true;
        Close();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        e.Handled = true;
        Close();
    }

    /// <summary>Le premier mouvement signalé à l'ouverture sert de repère ; seul un vrai déplacement ferme le diaporama.</summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (_firstPointer is not { } first)
        {
            _firstPointer = position;
            return;
        }

        if (Math.Abs(position.X - first.X) > MoveTolerance || Math.Abs(position.Y - first.Y) > MoveTolerance)
        {
            Close();
        }
    }
}
