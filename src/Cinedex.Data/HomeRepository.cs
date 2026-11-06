using Dapper;

namespace Cinedex.Data;

/// <summary>
/// Les bandes de la page d'accueil. Elles répondent à la seule question qu'on se pose en ouvrant son
/// catalogue : qu'est-ce que je regarde ce soir ? Chacune est une requête sur la base, rien de distant.
/// </summary>
public sealed class HomeRepository(CatalogDatabase database)
{
    /// <summary>
    /// Films possédés, jamais vus, dont un fichier est lisible tout de suite, et bien notés quand on a une
    /// note. Le tirage change chaque jour mais pas d'un retour à l'autre : une page qui bouge sans cesse
    /// devient désagréable, une page figée ne fait jamais redécouvrir.
    /// </summary>
    public IReadOnlyList<long> Tonight(IReadOnlyCollection<string> mountedLabels, DateOnly day, int limit = 6)
    {
        ArgumentNullException.ThrowIfNull(mountedLabels);
        using var connection = database.Open();
        return connection.Query<long>(
                $"""
                SELECT t.id FROM title t
                WHERE t.kind = 'movie' AND t.is_owned = 1
                  AND NOT EXISTS (SELECT 1 FROM viewing v WHERE v.title_id = t.id)
                  AND coalesce(t.tmdb_rating, t.imdb_rating, 10) >= 7
                  AND EXISTS (
                      SELECT 1 FROM media_file mf JOIN copy c ON c.id = mf.copy_id
                      WHERE c.title_id = t.id AND mf.is_missing = 0
                        AND (mf.volume_label IS NULL OR mf.volume_label IN (SELECT value FROM json_each(@Labels))))
                ORDER BY {Shuffle} LIMIT @limit;
                """,
                new { Labels = System.Text.Json.JsonSerializer.Serialize(mountedLabels), seed = Seed(day), limit })
            .ToList();
    }

    /// <summary>
    /// Sagas commencées : un film de la collection a été vu, un autre est au catalogue sans l'être. C'est la
    /// suggestion que seul votre propre historique peut faire.
    /// </summary>
    public IReadOnlyList<long> ResumeSaga(int limit = 6)
    {
        using var connection = database.Open();
        return connection.Query<long>(
                """
                SELECT t.id FROM title t
                WHERE t.is_owned = 1 AND t.collection_id IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM viewing v WHERE v.title_id = t.id)
                  AND EXISTS (
                      SELECT 1 FROM title s JOIN viewing v ON v.title_id = s.id
                      WHERE s.collection_id = t.collection_id AND s.id <> t.id)
                ORDER BY t.collection_id, t.year, t.sort_title LIMIT @limit;
                """,
                new { limit })
            .ToList();
    }

    /// <summary>Derniers titres entrés au catalogue.</summary>
    public IReadOnlyList<long> JustAdded(int limit = 12)
    {
        using var connection = database.Open();
        return connection.Query<long>(
                "SELECT id FROM title WHERE is_owned = 1 ORDER BY added_at DESC, id DESC LIMIT @limit;",
                new { limit })
            .ToList();
    }

    /// <summary>
    /// Films possédés depuis longtemps et jamais vus : la bande qui fait redécouvrir sa propre collection.
    /// </summary>
    public IReadOnlyList<long> Forgotten(DateOnly day, int limit = 6)
    {
        using var connection = database.Open();
        return connection.Query<long>(
                $"""
                SELECT t.id FROM title t
                WHERE t.is_owned = 1 AND t.added_at < @before
                  AND NOT EXISTS (SELECT 1 FROM viewing v WHERE v.title_id = t.id)
                ORDER BY {Shuffle} LIMIT @limit;
                """,
                new { before = day.AddMonths(-6).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), seed = Seed(day), limit })
            .ToList();
    }

    // Tirage mêlé mais reproductible : le même jour donne le même ordre, un autre jour en donne un autre.
    // La graine multiplie l'identifiant au lieu de s'y ajouter : une addition décale toutes les valeurs de
    // la même quantité et ne change donc pas leur ordre.
    private const string Shuffle = "(t.id * @seed) % 1000003";

    private static long Seed(DateOnly day) => 1 + (day.DayNumber * 2654435761L % 1000002L);
}
