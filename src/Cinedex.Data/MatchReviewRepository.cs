using System.Text.Json;
using Cinedex.Core.Metadata;
using Dapper;

namespace Cinedex.Data;

/// <summary>Un titre en attente de choix : ce qui a été cherché et les fiches TMDb possibles.</summary>
public sealed record MatchReview(long TitleId, string Title, int? Year, string Query, IReadOnlyList<MetadataSearchResult> Candidates, string? FilePath);

/// <summary>Liste « À vérifier » : titres dont la fiche TMDb n'a pas pu être choisie sans ambiguïté.</summary>
public sealed class MatchReviewRepository(CatalogDatabase database)
{
    /// <summary>Au plus autant de candidats gardés par titre (les premiers selon TMDb).</summary>
    public const int MaxCandidates = 12;

    public void Add(long titleId, string query, IReadOnlyList<MetadataSearchResult> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        using var connection = database.Open();
        connection.Execute(
            """
            INSERT INTO match_review (title_id, query, candidates) VALUES (@titleId, @query, @json)
            ON CONFLICT (title_id) DO UPDATE SET query = excluded.query, candidates = excluded.candidates, created_at = excluded.created_at;
            """,
            new { titleId, query, json = JsonSerializer.Serialize(candidates.Take(MaxCandidates).ToList()) });
    }

    public void Remove(long titleId)
    {
        using var connection = database.Open();
        connection.Execute("DELETE FROM match_review WHERE title_id = @titleId;", new { titleId });
    }

    public int Count()
    {
        using var connection = database.Open();
        return connection.ExecuteScalar<int>("SELECT count(*) FROM match_review;");
    }

    /// <summary>Titres à vérifier, les plus anciens d'abord, avec le chemin de leur premier fichier.</summary>
    public IReadOnlyList<MatchReview> List()
    {
        using var connection = database.Open();
        return connection.Query<(long TitleId, string Title, long? Year, string Query, string Candidates, string? Path, string? Root)>(
                """
                SELECT r.title_id, t.title, t.year, r.query, r.candidates, m.path, sr.path
                FROM match_review r
                JOIN title t ON t.id = r.title_id
                LEFT JOIN media_file m ON m.id = (SELECT min(m2.id) FROM media_file m2 JOIN copy c2 ON c2.id = m2.copy_id WHERE c2.title_id = r.title_id)
                LEFT JOIN scan_root sr ON sr.id = m.scan_root_id
                ORDER BY r.created_at, t.sort_title;
                """)
            .Select(r => new MatchReview(
                r.TitleId,
                r.Title,
                (int?)r.Year,
                r.Query,
                Deserialize(r.Candidates),
                r.Path is null ? null : r.Root is null ? r.Path : Path.Combine(r.Root, r.Path)))
            .ToList();
    }

    private static List<MetadataSearchResult> Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<MetadataSearchResult>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
