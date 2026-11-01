using System.Text.Json;
using Avalonia;
using Avalonia.Styling;

namespace Bobine.App.Services;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Apparence de l'application (clair, sombre ou comme le système). Réglage commun à tous les catalogues,
/// mémorisé dans <c>appearance.json</c> à la racine des données.
/// </summary>
public static class Appearance
{
    private const string FileName = "appearance.json";

    public static AppTheme Load(string dataRoot)
    {
        try
        {
            var path = Path.Combine(dataRoot, FileName);
            return File.Exists(path) && JsonSerializer.Deserialize<State>(File.ReadAllText(path)) is { } state && Enum.IsDefined(state.Theme)
                ? state.Theme
                : AppTheme.System;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return AppTheme.System;
        }
    }

    public static void Save(string dataRoot, AppTheme theme)
    {
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(dataRoot, FileName), JsonSerializer.Serialize(new State(theme)));
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

    private sealed record State(AppTheme Theme);
}
