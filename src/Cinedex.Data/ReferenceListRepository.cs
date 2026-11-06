using Dapper;

namespace Cinedex.Data;

/// <summary>Une entrée d'une liste de référence, et ce que le catalogue en possède.</summary>
public sealed record ReferenceEntry(int Rank, string? ImdbId, long? TmdbId, string Title, int? Year, double? Rating, int? Votes, long? OwnedTitleId)
{
    public bool IsOwned => OwnedTitleId is not null;
}

/// <summary>Une liste de référence et son état face au catalogue.</summary>
public sealed record ReferenceList(long Id, string Name, string Source, string? Note, DateTimeOffset? UpdatedAt, int Total, int Owned)
{
    public int Missing => Total - Owned;
}

/// <summary>
/// Listes de référence : Top 250 IMDb, palmarès, listes personnelles. Ce qui a de la valeur n'est pas la
/// liste mais ce qu'elle dit du catalogue — combien vous en avez, et lesquels manquent. Le rapprochement se
/// fait sur l'identifiant IMDb, puis sur le numéro TMDb : un titre identique n'est pas une preuve.
/// </summary>
public sealed class ReferenceListRepository(CatalogDatabase database)
{
    /// <summary>Condition de rapprochement entre une entrée de liste (alias <c>re</c>) et un titre (alias <c>t</c>).</summary>
    public const string MatchClause =
        "(re.imdb_id IS NOT NULL AND re.imdb_id = t.imdb_id) OR (re.tmdb_id IS NOT NULL AND re.tmdb_id = t.tmdb_id)";

    public IReadOnlyList<ReferenceList> All()
    {
        using var connection = database.Open();
        return connection.Query<ListRow>(
                $"""
                SELECT rl.id, rl.name, rl.source, rl.note, rl.updated_at,
                       (SELECT count(*) FROM reference_entry re WHERE re.list_id = rl.id) AS total,
                       (SELECT count(*) FROM reference_entry re WHERE re.list_id = rl.id
                        AND EXISTS (SELECT 1 FROM title t WHERE t.is_owned = 1 AND ({MatchClause}))) AS owned
                FROM reference_list rl ORDER BY rl.name COLLATE NOCASE;
                """)
            .Select(r => new ReferenceList(
                r.Id, r.Name, r.Source, r.Note,
                r.Updated_At is null ? null : DateTimeOffset.Parse(r.Updated_At, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                r.Total, r.Owned))
            .ToList();
    }

    public ReferenceList? Find(string name) =>
        All().FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Entrées de la liste, dans l'ordre du classement, avec le titre du catalogue qui leur répond.</summary>
    public IReadOnlyList<ReferenceEntry> Entries(long listId)
    {
        using var connection = database.Open();
        return connection.Query<EntryRow>(
                $"""
                SELECT re.rank, re.imdb_id, re.tmdb_id, re.title, re.year, re.rating, re.votes,
                       (SELECT min(t.id) FROM title t WHERE t.is_owned = 1 AND ({MatchClause})) AS owned_title_id
                FROM reference_entry re WHERE re.list_id = @listId ORDER BY re.rank;
                """,
                new { listId })
            .Select(r => new ReferenceEntry(r.Rank, r.Imdb_Id, r.Tmdb_Id, r.Title, r.Year, r.Rating, r.Votes, r.Owned_Title_Id))
            .ToList();
    }

    /// <summary>Remplace une liste entière (elle est recalculée, pas complétée) et la crée au besoin.</summary>
    public long Replace(string name, string source, string? note, IReadOnlyList<ReferenceEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(entries);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var id = connection.ExecuteScalar<long?>("SELECT id FROM reference_list WHERE name = @name COLLATE NOCASE;", new { name }, transaction)
            ?? connection.ExecuteScalar<long>(
                "INSERT INTO reference_list (name, source) VALUES (@name, @source) RETURNING id;",
                new { name, source },
                transaction);

        connection.Execute(
            "UPDATE reference_list SET source = @source, note = @note, updated_at = @when WHERE id = @id;",
            new { id, source, note, when = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture) },
            transaction);
        connection.Execute("DELETE FROM reference_entry WHERE list_id = @id;", new { id }, transaction);
        foreach (var entry in entries)
        {
            connection.Execute(
                """
                INSERT INTO reference_entry (list_id, rank, imdb_id, tmdb_id, title, year, rating, votes)
                VALUES (@id, @Rank, @ImdbId, @TmdbId, @Title, @Year, @Rating, @Votes);
                """,
                new { id, entry.Rank, entry.ImdbId, entry.TmdbId, entry.Title, entry.Year, entry.Rating, entry.Votes },
                transaction);
        }

        transaction.Commit();
        return id;
    }

    public void Delete(long listId)
    {
        using var connection = database.Open();
        connection.Execute("DELETE FROM reference_list WHERE id = @listId;", new { listId });
    }

    private sealed class ListRow
    {
        public long Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public string? Note { get; set; }

        public string? Updated_At { get; set; }

        public int Total { get; set; }

        public int Owned { get; set; }
    }

    private sealed class EntryRow
    {
        public int Rank { get; set; }

        public string? Imdb_Id { get; set; }

        public long? Tmdb_Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public int? Year { get; set; }

        public double? Rating { get; set; }

        public int? Votes { get; set; }

        public long? Owned_Title_Id { get; set; }
    }
}
