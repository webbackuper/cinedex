using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Dapper;
using Microsoft.Data.Sqlite;
using Cinedex.Core.Localization;

namespace Cinedex.Data;

/// <summary>Écrit une fiche issue d'une source de métadonnées dans le catalogue, selon les options d'import.</summary>
public sealed class MetadataRepository(CatalogDatabase database)
{
    /// <summary>Crée un titre à partir d'une fiche distante ; renvoie son identifiant.</summary>
    public long Create(MetadataRecord record, ImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        // Rien n'interdit qu'une autre entrée porte déjà ce numéro TMDb : Cinedex catalogue des fichiers, et
        // deux sources d'un même film ont chacune leur entrée, que la fusion des doublons regroupe ensuite.
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
            ?? throw new KeyNotFoundException(Tr.F("Titre {0} introuvable.", titleId));
        if (locked != 0)
        {
            return false;
        }

        ApplyCore(connection, transaction, titleId, record, options);
        transaction.Commit();
        return true;
    }

    /// <summary>
    /// Enregistre les notes externes (IMDb, Rotten Tomatoes, Metacritic). Une valeur absente ne remplace pas
    /// une valeur déjà connue. Les titres verrouillés ne sont pas modifiés.
    /// </summary>
    public bool ApplyRatings(long titleId, double? imdbRating, int? imdbVotes, int? tomatometer, int? metascore)
    {
        using var connection = database.Open();
        return connection.Execute(
            """
            UPDATE title SET
                imdb_rating = coalesce(@imdbRating, imdb_rating), imdb_votes = coalesce(@imdbVotes, imdb_votes),
                tomatometer = coalesce(@tomatometer, tomatometer), metascore = coalesce(@metascore, metascore),
                updated_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
            WHERE id = @titleId AND is_locked = 0;
            """,
            new { titleId, imdbRating, imdbVotes, tomatometer, metascore }) > 0;
    }

    /// <summary>Titres ayant un identifiant IMDb (pour l'actualisation des notes en masse).</summary>
    public IReadOnlyList<(long TitleId, string ImdbId)> TitlesWithImdbId()
    {
        using var connection = database.Open();
        return connection.Query<(long, string)>("SELECT id, imdb_id FROM title WHERE imdb_id IS NOT NULL AND is_locked = 0;").ToList();
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
                kind = @Kind, tmdb_id = CASE WHEN @SourceId > 0 THEN @SourceId ELSE tmdb_id END, imdb_id = coalesce(@ImdbId, imdb_id),
                year = coalesce(@Year, year), runtime_minutes = coalesce(@RuntimeMinutes, runtime_minutes),
                certification = coalesce(@Certification, certification),
                countries = coalesce(@Countries, countries), spoken_languages = coalesce(@SpokenLanguages, spoken_languages), studio = coalesce(@Studio, studio),
                web_link = coalesce(@Homepage, web_link), trailer_url = coalesce(@TrailerUrl, trailer_url),
                tmdb_rating = coalesce(@Rating, tmdb_rating), tmdb_votes = coalesce(@Votes, tmdb_votes), is_adult = max(is_adult, @IsAdult),
                series_status = coalesce(@SeriesStatus, series_status),
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

        if (options.UpdateGenres && r.GenreSourceIds.Count == 0 && r.GenreNames.Count > 0)
        {
            connection.Execute("DELETE FROM title_genre WHERE title_id = @titleId;", new { titleId }, transaction);
            foreach (var name in r.GenreNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()))
            {
                // Rapprochement par libellé ou code (« Drame », « drama ») ; un genre inconnu est ajouté au catalogue.
                var genreId = connection.ExecuteScalar<long?>(
                    "SELECT id FROM genre WHERE label = @name COLLATE NOCASE OR code = @name COLLATE NOCASE OR replace(code, '_', ' ') = @name COLLATE NOCASE;",
                    new { name },
                    transaction)
                    ?? connection.ExecuteScalar<long>(
                        "INSERT INTO genre (code, label) VALUES (@code, @name) ON CONFLICT (code) DO UPDATE SET label = genre.label RETURNING id;",
                        new { name, code = name.ToLowerInvariant().Replace(' ', '_') },
                        transaction);
                connection.Execute("INSERT OR IGNORE INTO title_genre (title_id, genre_id) VALUES (@titleId, @genreId);", new { titleId, genreId }, transaction);
            }
        }

        if (options.UpdateCredits && r.Credits.Count > 0)
        {
            ReplaceCredits(connection, transaction, titleId, r.Credits, options.MaxActors);
        }

        if (options.UpdateCollection && r.Collection is { } collection)
        {
            var collectionId = collection.SourceId > 0
                ? connection.ExecuteScalar<long>(
                    """
                    INSERT INTO collection (name, sort_key, tmdb_id) VALUES (@Name, @SortKey, @SourceId)
                    ON CONFLICT (tmdb_id) DO UPDATE SET name = collection.name
                    RETURNING id;
                    """,
                    new { collection.Name, SortKey = SortTitle.Compute(collection.Name), collection.SourceId },
                    transaction)
                : connection.ExecuteScalar<long?>("SELECT id FROM collection WHERE name = @Name COLLATE NOCASE;", new { collection.Name }, transaction)
                  ?? connection.ExecuteScalar<long>(
                      "INSERT INTO collection (name, sort_key) VALUES (@Name, @SortKey) RETURNING id;",
                      new { collection.Name, SortKey = SortTitle.Compute(collection.Name) },
                      transaction);
            connection.Execute("UPDATE title SET collection_id = @collectionId WHERE id = @titleId;", new { collectionId, titleId }, transaction);
        }

        // Les mots-clés vont toujours dans leur propre table : ils servent à rapprocher les films. Les verser
        // en plus dans les tags reste une option, car les tags sont le classement personnel de l'utilisateur.
        var keywords = r.Keywords.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToList();
        if (keywords.Count > 0)
        {
            connection.Execute("DELETE FROM title_keyword WHERE title_id = @titleId;", new { titleId }, transaction);
            foreach (var keyword in keywords)
            {
                var keywordId = connection.ExecuteScalar<long>(
                    "INSERT INTO keyword (name) VALUES (@keyword) ON CONFLICT (name) DO UPDATE SET name = keyword.name RETURNING id;",
                    new { keyword },
                    transaction);
                connection.Execute("INSERT OR IGNORE INTO title_keyword (title_id, keyword_id) VALUES (@titleId, @keywordId);", new { titleId, keywordId }, transaction);
            }
        }

        if (options.ImportKeywordsAsTags)
        {
            foreach (var keyword in keywords)
            {
                var tagId = connection.ExecuteScalar<long>(
                    "INSERT INTO tag (name) VALUES (@keyword) ON CONFLICT (name) DO UPDATE SET name = tag.name RETURNING id;",
                    new { keyword },
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

            // Personne sans identifiant (.nfo) : rapprochée par nom parmi celles sans identifiant TMDb.
            var personId = credit.SourcePersonId > 0
                ? connection.ExecuteScalar<long>(
                    """
                    INSERT INTO person (name, tmdb_id, profile_path) VALUES (@Name, @SourcePersonId, @ProfilePath)
                    ON CONFLICT (tmdb_id) DO UPDATE SET name = excluded.name, profile_path = coalesce(excluded.profile_path, person.profile_path)
                    RETURNING id;
                    """,
                    new { credit.Name, credit.SourcePersonId, credit.ProfilePath },
                    transaction)
                : connection.ExecuteScalar<long?>("SELECT id FROM person WHERE name = @Name AND tmdb_id IS NULL;", new { credit.Name }, transaction)
                  ?? connection.ExecuteScalar<long>("INSERT INTO person (name) VALUES (@Name) RETURNING id;", new { credit.Name }, transaction);
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
