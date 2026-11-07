using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Cinedex.Core.Titles;

namespace Cinedex.Core.Metadata;

/// <summary>
/// Référence à une fiche TMDb : son numéro, et la section où elle vit. TMDb range les téléfilms et les
/// collections de téléfilms sous <c>tv</c>, même lorsqu'on les catalogue comme des films ; les deux sections
/// ont leurs propres numéros, si bien que le numéro seul ne suffit pas à retrouver une fiche.
/// </summary>
public sealed partial record TmdbReference(TitleKind Kind, long Id)
{
    /// <summary>Forme canonique : <c>movie/949</c> ou <c>tv/322888</c>.</summary>
    public string Text => Kind == TitleKind.Series
        ? "tv/" + Id.ToString(CultureInfo.InvariantCulture)
        : "movie/" + Id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Forme courte : le numéro seul quand la section est celle qu'on attendait, sinon la forme complète.</summary>
    public string TextFor(TitleKind expected) => Kind == expected ? Id.ToString(CultureInfo.InvariantCulture) : Text;

    public string Url => "https://www.themoviedb.org/" + Text;

    /// <summary>
    /// Lit une référence écrite par un humain : un numéro seul (dans la section attendue), <c>movie/949</c>,
    /// <c>tv/322888</c>, ou l'adresse d'une page TMDb copiée depuis le navigateur.
    /// </summary>
    public static bool TryParse(string? text, TitleKind fallback, [NotNullWhen(true)] out TmdbReference? reference)
    {
        reference = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var bare))
        {
            reference = bare > 0 ? new TmdbReference(fallback, bare) : null;
            return reference is not null;
        }

        // L'adresse d'une page garde sa section et son numéro : « .../tv/322888-coup-de-foudre?language=fr-FR ».
        if (UrlPattern().Match(trimmed) is { Success: true } url)
        {
            trimmed = url.Groups["kind"].Value + "/" + url.Groups["id"].Value;
        }

        if (PathPattern().Match(trimmed) is not { Success: true } path)
        {
            return false;
        }

        var id = long.Parse(path.Groups["id"].Value, CultureInfo.InvariantCulture);
        // La casse est acceptée à la lecture (« TV/322888 ») mais la forme canonique reste en minuscules.
        var isTv = string.Equals(path.Groups["kind"].Value, "tv", StringComparison.OrdinalIgnoreCase);
        reference = id > 0 ? new TmdbReference(isTv ? TitleKind.Series : TitleKind.Movie, id) : null;
        return reference is not null;
    }

    [GeneratedRegex(@"^(?<kind>movie|tv)/(?<id>[0-9]{1,10})$", RegexOptions.IgnoreCase)]
    private static partial Regex PathPattern();

    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?themoviedb\.org/(?<kind>movie|tv)/(?<id>[0-9]{1,10})(?:[-/?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
}
