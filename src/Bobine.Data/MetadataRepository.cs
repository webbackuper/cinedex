using Bobine.Core.Metadata;
using Bobine.Core.Titles;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Bobine.Data;

/// <summary>Écrit une fiche issue d'une source de métadonnées dans le catalogue, selon les options d'import.</summary>
public sealed class MetadataRepository(CatalogDatabase database)
{
    /// <summary>Crée un titre à partir d'une fiche distante ; renvoie son identifiant.</summary>
    public long Create(MetadataRecord record, ImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var existing = FindBySource(connection, transaction, record.Kind, record.SourceId);
        if (existing is not null)
        {
            throw new DuplicateTitleException(existing.Value);
        }

        var id = connection.ExecuteScalar<long>(
            "INSERT INTO title (kind, title, sort_title) VALUES (@Kind, @Title, @SortTitle) RETURNING id;",
            new { Kind = record.Kind == TitleKind.Series ? "series" : "movie", record.Title, SortTitle = SortTitle.Compute(record.Title) },
            transaction);
        connection.Execute(
            "INSERT INTO copy (title_id, number) VALUES (@id, (SELECT coalesce(max(number), 0) + 1 FROM copy));",
            new { id },
            transaction);
        ApplyCore(connection, transaction, id, record, options with { UpdateTitle = true });
        transaction.Commit();
        return id;
    }

    /// <summary>Met à jour un titre existant. Un titre verrouillé n'est pas modifié (renvoie false).</summary>
    public bool Apply(long titleId, MetadataRecord record, ImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(options);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var locked = connection.ExecuteScalar<long?>("SELECT is_locked FROM title WHERE id = @titleId;", new { titleId }, transaction)
            ?? throw new KeyNotFoundException($"Titre {titleId} introuvable.");
        if (locked != 0)
        {
            return false;
        }

        var other = FindBySource(connection, transaction, record.Kind, record.SourceId);
        if (other is not null && other != titleId)
        {
            throw new DuplicateTitleException(other.Value);
        }

        ApplyCore(connection, transaction, titleId, record, options);
        transaction.Commit();
        return true;
    }

    public long? FindBySource(TitleKind kind, long sourceId)
    {
        using var connection = database.Open();
        return FindBySource(connection, null, kind, sourceId);
    }

    /// <summary>Identifiants TMDb des titres déjà liés, pour la mise à jour par lot.</summary>
    public IReadOnlyList<(long TitleId, TitleKind Kind, long SourceId, bool Locked)> LinkedTitles()
    {
        using var connection = database.Open();
        return connection.Query<(long, string, long, long)>("SELECT id, kind, tmdb_id, is_locked FROM title WHERE tmdb_id IS NOT NULL ORDER BY sort_title;")
            .Select(r => (r.Item1, r.Item2 == "series" ? TitleKind.Series : TitleKind.Movie, r.Item3, r.Item4 != 0))
            .ToList();
    }

    private static long? FindBySource(SqliteConnection connection, SqliteTransaction? transaction, TitleKind kind, long sourceId) =>
        connection.ExecuteScalar<long?>(
            "SELECT id FROM title WHERE kind = @kind AND tmdb_id = @sourceId;",
            new { kind = kind == TitleKind.Series ? "series" : "movie", sourceId },
            transaction);

    private static void ApplyCore(SqliteConnection connection, SqliteTransaction transaction, long titleId, MetadataRecord r, ImportOptions options)
    {
        connection.Execute(
            """
            UPDATE title SET
                kind = @Kind, tmdb_id = @SourceId, imdb_id = coalesce(@ImdbId, imdb_id),
                year = coalesce(@Year, year), runtime_minutes = coalesce(@RuntimeMinutes, runtime_minutes),
                certification = coalesce(@Certification, certification),
                countries = @Countries, spoken_languages = @SpokenLanguages, studio = coalesce(@Studio, studio),
                web_link = coalesce(@Homepage, web_link), trailer_url = coalesce(@TrailerUrl, trailer_url),
                tmdb_rating = @Rating, tmdb_votes = @Votes, is_adult = max(is_adult, @IsAdult),
                series_status = @SeriesStatus,
                metadata_fetched_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
                updated_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
            WHERE id = @TitleId;
            """,
            new
            {
                TitleId = titleId,
                Kind = r.Kind == TitleKind.Series ? "series" : "movie",
                r.SourceId,
                r.ImdbId,
                r.Year,
                r.RuntimeMinutes,
                r.Certification,
                Countries = r.Countries.Count == 0 ? null : string.Join(',', r.Countries),
                SpokenLanguages = r.SpokenLanguages.Count == 0 ? null : string.Join(',', r.SpokenLanguages),
                r.Studio,
                r.Homepage,
                r.TrailerUrl,
                r.Rating,
                r.Votes,
                r.IsAdult,
                SeriesStatus = r.Kind == TitleKind.Series ? r.SeriesStatus : null,
            },
            transaction);

        if (options.UpdateTitle)
        {
            connection.Execute(
                "UPDATE title SET title = @Title, original_title = @OriginalTitle, sort_title = @SortTitle WHERE id = @titleId;",
                new { titleId, r.Title, r.OriginalTitle, SortTitle = SortTitle.Compute(r.Title) },
                transaction);
        }

        if (options.UpdateOverview)
        {
            connection.Execute(
                "UPDATE title SET overview = coalesce(@Overview, overview), tagline = coalesce(@Tagline, tagline) WHERE id = @titleId;",
                new { titleId, r.Overview, r.Tagline },
                transaction);
        }

        if (options.UpdateGenres && r.GenreSourceIds.Count > 0)
        {
            connection.Execute("DELETE FROM title_genre WHERE title_id = @titleId;", new { titleId }, transaction);
            connection.Execute(
                "INSERT OR IGNORE INTO title_genre (title_id, genre_id) SELECT @titleId, id FROM genre WHERE tmdb_id = @g;",
                r.GenreSourceIds.Select(g => new { titleId, g }),
                transaction);
        }

        if (options.UpdateCredits)
        {
            ReplaceCredits(connection, transaction, titleId, r.Credits, options.MaxActors);
        }

        if (options.UpdateCollection && r.Collection is { } collection)
        {
            var collectionId = connection.ExecuteScalar<long>(
                """
                INSERT INTO collection (name, sort_key, tmdb_id) VALUES (@Name, @SortKey, @SourceId)
                ON CONFLICT (tmdb_id) DO UPDATE SET name = excluded.name
                RETURNING id;
                """,
                new { collection.Name, SortKey = SortTitle.Compute(collection.Name), collection.SourceId },
                transaction);
            connection.Execute("UPDATE title SET collection_id = @collectionId WHERE id = @titleId;", new { collectionId, titleId }, transaction);
        }

        if (options.ImportKeywordsAsTags)
        {
            foreach (var keyword in r.Keywords.Where(k => !string.IsNullOrWhiteSpace(k)).Take(30))
            {
                var tagId = connection.ExecuteScalar<long>(
                    "INSERT INTO tag (name) VALUES (@keyword) ON CONFLICT (name) DO UPDATE SET name = tag.name RETURNING id;",
                    new { keyword = keyword.Trim() },
                    transaction);
                connection.Execute("INSERT OR IGNORE INTO title_tag (title_id, tag_id) VALUES (@titleId, @tagId);", new { titleId, tagId }, transaction);
            }
        }
    }

    private static void ReplaceCredits(SqliteConnection connection, SqliteTransaction transaction, long titleId, IReadOnlyList<MetadataCredit> credits, int maxActors)
    {
        connection.Execute("DELETE FROM credit WHERE title_id = @titleId;", new { titleId }, transaction);
        var actors = 0;
        foreach (var credit in credits)
        {
            if (credit.Role == CreditRole.Actor && ++actors > maxActors)
            {
                continue;
            }

            var personId = connection.ExecuteScalar<long>(
                """
                INSERT INTO person (name, tmdb_id) VALUES (@Name, @SourcePersonId)
                ON CONFLICT (tmdb_id) DO UPDATE SET name = excluded.name
                RETURNING id;
                """,
                new { credit.Name, credit.SourcePersonId },
                transaction);
            connection.Execute(
                """
                INSERT INTO credit (title_id, person_id, role, character, is_voice, position)
                VALUES (@titleId, @personId, @Role, @Character, @IsVoice, @Order);
                """,
                new
                {
                    titleId,
                    personId,
                    Role = credit.Role.ToString().ToLowerInvariant(),
                    credit.Character,
                    IsVoice = credit.Character?.Contains("(voice)", StringComparison.OrdinalIgnoreCase) == true,
                    credit.Order,
                },
                transaction);
        }
    }
}

public sealed class DuplicateTitleException(long existingTitleId)
    : Exception("Ce titre est déjà dans le catalogue.")
{
    public long ExistingTitleId { get; } = existingTitleId;
}
