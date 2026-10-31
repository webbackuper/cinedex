using System.Globalization;
using System.Text;

namespace Bobine.Core.Titles;

/// <summary>
/// Calcule la clé de tri d'un titre : articles initiaux ignorés (« The », « Le », « L' »...), accents retirés, casse ignorée.
/// </summary>
public static class SortTitle
{
    public static readonly IReadOnlyList<string> DefaultPrefixes =
    [
        "The ", "A ", "An ", "Le ", "La ", "Les ", "L'", "L’", "Un ", "Une ", "Des ", "Der ", "Die ", "Das ", "El ", "Los ", "Las ", "Il ", "Lo ", "Gli ", "De ", "Het ",
    ];

    public static string Compute(string title, IEnumerable<string>? prefixes = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        var trimmed = title.Trim();
        foreach (var prefix in prefixes ?? DefaultPrefixes)
        {
            if (trimmed.Length > prefix.Length && trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[prefix.Length..].TrimStart();
                break;
            }
        }

        return RemoveDiacritics(trimmed).ToUpperInvariant();
    }

    private static string RemoveDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
