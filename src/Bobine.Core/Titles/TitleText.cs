using System.Globalization;

namespace Bobine.Core.Titles;

public static class TitleText
{
    /// <summary>« Heat (1995) » donne le titre « Heat » et l'année 1995 ; sinon le texte tel quel.</summary>
    public static (string Title, int? Year) SplitYear(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var text = input.Trim();
        if (text.Length > 7 && text[^1] == ')' && text[^6] == '(' &&
            int.TryParse(text.AsSpan(text.Length - 5, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) &&
            year is >= 1870 and <= 2100)
        {
            return (text[..^6].TrimEnd(), year);
        }

        return (text, null);
    }

    /// <summary>Durée lisible : « 2 h 50 », « 2 h », « 45 min » ; vide si inconnue.</summary>
    public static string FormatRuntime(int? minutes) => minutes switch
    {
        null or <= 0 => string.Empty,
        < 60 => $"{minutes} min",
        _ when minutes % 60 == 0 => $"{minutes / 60} h",
        _ => $"{minutes / 60} h {minutes % 60:00}",
    };

    /// <summary>Durée en secondes, à la seconde près pour un fichier : « 1 h 52 min 07 s ».</summary>
    public static string FormatDuration(int seconds) => seconds switch
    {
        < 60 => $"{seconds} s",
        < 3600 => $"{seconds / 60} min {seconds % 60:00} s",
        _ => $"{seconds / 3600} h {seconds % 3600 / 60:00} min {seconds % 60:00} s",
    };
}
