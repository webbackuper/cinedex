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
}
