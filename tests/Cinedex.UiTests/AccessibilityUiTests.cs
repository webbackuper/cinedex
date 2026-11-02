using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.App.Views;
using Cinedex.Data;

namespace Cinedex.UiTests;

/// <summary>
/// Accessibilité vérifiée sur les vraies fenêtres : chaque bouton a un nom lu par le lecteur d'écran, les couleurs
/// du texte sont assez contrastées en thème clair et sombre, et l'essentiel se fait au clavier.
/// </summary>
public sealed class AccessibilityUiTests(UiSession ui)
{
    private static readonly string[] Surfaces = ["CinedexBackground", "CinedexSurface"];

    /// <summary>Boutons (y compris bascules et cases) sans nom accessible : ni texte, ni AutomationProperties.Name.</summary>
    private static List<string> Unnamed(Window window) =>
    [
        .. window.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c is Button or ToggleButton && c.IsEffectivelyVisible)
            .Where(c => ControlAutomationPeer.CreatePeerForElement(c).GetName() is not { } name ||
                        string.IsNullOrWhiteSpace(name) || name.StartsWith("Avalonia.", StringComparison.Ordinal) || name.StartsWith("Cinedex.", StringComparison.Ordinal))
            .Select(c => $"{c.GetType().Name} {c.Name} ({ToolTip.GetTip(c)}) {(c as ContentControl)?.Content}"),
    ];

    [Fact]
    public Task Every_button_of_the_main_window_has_an_accessible_name() => ui.Run(() =>
    {
        using var catalog = new UiCatalog(50);
        catalog.ViewModel.SelectedTitles.Add(catalog.ViewModel.Titles[0]);
        UiCatalog.Pump(100);
        Assert.Empty(Unnamed(catalog.Window));
        catalog.ViewModel.IsGridView = true;
        UiCatalog.Pump(100);
        Assert.Empty(Unnamed(catalog.Window));
    });

    [Fact]
    public Task Every_button_of_the_settings_has_an_accessible_name() => ui.Run(() =>
    {
        using var catalog = new UiCatalog(10);
        using var temp = new Cinedex.Tests.TempDirectory();
        var settings = new SettingsWindow
        {
            DataContext = new SettingsViewModel(new MemorySecrets(), new SettingsStore(catalog.Database), temp.Path, new StatisticsRepository(catalog.Database).Compute())
            {
                Maintenance = new MaintenanceViewModel(new MaintenanceRepository(catalog.Database), new CatalogManager(new Cinedex.Core.AppPaths(temp.Path, true)), "tests", temp.Path, temp.Path),
                Export = new ExportViewModel(new ExportRepository(catalog.Database), new SettingsStore(catalog.Database)),
                Backups = new BackupsViewModel(new BackupService(new MaintenanceRepository(catalog.Database), new SettingsStore(catalog.Database), "tests", temp.Path, temp.Path, temp.Path)),
            },
        };
        settings.Show();
        var tabs = settings.GetVisualDescendants().OfType<TabControl>().First();
        var problems = new List<string>();
        for (var i = 0; i < tabs.ItemCount; i++)
        {
            tabs.SelectedIndex = i;
            UiCatalog.Pump(50);
            problems.AddRange(Unnamed(settings).Select(p => $"onglet {i} : {p}"));
        }

        settings.Close();
        Assert.Empty(problems);
    });

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task Text_colors_are_contrasted_enough(string variant) => ui.Run(() =>
    {
        var theme = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var app = Avalonia.Application.Current!;
        Color Get(string key) => app.TryGetResource(key, theme, out var value) && value is ISolidColorBrush brush ? brush.Color : throw new InvalidOperationException(key);

        foreach (var accent in Enum.GetValues<AccentColor>())
        {
            Appearance.ApplyStyle(new AppearanceStyle { Accent = accent });
            foreach (var surface in Surfaces)
            {
                // Texte courant : 4,5:1 (WCAG AA) ; texte secondaire et accent (titres, liens, notes) : 3:1 au moins.
                Assert.True(Contrast(Get("CinedexInk"), Get(surface)) >= 4.5, $"{variant} encre / {surface}");
                Assert.True(Contrast(Get("CinedexMuted"), Get(surface)) >= 3, $"{variant} texte secondaire / {surface}");
                Assert.True(Contrast(Get("CinedexAccent"), Get(surface)) >= 3, $"{variant} accent {accent} / {surface}");
            }

            foreach (var menu in Enum.GetValues<MenuColor>())
            {
                Appearance.ApplyStyle(new AppearanceStyle { Accent = accent, Menu = menu });
                Assert.True(Contrast(Get("CinedexInk"), Get("CinedexSidebar")) >= 4.5, $"{variant} encre / menus {menu}");
                Assert.True(Contrast(Get("CinedexAccent"), Get("CinedexSidebar")) >= 3, $"{variant} accent {accent} / menus {menu}");
            }
        }

        Appearance.ApplyStyle(new AppearanceStyle());
    });

    [Fact]
    public Task The_main_actions_work_from_the_keyboard() => ui.RunAsync(async () =>
    {
        using var catalog = new UiCatalog(30);
        var vm = catalog.ViewModel;

        // Ctrl+2 / Ctrl+1 : affiches puis liste.
        catalog.Window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Control);
        UiCatalog.Pump();
        Assert.True(vm.IsGridView);
        catalog.Window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Control);
        UiCatalog.Pump();
        Assert.False(vm.IsGridView);

        // Dans la liste : flèche bas sélectionne, Tab ne reste pas bloqué.
        var list = catalog.Find<ListBox>(l => l.Classes.Contains("titles"));
        list.Focus();
        catalog.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        catalog.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        await UiCatalog.Wait(50);
        Assert.Single(vm.SelectedTitles);
        Assert.NotNull(vm.Detail);

        var before = catalog.Window.FocusManager?.GetFocusedElement();
        catalog.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        UiCatalog.Pump();
        Assert.NotSame(before, catalog.Window.FocusManager?.GetFocusedElement());
    });

    /// <summary>Rapport de contraste WCAG entre deux couleurs opaques.</summary>
    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte c)
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color c) => (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
        var (l1, l2) = (Luminance(a), Luminance(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    private sealed class MemorySecrets : Cinedex.App.Security.ISecretStore
    {
        private readonly Dictionary<string, string> _values = [];

        public string? Read(string key) => _values.GetValueOrDefault(key);

        public void Write(string key, string value) => _values[key] = value;

        public void Delete(string key) => _values.Remove(key);
    }
}
