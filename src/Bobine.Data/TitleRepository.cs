using Bobine.Core.Titles;
using Dapper;

namespace Bobine.Data;

public sealed class TitleRepository(CatalogDatabase database)
{
    private const string SummaryColumns = """
        t.id, t.kind, t.title, t.year, t.runtime_minutes, t.user_rating, t.is_owned, t.is_favorite,
        EXISTS (SELECT 1 FROM viewing v WHERE v.title_id = t.id) AS is_seen
        """;

    public long Add(NewTitle title)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(title.Title);

        using var connection = database.Open();
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO title (kind, title, original_title, sort_title, year)
            VALUES (@Kind, @Title, @OriginalTitle, @SortTitle, @Year)
            RETURNING id;
            """,
            new
            {
                Kind = ToDb(title.Kind),
                Title = title.Title.Trim(),
                OriginalTitle = title.OriginalTitle?.Trim(),
                SortTitle = SortTitle.Compute(title.Title),
                title.Year,
            });
    }

    public IReadOnlyList<TitleSummary> ListAll()
    {
        using var connection = database.Open();
        return connection.Query<Row>($"SELECT {SummaryColumns} FROM title t ORDER BY t.sort_title, t.year;")
            .Select(ToSummary)
            .ToList();
    }

    /// <summary>Recherche plein texte (titres et synopsis), insensible aux accents.</summary>
    public IReadOnlyList<TitleSummary> Search(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ListAll();
        }

        using var connection = database.Open();
        return connection.Query<Row>(
                $"""
                SELECT {SummaryColumns}
                FROM title_fts f JOIN title t ON t.id = f.rowid
                WHERE title_fts MATCH @Query
                ORDER BY bm25(title_fts), t.sort_title;
                """,
                new { Query = ToFtsQuery(text) })
            .Select(ToSummary)
            .ToList();
    }

    public int Count()
    {
        using var connection = database.Open();
        return connection.ExecuteScalar<int>("SELECT count(*) FROM title;");
    }

    /// <summary>
    /// Transforme une saisie libre en requête FTS5 sûre : chaque mot devient un préfixe entre guillemets,
    /// ce qui neutralise la syntaxe FTS (opérateurs, colonnes, parenthèses).
    /// </summary>
    internal static string ToFtsQuery(string text)
    {
        var terms = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(term => "\"" + term.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"*");
        return string.Join(' ', terms);
    }

    private static string ToDb(TitleKind kind) => kind == TitleKind.Series ? "series" : "movie";

    private static TitleSummary ToSummary(Row row) => new(
        row.Id,
        row.Kind == "series" ? TitleKind.Series : TitleKind.Movie,
        row.Title,
        row.Year is null ? null : (int)row.Year,
        row.Runtime_Minutes is null ? null : (int)row.Runtime_Minutes,
        row.User_Rating,
        row.Is_Owned != 0,
        row.Is_Favorite != 0,
        row.Is_Seen != 0);

    // Dapper associe les colonnes aux propriétés sans tenir compte de la casse.
#pragma warning disable CA1707, CA1812 // Noms alignés sur les colonnes SQL ; instancié par Dapper.
    private sealed class Row
    {
        public long Id { get; set; }

        public string Kind { get; set; } = "movie";

        public string Title { get; set; } = string.Empty;

        public long? Year { get; set; }

        public long? Runtime_Minutes { get; set; }

        public double? User_Rating { get; set; }

        public long Is_Owned { get; set; }

        public long Is_Favorite { get; set; }

        public long Is_Seen { get; set; }
    }
#pragma warning restore CA1707, CA1812
}
