using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.Core.Metadata;
using Cinedex.Core.Localization;

namespace Cinedex.App.Views;

public sealed partial class MainWindow : Window, IDialogs, IApiLimitPrompt
{
    private string? _dataRoot;
    private WindowState _beforeFullScreen = WindowState.Maximized;
    private readonly IdleMonitor _idle = new();
    private readonly Avalonia.Threading.DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Avalonia.Threading.DispatcherTimer _backupTimer = new() { Interval = TimeSpan.FromMinutes(15) };

    public MainWindow()
    {
        InitializeComponent();
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.F11),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(ToggleFullScreen),
        });
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.F, KeyModifiers.Control),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => SearchBox.Focus()),
        });
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.A, KeyModifiers.Alt),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
            {
                AddButton.Flyout?.ShowAt(AddButton);
                NewTitleBox.Focus();
            }),
        });

        // Économiseur d'écran : toute action au clavier ou à la souris dans la fenêtre remet l'inactivité à zéro,
        // y compris quand un contrôle a déjà traité l'événement.
        AddHandler(KeyDownEvent, (_, _) => _idle.Touch(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, (_, _) => _idle.Touch(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, _) => _idle.Touch(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, (_, _) => _idle.Touch(), RoutingStrategies.Tunnel, handledEventsToo: true);
        Activated += (_, _) => _idle.Touch();
        _idleTimer.Tick += (_, _) => StartScreenSaverIfIdle();
        Opened += (_, _) => _idleTimer.Start();
        Closed += (_, _) => _idleTimer.Stop();

        // Sauvegarde planifiée : vérifiée à l'ouverture, puis toutes les 15 minutes.
        _backupTimer.Tick += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                await vm.RunScheduledBackupAsync(atStartup: false);
            }
        };
        Opened += async (_, _) =>
        {
            _backupTimer.Start();
            if (DataContext is MainWindowViewModel vm)
            {
                await vm.RunScheduledBackupAsync(atStartup: true);
            }
        };
        Closed += (_, _) => _backupTimer.Stop();

        // Largeur de la fiche : appliquée depuis le catalogue, et bornée pour laisser au moins 380 px à la liste.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                ApplyDetailWidth(vm.DetailWidth);
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainWindowViewModel.DetailWidth))
                    {
                        ApplyDetailWidth(vm.DetailWidth);
                    }
                };
            }
        };
        SizeChanged += (_, _) => LimitDetailWidth();
        PosterGrid.SizeChanged += (_, e) => FitPosters(e.NewSize.Width);
    }

    // Affiche de base : carte de 172 × 300 px, image de 156 × 234 px (rapport 2:3), 8 px entre les cartes.
    private const double PosterMinWidth = 172;
    private const double PosterMinHeight = 300;
    private const double PosterImageMinHeight = 234;
    private const double PosterSpacing = 8;

    private Avalonia.Layout.UniformGridLayout PosterLayout => (Avalonia.Layout.UniformGridLayout)PosterGrid.Layout!;

    /// <summary>
    /// Autant de colonnes que la largeur en permet avec des cartes d'au moins 172 px, puis les cartes s'élargissent
    /// pour remplir la rangée ; l'image garde son rapport 2:3, donc la carte grandit aussi en hauteur.
    /// </summary>
    private void FitPosters(double width)
    {
        if (width <= 0)
        {
            return;
        }

        var columns = Math.Max(1, (int)((width + PosterSpacing) / (PosterMinWidth + PosterSpacing)));
        // Un demi-pixel de marge : la grille doit retrouver exactement le même nombre de colonnes.
        var cardWidth = Math.Floor((width - (PosterSpacing * (columns - 1))) / columns) - 0.5;
        cardWidth = Math.Max(PosterMinWidth, cardWidth);
        var extra = (cardWidth - PosterMinWidth) * 1.5;
        if (Math.Abs(cardWidth - PosterLayout.MinItemWidth) < 0.1)
        {
            return;
        }

        PosterGrid.Resources["PosterCardWidth"] = cardWidth;
        PosterGrid.Resources["PosterImageHeight"] = PosterImageMinHeight + extra;
        PosterLayout.MinItemWidth = cardWidth;
        PosterLayout.MinItemHeight = PosterMinHeight + extra;
    }

    /// <summary>Colonne de la fiche dans la grille principale (la dernière).</summary>
    private ColumnDefinition DetailColumn => MainGrid.ColumnDefinitions[^1];

    private void ApplyDetailWidth(double width)
    {
        DetailColumn.Width = new GridLength(width);
        LimitDetailWidth();
    }

    /// <summary>La fiche ne peut pas manger la liste : barre latérale, 380 px de liste et poignée restent visibles.</summary>
    private void LimitDetailWidth()
    {
        var room = Bounds.Width - 232 - 380 - 8;
        DetailColumn.MaxWidth = room > 0 ? Math.Clamp(room, MainWindowViewModel.MinDetailWidth, MainWindowViewModel.MaxDetailWidth) : MainWindowViewModel.MaxDetailWidth;
    }

    private void OnDetailSplitterDragCompleted(object? sender, Avalonia.Input.VectorEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.DetailWidth = Math.Round(DetailColumn.ActualWidth);
        }
    }

    private void OnDetailSplitterDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.DetailWidth = MainWindowViewModel.DefaultDetailWidth;
        }
    }

    /// <summary>Lance le diaporama des affiches si la fenêtre est active et inactive depuis la durée réglée.</summary>
    private void StartScreenSaverIfIdle()
    {
        if (DataContext is not MainWindowViewModel vm || !_idle.ShouldStart(vm.ScreenSaver, IsActive))
        {
            return;
        }

        if (vm.CreateScreenSaver() is not { } slideshow)
        {
            _idle.Touch(); // Aucune affiche : on réessaiera après une nouvelle période d'inactivité.
            return;
        }

        _idle.IsShowing = true;
        var saver = new ScreenSaverWindow { DataContext = slideshow };
        saver.Closed += (_, _) =>
        {
            _idle.IsShowing = false;
            _idle.Touch();
            Activate();
        };
        saver.Show(this);
    }

    /// <summary>
    /// Taille au lancement selon les réglages : agrandie à l'écran (par défaut), plein écran, ou comme à la dernière
    /// fermeture si cette position est encore sur un écran branché. À appeler avant l'affichage de la fenêtre.
    /// </summary>
    public void ApplyStartupPlacement(string dataRoot)
    {
        _dataRoot = dataRoot;
        var placement = WindowPlacement.Load(dataRoot);
        switch (placement.Mode)
        {
            case StartupWindowMode.FullScreen:
                WindowState = WindowState.FullScreen;
                break;
            case StartupWindowMode.LastSize when placement is { Width: > 0, Height: > 0, X: { } x, Y: { } y }:
                WindowStartupLocation = WindowStartupLocation.Manual;
                Width = Math.Max(MinWidth, placement.Width.Value);
                Height = Math.Max(MinHeight, placement.Height.Value);
                Position = new PixelPoint(x, y);
                Opened += (_, _) =>
                {
                    // Écran débranché depuis : on revient au centre de l'écran principal.
                    if (Screens.All.All(s => !s.WorkingArea.Contains(new PixelPoint(x + 40, y + 20))) && Screens.Primary is { } screen)
                    {
                        Position = new PixelPoint(
                            screen.WorkingArea.X + Math.Max(0, (screen.WorkingArea.Width - (int)(Width * screen.Scaling)) / 2),
                            screen.WorkingArea.Y + Math.Max(0, (screen.WorkingArea.Height - (int)(Height * screen.Scaling)) / 2));
                    }
                };
                WindowState = placement.WasMaximized ? WindowState.Maximized : WindowState.Normal;
                break;
            default:
                WindowState = WindowState.Maximized;
                break;
        }

        Closing += (_, _) => RememberPlacement();
    }

    private void RememberPlacement()
    {
        if (_dataRoot is null)
        {
            return;
        }

        var previous = WindowPlacement.Load(_dataRoot);
        var normal = WindowState == WindowState.Normal;
        (previous with
        {
            WasMaximized = WindowState != WindowState.Normal,
            X = normal ? Position.X : previous.X,
            Y = normal ? Position.Y : previous.Y,
            Width = normal ? Width : previous.Width,
            Height = normal ? Height : previous.Height,
        }).Save(_dataRoot);
    }

    /// <summary>F11 : plein écran, puis retour à l'état précédent.</summary>
    private void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = _beforeFullScreen;
        }
        else
        {
            _beforeFullScreen = WindowState;
            WindowState = WindowState.FullScreen;
        }
    }

    public async Task<bool> EditTitleAsync(TitleEditorViewModel editor)
    {
        var window = new TitleEditorWindow { DataContext = editor };
        return await window.ShowDialog<bool>(this);
    }

    public Task<bool> ConfirmAsync(string title, string message) => MessageWindow.ShowAsync(this, title, message, confirm: true);

    public Task InformAsync(string title, string message) => MessageWindow.ShowAsync(this, title, message, confirm: false);

    public Task<MetadataSearchResult?> SearchMetadataAsync(MetadataSearchViewModel search)
    {
        AddButton.Flyout?.Hide();
        return new MetadataSearchWindow { DataContext = search }.ShowDialog<MetadataSearchResult?>(this);
    }

    public Task<bool> EditSettingsAsync(SettingsViewModel settings) =>
        new SettingsWindow { DataContext = settings }.ShowDialog<bool>(this);

    public Task<bool> RuleSearchAsync(RuleSearchViewModel search) =>
        new RuleSearchWindow { DataContext = search }.ShowDialog<bool>(this);

    public Task<bool> BulkEditAsync(BulkEditViewModel edit) =>
        new BulkEditWindow { DataContext = edit }.ShowDialog<bool>(this);

    public Task<bool> ImportFolderAsync(ImportFolderViewModel import) =>
        new ImportFolderWindow { DataContext = import }.ShowDialog<bool>(this);

    public Task ManageCatalogsAsync(CatalogsViewModel catalogs) =>
        new CatalogsWindow { DataContext = catalogs }.ShowDialog(this);

    /// <summary>Limite d'API atteinte pendant un import : continuer sans elle ou interrompre.</summary>
    public Task<bool> ContinueWithoutAsync(ExternalApi api, string reason, string consequence)
    {
        var name = api switch
        {
            ExternalApi.Tmdb => "TMDb",
            ExternalApi.Omdb => "OMDb",
            _ => "le fichier des notes IMDb",
        };
        return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => MessageWindow.ShowAsync(
            this,
            Tr.F("Limite atteinte : {0}", name),
            Tr.F("{0}\n\nSi vous continuez sans {1} : {2}", reason, name, consequence),
            confirm: true,
            okLabel: Tr.F("Continuer sans {0}", name),
            cancelLabel: "Interrompre"));
    }

    public Task<bool> ShowSeriesAsync(SeriesViewModel series) =>
        new SeriesWindow { DataContext = series }.ShowDialog<bool>(this);

    public Task<bool> ShowPersonAsync(PersonViewModel person) =>
        new PersonWindow { DataContext = person }.ShowDialog<bool>(this);

    private HelpWindow? _help;

    public void ShowHelp(HelpViewModel help)
    {
        if (_help is { IsVisible: true })
        {
            _help.Activate();
            return;
        }

        _help = new HelpWindow { DataContext = help };
        _help.Closed += (_, _) => _help = null;
        _help.Show(this);
    }

    public Task<bool> ShowMatchReviewAsync(MatchReviewViewModel review) =>
        new MatchReviewWindow { DataContext = review }.ShowDialog<bool>(this);

    public Task<bool> ShowSeriesTrackingAsync(SeriesTrackingViewModel tracking) =>
        new SeriesTrackingWindow { DataContext = tracking }.ShowDialog<bool>(this);

    public Task<bool> ShowCollectionsAsync(CollectionsViewModel collections) =>
        new CollectionsWindow { DataContext = collections }.ShowDialog<bool>(this);

    public Task<bool> ShowPeopleAsync(PeopleBrowserViewModel people) =>
        new PeopleWindow { DataContext = people }.ShowDialog<bool>(this);

    /// <summary>Clic sur une affiche : sélection (Ctrl pour ajouter ou retirer, Maj pour une plage).</summary>
    private void OnPosterPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || sender is not Control { DataContext: Cinedex.Core.Titles.TitleSummary title } ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var toggle = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        vm.SelectInGrid(vm.Titles.IndexOf(title), toggle, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        PosterScroll.Focus();
    }

    /// <summary>
    /// Clavier dans la grille d'affiches : flèches (Maj pour étendre), Début, Fin, pages, Entrée pour ouvrir la fiche,
    /// Suppr pour supprimer, Ctrl+A pour tout sélectionner.
    /// </summary>
    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var columns = Math.Max(1, (int)((PosterGrid.Bounds.Width + PosterSpacing) / (PosterLayout.MinItemWidth + PosterSpacing)));
        var rows = Math.Max(1, (int)(PosterScroll.Viewport.Height / (PosterLayout.MinItemHeight + PosterSpacing)));
        var extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        int? delta = e.Key switch
        {
            Key.Left => -1,
            Key.Right => 1,
            Key.Up => -columns,
            Key.Down => columns,
            Key.PageUp => -columns * rows,
            Key.PageDown => columns * rows,
            Key.Home => int.MinValue / 2,
            Key.End => int.MaxValue / 2,
            _ => null,
        };
        if (delta is { } move)
        {
            var index = vm.MoveInGrid(move, extend);
            if (index >= 0 && PosterGrid.GetOrCreateElement(index) is { } element)
            {
                element.BringIntoView();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Enter && vm.EditCommand.CanExecute(null))
        {
            vm.EditCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && vm.DeleteCommand.CanExecute(null))
        {
            vm.DeleteCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.SelectInGrid(0, toggle: false, extend: false);
            vm.SelectInGrid(vm.Titles.Count - 1, toggle: false, extend: true);
            e.Handled = true;
        }
    }

    private void OnListDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.EditCommand.CanExecute(null))
        {
            vm.EditCommand.Execute(null);
        }
    }
}
