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

    public Task<bool> ShowPeopleAsync(PeopleBrowserViewModel people) =>
        new PeopleWindow { DataContext = people }.ShowDialog<bool>(this);

    private void OnListDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.EditCommand.CanExecute(null))
        {
            vm.EditCommand.Execute(null);
        }
    }
}
