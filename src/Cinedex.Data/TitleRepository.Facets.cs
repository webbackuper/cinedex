using Dapper;

namespace Cinedex.Data;

/// <summary>
/// Listes de parcours du bandeau gauche : genres, décennies, pays, langues. Chacune ne contient que ce que
/// le catalogue porte vraiment, pour qu'un clic ne tombe jamais sur une vue vide.
/// </summary>
public sealed partial class TitleRepository
{
    /// <summary>Genres portés par au moins un titre : code (clé de recherche) et libellé français de référence.</summary>
    public IReadOnlyList<(string Code, string Label)> UsedGenres()
    {
        using var connection = database.Open();
        return connection.Query<(string Code, string Label)>(
                """
                SELECT g.code, g.label FROM genre g
                WHERE EXISTS (SELECT 1 FROM title_genre tg WHERE tg.genre_id = g.id)
                ORDER BY g.label COLLATE NOCASE;
                """)
            .ToList();
    }

    /// <summary>Décennies présentes, la plus récente d'abord : 2020 vaut « 2020 – 2029 ».</summary>
    public IReadOnlyList<int> Decades()
    {
        using var connection = database.Open();
        return connection.Query<int>(
                "SELECT DISTINCT (year / 10) * 10 FROM title WHERE year IS NOT NULL ORDER BY 1 DESC;")
            .ToList();
    }

    /// <summary>Codes pays (ISO 3166-1) présents ; la colonne en porte plusieurs, séparés par des virgules.</summary>
    public IReadOnlyList<string> CountryCodes() => Codes("countries");

    /// <summary>Codes des langues parlées (ISO 639-1) présents.</summary>
    public IReadOnlyList<string> LanguageCodes() => Codes("spoken_languages");

    private List<string> Codes(string column)
    {
        using var connection = database.Open();
        // Les codes sont concaténés dans une colonne : le découpage se fait ici, pas en SQL.
        return connection.Query<string>($"SELECT {column} FROM title WHERE coalesce({column}, '') <> '';")
            .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(v => v.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
