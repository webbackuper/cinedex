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
        _timer.Tick += async (_, _) => await AdvanceAsync();
    }

    /// <summary>Largeurs de décodage de la diapositive, les mêmes que dans la vue : le cache se retrouve par elles.</summary>
    private const int BackgroundWidth = 960;

    private const int PosterWidth = 900;

    private bool _advancing;

    /// <summary>
    /// Passe à la diapositive suivante une fois ses deux images décodées. Le décodage d'une affiche en 900 px
    /// prend quelques dizaines de millisecondes : fait sur le fil d'interface, il saccadait chaque transition.
    /// </summary>
    private async Task AdvanceAsync()
    {
        if (_advancing || DataContext is not ScreenSaverViewModel vm)
        {
            return;
        }

        _advancing = true;
        try
        {
            var next = vm.Upcoming;
            await Task.Run(() =>
            {
                PathToBitmapConverter.Load(next.BackgroundPath, BackgroundWidth);
                PathToBitmapConverter.Load(next.PosterPath, PosterWidth);
            });
            vm.Next();
        }
        finally
        {
            _advancing = false;
        }
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
