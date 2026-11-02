using Cinedex.Core.Titles;

namespace Cinedex.Core.Metadata;

/// <summary>
/// Résultat d'un rapprochement automatique avec TMDb : une fiche retenue sans ambiguïté, ou des candidats
/// à faire choisir par l'utilisateur (liste « À vérifier »), ou rien du tout.
/// </summary>
public sealed record Identification(MetadataSearchResult? Match, IReadOnlyList<MetadataSearchResult> Candidates, string Query)
{
    public static Identification None(string query) => new(null, [], query);

    public static Identification Found(MetadataSearchResult match, string query) => new(match, [], query);

    public bool NeedsReview => Match is null && Candidates.Count > 0;

    /// <summary>
    /// Règle commune à l'import et à la mise à jour : un seul résultat à l'année près (±1 an) est retenu ;
    /// sinon (plusieurs, aucun à la bonne année, ou année inconnue) les résultats sont proposés à l'utilisateur.
    /// <paramref name="takeFirst"/> garde l'ancien comportement « premier résultat si plusieurs ».
    /// </summary>
    public static Identification Decide(IReadOnlyList<MetadataSearchResult> results, int? year, string query, bool takeFirst = false)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Count == 0)
        {
            return None(query);
        }

        if (year is not { } y)
        {
            return takeFirst ? Found(results[0], query) : new(null, results, query);
        }

        var plausible = results.Where(r => r.Year is { } ry && Math.Abs(ry - y) <= 1).ToList();
        return plausible.Count switch
        {
            1 => Found(plausible[0], query),
            > 1 when takeFirst => Found(plausible[0], query),
            > 1 => new(null, plausible, query),
            _ => new(null, results, query),
        };
    }
}

/// <summary>Liens vers le site TMDb, construits uniquement par l'application (https, domaine fixe).</summary>
public static class TmdbLinks
{
    public static string Title(TitleKind kind, long sourceId) =>
        $"https://www.themoviedb.org/{(kind == TitleKind.Series ? "tv" : "movie")}/{sourceId}?language=fr-FR";

    public static string Search(string query, TitleKind? kind = TitleKind.Movie) =>
        $"https://www.themoviedb.org/search{(kind switch { TitleKind.Movie => "/movie", TitleKind.Series => "/tv", _ => string.Empty })}?query={Uri.EscapeDataString(query ?? string.Empty)}&language=fr-FR";
}
