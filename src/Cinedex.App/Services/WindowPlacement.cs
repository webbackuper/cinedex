using System.Text.Json;

namespace Cinedex.App.Services;

/// <summary>Taille de la fenêtre principale au lancement.</summary>
public enum StartupWindowMode
{
    /// <summary>Agrandie à la zone utile de l'écran (barre des tâches visible) : par défaut.</summary>
    Maximized,

    /// <summary>Plein écran, sans bordure ni barre des tâches (F11 pour en sortir).</summary>
    FullScreen,

    /// <summary>Taille et position de la dernière fermeture.</summary>
    LastSize,
}

/// <summary>
/// Placement de la fenêtre principale : mode choisi dans les réglages et dernière taille connue.
/// Commun à tous les catalogues, mémorisé dans <c>window.json</c> à la racine des données.
/// </summary>
public sealed record WindowPlacement
{
    private const string FileName = "window.json";

    public StartupWindowMode Mode { get; init; } = StartupWindowMode.Maximized;

    public bool WasMaximized { get; init; } = true;

    public int? X { get; init; }

    public int? Y { get; init; }

    public double? Width { get; init; }

    public double? Height { get; init; }

    public static WindowPlacement Load(string dataRoot)
    {
        try
        {
            var path = Path.Combine(dataRoot, FileName);
            return File.Exists(path) && JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(path)) is { } placement && Enum.IsDefined(placement.Mode)
                ? placement
                : new WindowPlacement();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new WindowPlacement();
        }
    }

    public void Save(string dataRoot)
    {
        try
        {
            Directory.CreateDirectory(dataRoot);
            File.WriteAllText(Path.Combine(dataRoot, FileName), JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Placement non mémorisé : sans conséquence, la fenêtre s'ouvrira agrandie la prochaine fois.
        }
    }
}
