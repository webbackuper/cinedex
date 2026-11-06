using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Dapper;

namespace Cinedex.Data;

/// <summary>
/// Une suggestion de TMDb : le film proposé, et s'il est déjà au catalogue (auquel cas son identifiant local).
/// </summary>
public sealed record TmdbSuggestion(TitleKind Kind, long TmdbId, string Name, int? Year, string? PosterPath, double? Rating, long? OwnedTitleId)
{
    public bool IsOwned => OwnedTitleId is not null;
}

/// <summary>
/// Cache des suggestions TMDb d'un titre. TMDb propose le monde entier, dont l'essentiel n'est pas au
/// catalogue : ces suggestions complètent « Vous aimerez aussi » au lieu de le remplacer, et un seul appel
/// réseau par titre suffit, son résultat étant gardé ici.
/// </summary>
public sealed class SuggestionRepository(CatalogDatabase database)
{
    /// <summary>Vrai si ce titre a déjà été interrogé (même si TMDb n'avait rien à proposer).</summary>
    public bool WasFetched(long titleId)
    {
        using var connection = database.Open();
        return connection.ExecuteScalar<int>("SELECT count(*) FROM tmdb_suggestion_fetch WHERE title_id = @titleId;", new { titleId }) > 0;
    }

    /// <summary>Suggestions gardées pour ce titre, dans l'ordre de TMDb, avec ce que le catalogue possède déjà.</summary>
    public IReadOnlyList<TmdbSuggestion> For(long titleId)
    {
        using var connection = database.Open();
        return connection.Query<Row>(
                """
                SELECT s.kind, s.tmdb_id, s.name, s.year, s.poster_path, s.rating,
                       (SELECT t.id FROM title t WHERE t.tmdb_id = s.tmdb_id AND t.kind = s.kind) AS owned_title_id
                FROM tmdb_suggestion s WHERE s.title_id = @titleId ORDER BY s.position;
                """,
                new { titleId })
            .Select(r => new TmdbSuggestion(
                r.Kind == "series" ? TitleKind.Series : TitleKind.Movie,
                r.Tmdb_Id, r.Name, r.Year, r.Poster_Path, r.Rating, r.Owned_Title_Id))
            .ToList();
    }

    /// <summary>Remplace les suggestions gardées pour ce titre et note la date de l'appel.</summary>
    public void Save(long titleId, IReadOnlyList<MetadataSearchResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("DELETE FROM tmdb_suggestion WHERE title_id = @titleId;", new { titleId }, transaction);
        var position = 0;
        foreach (var result in results)
        {
            connection.Execute(
                """
                INSERT INTO tmdb_suggestion (title_id, position, kind, tmdb_id, name, year, poster_path, rating)
                VALUES (@titleId, @position, @kind, @tmdbId, @name, @year, @poster, @rating);
                """,
                new
                {
                    titleId,
                    position = position++,
                    kind = result.Kind == TitleKind.Series ? "series" : "movie",
                    tmdbId = result.SourceId,
                    name = result.Title,
                    year = result.Year,
                    poster = result.PosterPath,
                    rating = result.Rating,
                },
                transaction);
        }

        connection.Execute(
            """
            INSERT INTO tmdb_suggestion_fetch (title_id, fetched_at) VALUES (@titleId, @when)
            ON CONFLICT (title_id) DO UPDATE SET fetched_at = @when;
            """,
            new { titleId, when = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture) },
            transaction);
        transaction.Commit();
    }

    private sealed class Row
    {
        public string Kind { get; set; } = "movie";

        public long Tmdb_Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int? Year { get; set; }

        public string? Poster_Path { get; set; }

        public double? Rating { get; set; }

        public long? Owned_Title_Id { get; set; }
    }
}
