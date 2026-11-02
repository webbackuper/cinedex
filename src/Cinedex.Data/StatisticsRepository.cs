using Dapper;

namespace Cinedex.Data;

/// <summary>Une ligne de répartition : libellé, nombre de titres, part relative à la plus grande ligne (0 à 1).</summary>
public sealed record StatCount(string Label, int Count)
{
    public double Ratio { get; init; }
}

/// <summary>Chiffres du catalogue ouvert (onglet Statistiques des réglages).</summary>
public sealed record CatalogStatistics
{
    public int Films { get; init; }

    public int Series { get; init; }

    public int Episodes { get; init; }

    public int Actors { get; init; }

    public int Directors { get; init; }

    public int Seen { get; init; }

    public int Favorites { get; init; }

    public int Loaned { get; init; }

    public int Files { get; init; }

    public long TotalBytes { get; init; }

    public long FilmMinutes { get; init; }

    public int LinkedToTmdb { get; init; }

    public int ToReview { get; init; }

    public IReadOnlyList<StatCount> Genres { get; init; } = [];

    public IReadOnlyList<StatCount> Decades { get; init; } = [];

    public IReadOnlyList<StatCount> Definitions { get; init; } = [];

    public IReadOnlyList<StatCount> Media { get; init; } = [];

    public IReadOnlyList<StatCount> Disks { get; init; } = [];
}

/// <summary>Calcule les statistiques du catalogue en quelques requêtes agrégées.</summary>
public sealed class StatisticsRepository(CatalogDatabase database)
{
    public CatalogStatistics Compute()
    {
        using var connection = database.Open();
        int Count(string sql) => connection.ExecuteScalar<int>(sql);

        return new CatalogStatistics
        {
            Films = Count("SELECT count(*) FROM title WHERE kind = 'movie';"),
            Series = Count("SELECT count(*) FROM title WHERE kind = 'series';"),
            Episodes = Count("SELECT count(*) FROM episode;"),
            Actors = Count("SELECT count(DISTINCT person_id) FROM credit WHERE role = 'actor';"),
            Directors = Count("SELECT count(DISTINCT person_id) FROM credit WHERE role = 'director';"),
            Seen = Count("SELECT count(DISTINCT title_id) FROM viewing WHERE title_id IS NOT NULL;"),
            Favorites = Count("SELECT count(*) FROM title WHERE is_favorite = 1;"),
            Loaned = Count("SELECT count(*) FROM loan WHERE returned_at IS NULL;"),
            Files = Count("SELECT count(*) FROM media_file;"),
            TotalBytes = connection.ExecuteScalar<long?>("SELECT sum(size_bytes) FROM media_file;") ?? 0,
            FilmMinutes = connection.ExecuteScalar<long?>("SELECT sum(runtime_minutes) FROM title WHERE kind = 'movie';") ?? 0,
            LinkedToTmdb = Count("SELECT count(*) FROM title WHERE tmdb_id IS NOT NULL;"),
            ToReview = Count("SELECT count(*) FROM match_review;"),
            Genres = Distribution(connection,
                """
                SELECT g.label, count(DISTINCT tg.title_id) FROM title_genre tg JOIN genre g ON g.id = tg.genre_id
                GROUP BY g.id ORDER BY 2 DESC, g.label;
                """),
            Decades = Distribution(connection,
                """
                SELECT (year / 10 * 10) || ' – ' || (year / 10 * 10 + 9), count(*) FROM title WHERE year IS NOT NULL
                GROUP BY year / 10 ORDER BY year / 10;
                """),
            Definitions = Distribution(connection,
                """
                SELECT CASE WHEN h >= 2000 THEN 'Ultra HD (4K)' WHEN h >= 700 THEN 'Haute définition' ELSE 'Définition standard' END, count(*)
                FROM (SELECT max(m.height) AS h FROM media_file m JOIN copy c ON c.id = m.copy_id WHERE m.height IS NOT NULL GROUP BY c.title_id)
                GROUP BY 1 ORDER BY min(h) DESC;
                """),
            Media = Distribution(connection,
                """
                SELECT r.label, count(DISTINCT c.title_id) FROM copy c JOIN ref_list r ON r.id = c.medium_id
                GROUP BY r.id ORDER BY 2 DESC, r.label;
                """),
            Disks = Distribution(connection,
                """
                SELECT m.volume_label, count(DISTINCT c.title_id) FROM media_file m JOIN copy c ON c.id = m.copy_id
                WHERE m.volume_label IS NOT NULL GROUP BY m.volume_label ORDER BY 2 DESC, m.volume_label;
                """),
        };
    }

    private static List<StatCount> Distribution(Microsoft.Data.Sqlite.SqliteConnection connection, string sql)
    {
        var rows = connection.Query<(string Label, long Count)>(sql).ToList();
        var max = rows.Count == 0 ? 1 : Math.Max(1, rows.Max(r => r.Count));
        return [.. rows.Select(r => new StatCount(r.Label, (int)r.Count) { Ratio = (double)r.Count / max })];
    }
}
