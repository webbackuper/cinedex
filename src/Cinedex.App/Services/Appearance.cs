using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Cinedex.App.Services;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Jeux d'icônes : même dessin, trois traitements, plus les icônes d'origine.</summary>
public enum IconSet
{
    /// <summary>Trait fin et arrondi (défaut).</summary>
    Fine,

    /// <summary>Trait épais, plus lisible sur grand écran.</summary>
    Bold,

    /// <summary>Trait fin et formes fermées teintées.</summary>
    Duotone,

    /// <summary>Icônes Buddha d'origine là où elles existent.</summary>
    Classic,
}

/// <summary>Couleur du bandeau gauche (menus).</summary>
public enum MenuColor
{
    Sand,
    Slate,
    Sage,
    Lavender,
    Rose,
    White,
}

/// <summary>Couleur d'accent : élément choisi, boutons principaux, notes.</summary>
public enum AccentColor
{
    TerraCotta,
    Blue,
    Green,
    Violet,
    Red,
    Graphite,
}

/// <summary>Style de l'interface : icônes, couleurs, police. Commun à tous les catalogues.</summary>
public sealed record AppearanceStyle
{
    public const string DefaultFont = "Inter";

    public IconSet Icons { get; init; }

    public MenuColor Menu { get; init; }

    public AccentColor Accent { get; init; }

    /// <summary>Nom de la police (« Inter » est fournie avec Cinedex, « Système » suit le système).</summary>
    public string Font { get; init; } = DefaultFont;

    /// <summary>Polices proposées : Inter (fournie), celle du système, puis des polices courantes.</summary>
    public static IReadOnlyList<string> Fonts { get; } = [DefaultFont, "Système", "Segoe UI", "Calibri", "Verdana", "Trebuchet MS", "Georgia", "Arial"];

    /// <summary>Couleurs du bandeau gauche en thème clair et sombre.</summary>
    public static (Color Light, Color Dark) MenuColors(MenuColor menu) => menu switch
    {
        MenuColor.Slate => (Color.Parse("#E3E7EC"), Color.Parse("#14171B")),
        MenuColor.Sage => (Color.Parse("#E2EADF"), Color.Parse("#131813")),
        MenuColor.Lavender => (Color.Parse("#E9E4F0"), Color.Parse("#17141C")),
        MenuColor.Rose => (Color.Parse("#F2E2E2"), Color.Parse("#1C1415")),
        MenuColor.White => (Color.Parse("#FFFFFF"), Color.Parse("#211E1B")),
        _ => (Color.Parse("#EEE8DF"), Color.Parse("#171513")),
    };

    /// <summary>Accent en thème clair et sombre (plus lumineux sur fond sombre).</summary>
    public static (Color Light, Color Dark) AccentColors(AccentColor accent) => accent switch
    {
        AccentColor.Blue => (Color.Parse("#2F6DAE"), Color.Parse("#6FA8E4")),
        AccentColor.Green => (Color.Parse("#2E7A4E"), Color.Parse("#63B585")),
        AccentColor.Violet => (Color.Parse("#6C4AAE"), Color.Parse("#A88CE6")),
        AccentColor.Red => (Color.Parse("#AE3646"), Color.Parse("#E27A87")),
        AccentColor.Graphite => (Color.Parse("#4B5058"), Color.Parse("#A9B0BA")),
        _ => (Color.Parse("#B4572C"), Color.Parse("#E2895A")),
    };

    /// <summary>Mélange de deux couleurs (part de la seconde entre 0 et 1).</summary>
    public static Color Mix(Color a, Color b, double amount) => Color.FromRgb(
        (byte)Math.Round(a.R + ((b.R - a.R) * amount)),
        (byte)Math.Round(a.G + ((b.G - a.G) * amount)),
        (byte)Math.Round(a.B + ((b.B - a.B) * amount)));
}

/// <summary>
/// Apparence de l'application : thème (clair, sombre ou comme le système), jeu d'icônes, couleur des menus,
/// accent et police. Réglage commun à tous les catalogues, mémorisé dans <c>appearance.json</c> à la racine des données.
/// </summary>
public static class Appearance
{
    private const string FileName = "appearance.json";

    public static AppTheme Load(string dataRoot) => Read(dataRoot) is { } state && Enum.IsDefined(state.Theme) ? state.Theme : AppTheme.System;

    public static void Save(string dataRoot, AppTheme theme) => Write(dataRoot, (Read(dataRoot) ?? new State()) with { Theme = theme });

    public static AppearanceStyle LoadStyle(string dataRoot)
    {
        var state = Read(dataRoot);
        return new AppearanceStyle
        {
            Icons = state?.Icons is { } icons && Enum.IsDefined(icons) ? icons : IconSet.Fine,
            Menu = state?.Menu is { } menu && Enum.IsDefined(menu) ? menu : MenuColor.Sand,
            Accent = state?.Accent is { } accent && Enum.IsDefined(accent) ? accent : AccentColor.TerraCotta,
            Font = state?.Font is { Length: > 0 } font && AppearanceStyle.Fonts.Contains(font) ? font : AppearanceStyle.DefaultFont,
        };
    }

    public static void SaveStyle(string dataRoot, AppearanceStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        Write(dataRoot, (Read(dataRoot) ?? new State()) with { Icons = style.Icons, Menu = style.Menu, Accent = style.Accent, Font = style.Font });
    }

    /// <summary>Change le thème de toutes les fenêtres ouvertes, immédiatement.</summary>
    public static void Apply(AppTheme theme)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }

    /// <summary>Applique icônes, couleurs et police à toutes les fenêtres ouvertes, immédiatement.</summary>
    public static void ApplyStyle(AppearanceStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        Controls.Icon.Set = style.Icons;
        if (Application.Current is not { } app)
        {
            return;
        }

        var (menuLight, menuDark) = AppearanceStyle.MenuColors(style.Menu);
        var (accentLight, accentDark) = AppearanceStyle.AccentColors(style.Accent);
        SetBrushes(app, ThemeVariant.Light, menuLight, accentLight, AppearanceStyle.Mix(accentLight, Colors.White, 0.84));
        SetBrushes(app, ThemeVariant.Dark, menuDark, accentDark, AppearanceStyle.Mix(accentDark, Color.Parse("#1C1A18"), 0.78));
        // Palette Fluent (cases à cocher, curseurs, sélection) : même accent.
        if (app.Styles.OfType<Avalonia.Themes.Fluent.FluentTheme>().FirstOrDefault() is { } fluent)
        {
            if (fluent.Palettes.TryGetValue(ThemeVariant.Light, out var light))
            {
                light.Accent = accentLight;
            }

            if (fluent.Palettes.TryGetValue(ThemeVariant.Dark, out var dark))
            {
                dark.Accent = accentDark;
            }
        }
        app.Resources["CinedexFont"] = style.Font switch
        {
            AppearanceStyle.DefaultFont => new FontFamily("fonts:Inter#Inter"),
            "Système" => FontFamily.Default,
            var name => new FontFamily(name),
        };

        // Les icônes déjà affichées se redessinent avec le nouveau jeu.
        if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var icon in desktop.Windows.SelectMany(w => w.GetVisualDescendants().OfType<Controls.Icon>()))
            {
                icon.InvalidateVisual();
            }
        }
    }

    private static void SetBrushes(Application app, ThemeVariant variant, Color menu, Color accent, Color accentSoft)
    {
        if (app.Resources.ThemeDictionaries.TryGetValue(variant, out var provider) && provider is ResourceDictionary dictionary)
        {
            SetBrush(dictionary, "CinedexSidebar", menu);
            SetBrush(dictionary, "CinedexAccent", accent);
            SetBrush(dictionary, "CinedexAccentSoft", accentSoft);
            // Contrôles Fluent (cases à cocher, sélections, curseurs) : même accent.
            dictionary["SystemAccentColor"] = accent;
            dictionary["SystemAccentColorDark1"] = AppearanceStyle.Mix(accent, Colors.Black, 0.12);
            dictionary["SystemAccentColorLight1"] = AppearanceStyle.Mix(accent, Colors.White, 0.12);
        }
    }

    /// <summary>Change la couleur du pinceau existant (les fenêtres ouvertes suivent), ou en crée un.</summary>
    private static void SetBrush(ResourceDictionary dictionary, string key, Color color)
    {
        if (dictionary.TryGetValue(key, out var existing) && existing is SolidColorBrush brush)
        {
            brush.Color = color;
        }
        else
        {
            dictionary[key] = new SolidColorBrush(color);
        }
    }

    private static State? Read(string dataRoot)
    {
        try
        {
            var path = Path.Combine(dataRoot, FileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Write(string dataRoot, State state)
    {
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(dataRoot, FileName), JsonSerializer.Serialize(state));
    }

    private sealed record State(AppTheme Theme = AppTheme.System, IconSet? Icons = null, MenuColor? Menu = null, AccentColor? Accent = null, string? Font = null);
}
