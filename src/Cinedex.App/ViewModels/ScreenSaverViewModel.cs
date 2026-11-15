using CommunityToolkit.Mvvm.ComponentModel;

namespace Cinedex.App.ViewModels;

/// <summary>Une affiche du diaporama, avec ses chemins absolus.</summary>
public sealed record ScreenSaverSlide(long TitleId, string Title, int? Year, string PosterPath, string? BackdropPath)
{
    /// <summary>Fond flouté : le décor du film, sinon son affiche.</summary>
    public string BackgroundPath => BackdropPath ?? PosterPath;

    public string YearText => Year?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
}

/// <summary>
/// Économiseur d'écran : les affiches du catalogue dans un ordre aléatoire, chacune affichée l'intervalle choisi ;
/// une fois toutes passées, un nouveau tirage recommence.
/// </summary>
public sealed partial class ScreenSaverViewModel : ObservableObject
{
    private readonly IReadOnlyList<ScreenSaverSlide> _slides;
    private readonly Random _random;
    private List<ScreenSaverSlide> _order = [];
    private int _position;

    public ScreenSaverViewModel(IReadOnlyList<ScreenSaverSlide> slides, TimeSpan interval, bool showTitles, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(slides);
        ArgumentOutOfRangeException.ThrowIfZero(slides.Count);
        _slides = slides;
        _random = random ?? Random.Shared;
        Interval = interval;
        ShowTitles = showTitles;
        Next();
    }

    public TimeSpan Interval { get; }

    public bool ShowTitles { get; }

    [ObservableProperty]
    public partial ScreenSaverSlide? Current { get; private set; }

    /// <summary>Affiche suivante ; jamais deux fois la même d'affilée quand il y en a plusieurs.</summary>
    /// <summary>
    /// La diapositive que <see cref="Next"/> montrera. La fenêtre s'en sert pour décoder ses images avant le
    /// changement, hors du fil d'interface : la transition n'attend alors plus le décodage.
    /// </summary>
    public ScreenSaverSlide Upcoming
    {
        get
        {
            EnsureOrder();
            return _order[_position];
        }
    }

    public void Next()
    {
        EnsureOrder();
        Current = _order[_position++];
    }

    private void EnsureOrder()
    {
        if (_position < _order.Count)
        {
            return;
        }

        var previous = Current;
        _order = [.. _slides.OrderBy(_ => _random.Next())];
        if (_order.Count > 1 && ReferenceEquals(_order[0], previous))
        {
            (_order[0], _order[^1]) = (_order[^1], _order[0]);
        }

        _position = 0;
    }
}
