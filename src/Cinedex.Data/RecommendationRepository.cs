using Dapper;

namespace Cinedex.Data;

/// <summary>
/// Un titre proche d'un autre : son identifiant, son score, et la raison du rapprochement telle qu'elle
/// s'affiche sous l'affiche (« même réalisateur », « policier, braquage »).
/// </summary>
public sealed record Recommendation(long TitleId, double Score, string Reason);

/// <summary>
/// « Si vous aimez ce film, vous aimerez aussi ». Les rapprochements sont calculés sur le catalogue, pas
/// demandés à TMDb : une suggestion qu'on ne peut pas regarder ce soir n'en est pas une. Tout vient de
/// données déjà en base (genres, mots-clés, générique, collection, année, note), donc hors ligne et immédiat.
/// </summary>
public sealed class RecommendationRepository(CatalogDatabase database)
{
    /// <summary>Poids de chaque signal. Ils se cumulent : un film peut gagner sur plusieurs à la fois.</summary>
    private const double GenreWeight = 3;
    private const double KeywordWeight = 2;
    private const double DirectorWeight = 5;
    private const double ActorWeight = 2;
    private const double CollectionWeight = 6;

    /// <summary>Acteurs du haut de l'affiche : au-delà, un second rôle partagé ne dit plus grand-chose.</summary>
    private const int LeadActors = 10;

    /// <summary>
    /// Ce qui justifie une suggestion : genres, mots-clés, générique, saga. L'année proche et la bonne note
    /// départagent des candidats, elles ne prouvent rien — elles comptent donc dans l'ordre, pas dans ce seuil.
    /// Fixé à 5 : deux genres communs, ou un genre et un mot-clé, ou un réalisateur. Un seul genre ne suffit pas,
    /// sinon tout le catalogue ressortirait.
    /// </summary>
    private const double MinimumEvidence = 5;

    /// <summary>
    /// Titres proches de <paramref name="titleId"/>, les plus proches d'abord. Même nature seulement : on ne
    /// propose pas une série à la suite d'un film.
    /// </summary>
    public IReadOnlyList<Recommendation> For(long titleId, int limit = 8)
    {
        using var connection = database.Open();
        var rows = connection.Query<Row>(Sql, new { id = titleId, limit, lead = LeadActors, minimum = MinimumEvidence }).ToList();
        return rows.Select(r => new Recommendation(r.Id, r.Score, Reason(r))).ToList();
    }

    /// <summary>
    /// La raison affichée compte autant que la suggestion : elle rend le classement lisible, et fausse elle
    /// saute aux yeux. Les signaux les plus parlants passent devant, et on s'arrête à trois.
    /// </summary>
    private static string Reason(Row row)
    {
        var parts = new List<string>(3);
        if (row.Same_Collection == 1)
        {
            parts.Add(Cinedex.Core.Localization.Tr.T("même saga"));
        }

        if (row.Directors is { Length: > 0 } director)
        {
            parts.Add(Cinedex.Core.Localization.Tr.F("même réalisateur : {0}", director));
        }

        if (row.Actors is { Length: > 0 } actor && parts.Count < 3)
        {
            parts.Add(Cinedex.Core.Localization.Tr.F("avec {0}", actor));
        }

        if (row.Keywords is { Length: > 0 } keywords && parts.Count < 3)
        {
            // Les mots-clés viennent de TMDb dans la langue demandée : ils ne passent pas par le catalogue
            // de traduction, qui ne connaît que les textes de l'interface.
            parts.Add(Join(keywords, 2, t => t));
        }

        if (parts.Count < 3 && row.Genres is { Length: > 0 } genres)
        {
            parts.Add(Join(genres, 2, Cinedex.Core.Localization.Tr.T));
        }

        return string.Join(" · ", parts);
    }

    private static string Join(string list, int max, Func<string, string> label) =>
        string.Join(", ", list.Split('\u001f').Take(max).Select(label));

    private sealed class Row
    {
        public long Id { get; set; }

        public double Score { get; set; }

        public int Same_Collection { get; set; }

        public string? Genres { get; set; }

        public string? Keywords { get; set; }

        public string? Directors { get; set; }

        public string? Actors { get; set; }
    }

    // group_concat sépare par une unité de séparation (U+001F) : une virgule apparaîtrait dans un nom.
    private const string Sql = """
        WITH ref AS (SELECT id, kind, year, collection_id FROM title WHERE id = @id),
        shared_genre AS (
            SELECT tg.title_id, count(*) AS n, group_concat(g.label, char(31)) AS labels
            FROM title_genre tg JOIN genre g ON g.id = tg.genre_id
            WHERE tg.genre_id IN (SELECT genre_id FROM title_genre WHERE title_id = @id) AND tg.title_id <> @id
            GROUP BY tg.title_id),
        shared_keyword AS (
            SELECT tk.title_id, count(*) AS n, group_concat(k.name, char(31)) AS labels
            FROM title_keyword tk JOIN keyword k ON k.id = tk.keyword_id
            WHERE tk.keyword_id IN (SELECT keyword_id FROM title_keyword WHERE title_id = @id) AND tk.title_id <> @id
            GROUP BY tk.title_id),
        shared_director AS (
            SELECT cr.title_id, count(DISTINCT cr.person_id) AS n, min(p.name) AS labels
            FROM credit cr JOIN person p ON p.id = cr.person_id
            WHERE cr.role = 'director' AND cr.title_id <> @id
              AND cr.person_id IN (SELECT person_id FROM credit WHERE title_id = @id AND role = 'director')
            GROUP BY cr.title_id),
        shared_actor AS (
            SELECT cr.title_id, count(DISTINCT cr.person_id) AS n, min(p.name) AS labels
            FROM credit cr JOIN person p ON p.id = cr.person_id
            WHERE cr.role = 'actor' AND cr.position < @lead AND cr.title_id <> @id
              AND cr.person_id IN (SELECT person_id FROM credit WHERE title_id = @id AND role = 'actor' AND position < @lead)
            GROUP BY cr.title_id)
        SELECT id, evidence + tiebreak AS score, same_collection, genres, keywords, directors, actors FROM (
            SELECT t.id AS id,
                   CASE WHEN t.collection_id IS NOT NULL AND t.collection_id = (SELECT collection_id FROM ref) THEN 1 ELSE 0 END AS same_collection,
                   sg.labels AS genres, sk.labels AS keywords, sd.labels AS directors, sa.labels AS actors,
                   coalesce(sg.n, 0) * 3.0
                   + coalesce(sk.n, 0) * 2.0
                   + min(coalesce(sd.n, 0) * 5.0, 5.0)
                   + min(coalesce(sa.n, 0) * 2.0, 6.0)
                   + CASE WHEN t.collection_id IS NOT NULL AND t.collection_id = (SELECT collection_id FROM ref) THEN 6.0 ELSE 0.0 END AS evidence,
                   CASE WHEN t.year IS NOT NULL AND (SELECT year FROM ref) IS NOT NULL
                             AND abs(t.year - (SELECT year FROM ref)) <= 10
                        THEN (10 - abs(t.year - (SELECT year FROM ref))) / 5.0 ELSE 0.0 END
                   + CASE WHEN coalesce(t.tmdb_rating, t.imdb_rating, 0) >= 7 THEN 1.0 ELSE 0.0 END AS tiebreak
            FROM title t
            LEFT JOIN shared_genre sg ON sg.title_id = t.id
            LEFT JOIN shared_keyword sk ON sk.title_id = t.id
            LEFT JOIN shared_director sd ON sd.title_id = t.id
            LEFT JOIN shared_actor sa ON sa.title_id = t.id
            WHERE t.id <> @id AND t.kind = (SELECT kind FROM ref) AND t.is_owned = 1
              AND (sg.title_id IS NOT NULL OR sk.title_id IS NOT NULL OR sd.title_id IS NOT NULL OR sa.title_id IS NOT NULL)
        )
        WHERE evidence >= @minimum
        ORDER BY score DESC, id
        LIMIT @limit;
        """;
}
