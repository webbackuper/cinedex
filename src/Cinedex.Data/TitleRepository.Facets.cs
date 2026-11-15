using Dapper;

namespace Cinedex.Data;

/// <summary>
/// Listes de parcours du bandeau gauche : genres, décennies, pays, langues. Chacune ne contient que ce que
/// le catalogue porte vraiment, pour qu'un clic ne tombe jamais sur une vue vide.
/// </summary>
public sealed record GenreShowcase(string Code, string Label, int Titles, IReadOnlyList<string> PosterRelativePaths);

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

    /// <summary>
    /// Les genres portés par le catalogue, chacun avec son nombre de titres et jusqu'à quatre affiches : de
    /// quoi lui faire un visuel avec ce que le catalogue contient. Les affiches sont celles des titres les
    /// mieux notés du genre, pour qu'il se reconnaisse à ses films les plus connus.
    /// </summary>
    public IReadOnlyList<GenreShowcase> GenreShowcases()
    {
        using var connection = database.Open();
        var counts = connection.Query<(string Code, string Label, long Count)>(
                """
                SELECT g.code, g.label, count(*) FROM genre g JOIN title_genre tg ON tg.genre_id = g.id
                GROUP BY g.id ORDER BY g.label COLLATE NOCASE;
                """)
            .ToList();
        var posters = connection.Query<(string Code, string Path)>(
                """
                SELECT code, rel_path FROM (
                    SELECT g.code AS code, i.rel_path AS rel_path,
                           row_number() OVER (PARTITION BY g.id ORDER BY t.imdb_rating IS NULL, t.imdb_rating DESC, t.id) AS rank
                    FROM genre g
                    JOIN title_genre tg ON tg.genre_id = g.id
                    JOIN title t ON t.id = tg.title_id
                    JOIN title_image ti ON ti.title_id = t.id AND ti.role = 'poster'
                    JOIN image i ON i.id = ti.image_id)
                WHERE rank <= 4 ORDER BY code, rank;
                """)
            .ToLookup(p => p.Code, p => p.Path, StringComparer.Ordinal);
        return counts.Select(c => new GenreShowcase(c.Code, c.Label, (int)c.Count, posters[c.Code].ToList())).ToList();
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
