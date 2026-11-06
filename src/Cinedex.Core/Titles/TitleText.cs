using System.Globalization;
using Cinedex.Core.Localization;

namespace Cinedex.Core.Titles;

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
        < 60 => Tr.F("{0} min", minutes),
        _ when minutes % 60 == 0 => $"{minutes / 60} h",
        _ => $"{minutes / 60} h {minutes % 60:00}",
    };

    /// <summary>Durée en secondes, à la seconde près pour un fichier : « 1 h 52 min 07 s ».</summary>
    public static string FormatDuration(int seconds) => seconds switch
    {
        < 60 => $"{seconds} s",
        < 3600 => Tr.F("{0} min {1:00} s", seconds / 60, seconds % 60),
        _ => Tr.F("{0} h {1:00} min {2:00} s", seconds / 3600, seconds % 3600 / 60, seconds % 60),
    };

    /// <summary>
    /// Nom d'un pays depuis son code ISO 3166-1 (« FR » donne « France »), dans la langue de l'interface.
    /// Le code lui-même est rendu quand il n'est pas reconnu : une donnée TMDb inattendue reste lisible.
    /// </summary>
    public static string CountryName(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        try
        {
            return new RegionInfo(code.Trim()).DisplayName;
        }
        catch (ArgumentException)
        {
            return code.Trim();
        }
    }

    /// <summary>Nom d'une langue depuis son code ISO 639-1 (« fr » donne « français »), ou le code si inconnu.</summary>
    public static string LanguageName(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var trimmed = code.Trim();
        try
        {
            var culture = new CultureInfo(trimmed);
            return culture.Name.Length == 0 ? trimmed : culture.DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return trimmed;
        }
    }

    /// <summary>Libellé d'une décennie : 1980 donne « 1980 – 1989 ».</summary>
    public static string DecadeLabel(int decade) =>
        string.Create(CultureInfo.CurrentCulture, $"{decade} – {decade + 9}");
}
