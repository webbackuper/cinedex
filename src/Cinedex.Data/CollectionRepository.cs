using System.Text.Json;
using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Dapper;

namespace Cinedex.Data;

/// <summary>Une collection du catalogue : nombre de films possédés et, si sa fiche TMDb est lue, nombre de films de la saga.</summary>
public sealed record CollectionSummary(long Id, string Name, long? TmdbId, int Owned, int Parts, string? PosterRelativePath);

/// <summary>Fiche d'une collection : présentation TMDb, films de la saga, affiche et décor.</summary>
public sealed record CollectionDetail(
    long Id,
    string Name,
    long? TmdbId,
    string? Overview,
    IReadOnlyList<CollectionPart> Parts,
    string? PosterRelativePath,
    string? BackdropRelativePath,
    string? FetchedAt);

/// <summary>Un titre du catalogue rangé dans une collection.</summary>
public sealed record CollectionMember(long TitleId, string Title, int? Year, long? TmdbId, string? PosterRelativePath);

/// <summary>Collections (sagas, coffrets) : liste, fiche TMDb, films possédés, renommage et suppression.</summary>
public sealed class CollectionRepository(CatalogDatabase database)
{
    private const string PosterOf =
        "(SELECT i.rel_path FROM title_image ti JOIN image i ON i.id = ti.image_id WHERE ti.collection_id = c.id AND ti.role = 'poster')";

    private const string BackdropOf =
        "(SELECT i.rel_path FROM title_image ti JOIN image i ON i.id = ti.image_id WHERE ti.collection_id = c.id AND ti.role = 'backdrop')";

    public IReadOnlyList<CollectionSummary> List()
    {
        using var connection = database.Open();
        return connection.Query<(long Id, string Name, long? TmdbId, long Owned, long Parts, string? Poster)>(
                $"""
                SELECT c.id, c.name, c.tmdb_id,
                       (SELECT count(*) FROM title t WHERE t.collection_id = c.id),
                       coalesce(json_array_length(c.parts), 0),
                       {PosterOf}
                FROM collection c ORDER BY coalesce(c.sort_key, c.name);
                """)
            .Select(c => new CollectionSummary(c.Id, c.Name, c.TmdbId, (int)c.Owned, (int)c.Parts, c.Poster))
            .ToList();
    }

    public CollectionDetail? Get(long id)
    {
        using var connection = database.Open();
        var row = connection.QuerySingleOrDefault<(long Id, string Name, long? TmdbId, string? Overview, string? Parts, string? Poster, string? Backdrop, string? FetchedAt)?>(
            $"""
            SELECT c.id, c.name, c.tmdb_id, c.overview, c.parts,
                   {PosterOf},
                   {BackdropOf},
                   c.fetched_at
            FROM collection c WHERE c.id = @id;
            """,
            new { id });
        return row is { } r ? new CollectionDetail(r.Id, r.Name, r.TmdbId, r.Overview, ReadParts(r.Parts), r.Poster, r.Backdrop, r.FetchedAt) : null;
    }

    /// <summary>Titres du catalogue rangés dans la collection, du plus ancien au plus récent.</summary>
    public IReadOnlyList<CollectionMember> Members(long id)
    {
        using var connection = database.Open();
        return connection.Query<(long Id, string Title, long? Year, long? TmdbId, string? Poster)>(
                """
                SELECT t.id, t.title, t.year, t.tmdb_id,
                       (SELECT i.rel_path FROM title_image ti JOIN image i ON i.id = ti.image_id WHERE ti.title_id = t.id AND ti.role = 'poster')
                FROM title t WHERE t.collection_id = @id ORDER BY coalesce(t.year, 9999), t.sort_title;
                """,
                new { id })
            .Select(m => new CollectionMember(m.Id, m.Title, (int?)m.Year, m.TmdbId, m.Poster))
            .ToList();
    }

    /// <summary>Collection d'un titre et son identifiant TMDb, ou null.</summary>
    public (long Id, long? TmdbId, bool Fetched)? ForTitle(long titleId)
    {
        using var connection = database.Open();
        return connection.QuerySingleOrDefault<(long, long?, bool)?>(
            "SELECT c.id, c.tmdb_id, c.fetched_at IS NOT NULL FROM title t JOIN collection c ON c.id = t.collection_id WHERE t.id = @titleId;",
            new { titleId });
    }

    /// <summary>
    /// Enregistre la fiche TMDb d'une collection. Si une autre collection du catalogue porte déjà cet identifiant TMDb,
    /// ses titres la rejoignent et elle est fusionnée ; renvoie l'identifiant de la collection gardée.
    /// </summary>
    public long SaveDetails(long id, CollectionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var other = connection.ExecuteScalar<long?>(
            "SELECT id FROM collection WHERE tmdb_id = @SourceId AND id <> @id;",
            new { record.SourceId, id },
            transaction);
        if (other is { } keep)
        {
            connection.Execute("UPDATE title SET collection_id = @keep WHERE collection_id = @id;", new { keep, id }, transaction);
            connection.Execute("DELETE FROM collection WHERE id = @id;", new { id }, transaction);
            id = keep;
        }

        connection.Execute(
            """
            UPDATE collection SET tmdb_id = @SourceId, name = @Name, sort_key = @SortKey, overview = coalesce(@Overview, overview),
                                  parts = @Parts, fetched_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
            WHERE id = @id;
            """,
            new { id, record.SourceId, record.Name, SortKey = SortTitle.Compute(record.Name), record.Overview, Parts = JsonSerializer.Serialize(record.Parts) },
            transaction);
        transaction.Commit();
        return id;
    }

    /// <summary>Collections liées à TMDb dont la fiche n'a jamais été lue (ou est plus ancienne que la date donnée).</summary>
    public IReadOnlyList<(long Id, long TmdbId)> Stale(DateTime? olderThan = null)
    {
        using var connection = database.Open();
        return connection.Query<(long, long)>(
                "SELECT id, tmdb_id FROM collection WHERE tmdb_id IS NOT NULL AND (fetched_at IS NULL OR fetched_at < @limit) ORDER BY id;",
                new { limit = olderThan?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty })
            .ToList();
    }

    /// <summary>Collections sans identifiant TMDb (créées à la main ou reprises d'un autre logiciel), à rechercher par leur nom.</summary>
    public IReadOnlyList<(long Id, string Name)> Unlinked()
    {
        using var connection = database.Open();
        return connection.Query<(long, string)>("SELECT id, name FROM collection WHERE tmdb_id IS NULL ORDER BY id;").ToList();
    }

    public void Rename(long id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using var connection = database.Open();
        connection.Execute("UPDATE collection SET name = @Name, sort_key = @SortKey WHERE id = @id;", new { id, Name = name.Trim(), SortKey = SortTitle.Compute(name) });
    }

    /// <summary>Supprime la collection ; ses titres restent au catalogue, simplement hors collection.</summary>
    public void Delete(long id)
    {
        using var connection = database.Open();
        connection.Execute("DELETE FROM collection WHERE id = @id;", new { id });
    }

    internal static List<CollectionPart> ReadParts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<CollectionPart>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
