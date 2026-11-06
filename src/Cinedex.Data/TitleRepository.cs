using System.Globalization;
using System.Text;
using Cinedex.Core.Titles;
using Dapper;
using Microsoft.Data.Sqlite;
using Cinedex.Core.Localization;

namespace Cinedex.Data;

/// <summary>Lecture et écriture des titres, de leur exemplaire principal, des visionnages et des prêts.</summary>
public sealed partial class TitleRepository(CatalogDatabase database)
{
    // Exemplaire principal = le plus ancien. Les champs dérivés servent aux filtres SD / HD / UHD / 3D / fichiers.
    private const string BaseSelect = """
        SELECT t.id, t.kind, t.title, t.year, t.runtime_minutes, t.user_rating, t.is_owned, t.is_favorite,
               EXISTS (SELECT 1 FROM viewing v WHERE v.title_id = t.id) AS is_seen,
               c.number, m.label AS medium,
               (SELECT l.borrower FROM loan l WHERE l.copy_id = c.id AND l.returned_at IS NULL) AS borrower,
               (SELECT i.rel_path FROM title_image ti JOIN image i ON i.id = ti.image_id WHERE ti.title_id = t.id AND ti.role = 'poster') AS poster_rel,
               t.imdb_rating, substr(t.overview, 1, 600) AS overview,
               t.original_title, t.studio, t.certification, substr(t.added_at, 1, 10) AS added_on,
               (SELECT group_concat(DISTINCT mf.volume_label) FROM media_file mf JOIN copy cm ON cm.id = mf.copy_id
                WHERE cm.title_id = t.id AND mf.volume_label IS NOT NULL) AS media_id,
               (SELECT co.name FROM collection co WHERE co.id = t.collection_id) AS collection_name,
               (SELECT group_concat(g.label, ', ') FROM title_genre tg JOIN genre g ON g.id = tg.genre_id WHERE tg.title_id = t.id) AS genres
        FROM title t
        LEFT JOIN copy c ON c.id = (SELECT min(c2.id) FROM copy c2 WHERE c2.title_id = t.id)
        LEFT JOIN ref_list m ON m.id = c.medium_id
        """;

    private const string MaxHeight = "(SELECT max(mf.height) FROM media_file mf JOIN copy cx ON cx.id = mf.copy_id WHERE cx.title_id = t.id)";

    private const string Definition = $"""
        CASE
            WHEN {MaxHeight} >= 2000 OR m.code = 'uhd_bluray' THEN 3
            WHEN {MaxHeight} >= 700 OR m.code IN ('bluray', 'bluray_3d', 'hd_dvd') THEN 2
            WHEN {MaxHeight} IS NOT NULL OR m.code IN ('dvd', 'dvd_r', 'vhs', 'vcd', 'laserdisc') THEN 1
            ELSE 0
        END
        """;

    /// <summary>Disques du catalogue (MediaID), par ordre alphabétique, tels que le bandeau gauche les liste.</summary>
    public IReadOnlyList<string> MediaIds()
    {
        using var connection = database.Open();
        return connection.Query<string>(
                """
                SELECT DISTINCT mf.volume_label FROM media_file mf
                WHERE mf.volume_label IS NOT NULL AND trim(mf.volume_label) <> ''
                ORDER BY mf.volume_label COLLATE NOCASE;
                """)
            .ToList();
    }

    /// <summary>
    /// Un titre « sans infos » : ni synopsis ni genre, ce que laisse une fiche jamais rapatriée. On juge le contenu
    /// plutôt que la date de rapatriement, qu'un .nfo lu au scan pose aussi sans rien apporter de descriptif.
    /// L'alias de la table doit être <c>t</c>.
    /// </summary>
    public const string WithoutMetadata =
        "coalesce(t.overview, '') = '' AND NOT EXISTS (SELECT 1 FROM title_genre tg WHERE tg.title_id = t.id)";

    /// <summary>Titres (films et séries) sans synopsis ni genre, par ordre de tri : ceux qui restent à compléter.</summary>
    public IReadOnlyList<long> WithoutMetadataIds()
    {
        using var connection = database.Open();
        return connection.Query<long>($"SELECT t.id FROM title t WHERE {WithoutMetadata} ORDER BY t.sort_title, t.id;").ToList();
    }

    /// <summary>
    /// Titres rattachés à TMDb dont la bande-annonce manque. Une mise à jour TMDb la rapatrie : inutile de
    /// repasser sur les fiches qui en ont déjà une, ni sur celles que TMDb ne connaît pas.
    /// </summary>
    public IReadOnlyList<long> WithoutTrailerIds()
    {
        using var connection = database.Open();
        return connection.Query<long>(
            "SELECT t.id FROM title t WHERE t.tmdb_id IS NOT NULL AND coalesce(t.trailer_url, '') = '' AND t.is_locked = 0 ORDER BY t.sort_title, t.id;").ToList();
    }

    /// <summary>
    /// Titres rattachés à TMDb sans aucun mot-clé. Les mots-clés n'étaient pas gardés avant la 0.6 : une mise
    /// à jour TMDb les rapatrie, et c'est ce que rattrape cette liste.
    /// </summary>
    public IReadOnlyList<long> WithoutKeywordIds()
    {
        using var connection = database.Open();
        return connection.Query<long>(
            """
            SELECT t.id FROM title t
            WHERE t.tmdb_id IS NOT NULL AND t.is_locked = 0
              AND NOT EXISTS (SELECT 1 FROM title_keyword tk WHERE tk.title_id = t.id)
            ORDER BY t.sort_title, t.id;
            """).ToList();
    }

    /// <summary>
    /// Clé de rapprochement d'un titre : genre, titre normalisé (sans accents, casse ni ponctuation) et année.
    /// Même règle que la recherche de doublons de la maintenance.
    /// </summary>
    public readonly record struct TitleKey(TitleKind Kind, string Title, int? Year);

    /// <summary>
    /// Clés de tous les titres du catalogue, lues en une fois : l'import s'en sert pour rattacher un second fichier
    /// au film déjà présent au lieu d'en créer un double.
    /// </summary>
    public IReadOnlyList<(long Id, TitleKey Key)> TitleKeys()
    {
        using var connection = database.Open();
        return connection.Query<(long Id, string Title, long? Year, string Kind)>("SELECT id, title, year, kind FROM title;")
            .Select(r => (r.Id, new TitleKey(
                r.Kind == "series" ? TitleKind.Series : TitleKind.Movie,
                MaintenanceRepository.NormalizeTitle(r.Title),
                (int?)r.Year)))
            .ToList();
    }

    /// <summary>Clé de rapprochement d'un titre et d'une année bruts (nom de fichier, .nfo).</summary>
    public static TitleKey KeyOf(TitleKind kind, string title, int? year) =>
        new(kind, MaintenanceRepository.NormalizeTitle(title), year);

    public long Add(NewTitle title)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(title.Title);

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var id = connection.ExecuteScalar<long>(
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
            },
            transaction);
        connection.Execute(
            "INSERT INTO copy (title_id, number) VALUES (@id, (SELECT coalesce(max(number), 0) + 1 FROM copy));",
            new { id },
            transaction);
        transaction.Commit();
        return id;
    }

    public IReadOnlyList<TitleSummary> ListAll() => Query(TitleQuery.Default);

    /// <summary>Recherche plein texte (titres et synopsis), insensible aux accents.</summary>
    public IReadOnlyList<TitleSummary> Search(string text) => Query(new TitleQuery(text));

    public IReadOnlyList<TitleSummary> Query(TitleQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var sql = new StringBuilder(BaseSelect);
        var where = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            sql.Append(" JOIN title_fts f ON f.rowid = t.id");
            where.Add("title_fts MATCH @Fts");
            parameters.Add("Fts", ToFtsQuery(query.Text));
        }

        if (FilterClause(query.Filter) is { } clause)
        {
            where.Add(clause);
        }

        if (query.HideAdult)
        {
            where.Add("t.is_adult = 0");
        }

        if (query.Criteria is { IsEmpty: false } criteria)
        {
            AddCriteria(criteria, where, parameters);
        }

        if (query.Rules is { IsEmpty: false } rules && RuleClause(rules, parameters) is { } rulesClause)
        {
            where.Add(rulesClause);
        }

        if (query.Ids is { } ids)
        {
            // Mise à jour ciblée de quelques lignes de la liste (favori, vu, prêt…), sans tout relire.
            where.Add("t.id IN (SELECT value FROM json_each(@Ids))");
            parameters.Add("Ids", System.Text.Json.JsonSerializer.Serialize(ids));
        }

        if (where.Count > 0)
        {
            sql.Append(" WHERE ").AppendJoin(" AND ", where);
        }

        sql.Append(" ORDER BY ").Append(OrderBy(query.Sort, query.Descending)).Append(';');

        using var connection = database.Open();
        return connection.Query<Row>(sql.ToString(), parameters).Select(ToSummary).ToList();
    }

    /// <summary>Tire un titre au hasard parmi le résultat de la requête, éventuellement parmi les non vus seulement.</summary>
    public TitleSummary? PickRandom(TitleQuery query, bool unseenOnly, Random? random = null)
    {
        var candidates = Query(query).Where(t => !unseenOnly || !t.IsSeen).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

#pragma warning disable CA5394 // Tirage d'agrément, pas d'usage cryptographique.
        return candidates[(random ?? Random.Shared).Next(candidates.Count)];
#pragma warning restore CA5394
    }

    public TitleDetail? Get(long id)
    {
        using var connection = database.Open();
        var row = connection.QuerySingleOrDefault<DetailRow>(
            """
            SELECT t.*, (SELECT count(*) FROM viewing v WHERE v.title_id = t.id) AS view_count,
                   (SELECT max(v.viewed_at) FROM viewing v WHERE v.title_id = t.id) AS last_seen_at
            FROM title t WHERE t.id = @id;
            """,
            new { id });
        if (row is null)
        {
            return null;
        }

        var genres = connection.Query<string>(
            "SELECT g.code FROM title_genre tg JOIN genre g ON g.id = tg.genre_id WHERE tg.title_id = @id ORDER BY g.label;",
            new { id }).ToList();

        var tags = connection.Query<string>(
            "SELECT g.name FROM title_tag tt JOIN tag g ON g.id = tt.tag_id WHERE tt.title_id = @id ORDER BY g.name;",
            new { id }).ToList();
        var groups = connection.Query<string>(
            "SELECT g.name FROM title_group tg JOIN user_group g ON g.id = tg.group_id WHERE tg.title_id = @id ORDER BY g.name;",
            new { id }).ToList();
        var collectionName = row.Collection_Id is null
            ? null
            : connection.ExecuteScalar<string?>("SELECT name FROM collection WHERE id = @Collection_Id;", new { row.Collection_Id });

        var copy = connection.QuerySingleOrDefault<CopyRow>(
            """
            SELECT c.id, c.number, c.custom_number, c.location, c.disc_count, c.ean, c.price_cents, c.is_3d,
                   m.code AS medium_code, k.code AS case_code, v.code AS version_code
            FROM copy c
            LEFT JOIN ref_list m ON m.id = c.medium_id
            LEFT JOIN ref_list k ON k.id = c.case_id
            LEFT JOIN ref_list v ON v.id = c.version_id
            WHERE c.title_id = @id ORDER BY c.id LIMIT 1;
            """,
            new { id });

        Loan? loan = null;
        if (copy is not null)
        {
            loan = connection.QuerySingleOrDefault<LoanRow>(
                "SELECT id, borrower, loaned_at, returned_at FROM loan WHERE copy_id = @Id AND returned_at IS NULL;",
                new { copy.Id }) is { } l ? ToLoan(l) : null;
        }

        return new TitleDetail
        {
            Id = row.Id,
            Kind = row.Kind == "series" ? TitleKind.Series : TitleKind.Movie,
            Title = row.Title,
            OriginalTitle = row.Original_Title,
            AlternativeTitle = row.Alternative_Title,
            Year = (int?)row.Year,
            RuntimeMinutes = (int?)row.Runtime_Minutes,
            Tagline = row.Tagline,
            Overview = row.Overview,
            Certification = row.Certification,
            Countries = row.Countries,
            SpokenLanguages = row.Spoken_Languages,
            Studio = row.Studio,
            WebLink = row.Web_Link,
            TrailerUrl = row.Trailer_Url,
            TmdbId = row.Tmdb_Id,
            ImdbId = row.Imdb_Id,
            TmdbRating = row.Tmdb_Rating,
            ImdbRating = row.Imdb_Rating,
            ImdbVotes = (int?)row.Imdb_Votes,
            Tomatometer = (int?)row.Tomatometer,
            Metascore = (int?)row.Metascore,
            UserRating = row.User_Rating,
            Comments = row.Comments,
            IsOwned = row.Is_Owned != 0,
            IsFavorite = row.Is_Favorite != 0,
            IsShortlisted = row.Is_Shortlisted != 0,
            IsLocked = row.Is_Locked != 0,
            IsAdult = row.Is_Adult != 0,
            AddedAt = ParseTimestamp(row.Added_At),
            GenreCodes = genres,
            Tags = tags,
            Groups = groups,
            CollectionId = row.Collection_Id,
            CollectionName = collectionName,
            ViewCount = (int)row.View_Count,
            LastSeenAt = row.Last_Seen_At is null ? null : ParseTimestamp(row.Last_Seen_At),
            CurrentLoan = loan,
            Copy = copy is null ? null : new CopyInfo
            {
                Id = copy.Id,
                Number = (int?)copy.Number,
                CustomNumber = copy.Custom_Number,
                MediumCode = copy.Medium_Code,
                CaseCode = copy.Case_Code,
                VersionCode = copy.Version_Code,
                Location = copy.Location,
                DiscCount = (int?)copy.Disc_Count,
                Ean = copy.Ean,
                Price = copy.Price_Cents is null ? null : copy.Price_Cents.Value / 100m,
                Is3D = copy.Is_3d != 0,
            },
        };
    }

    /// <summary>
    /// Vide une fiche de tout ce qui vient d'une source extérieure (TMDb, OMDb, notes IMDb, .nfo) : descriptif,
    /// identifiants, notes des sites, genres, distribution, images et collection. Sont gardés le titre, le genre de
    /// fiche, ce que l'utilisateur a saisi (sa note, ses tags, ses groupes, ses commentaires, favori, vu, prêts) et
    /// les fichiers vidéo avec leurs propriétés MediaInfo.
    /// </summary>
    public void ClearMetadata(long titleId)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute(
            """
            UPDATE title SET
                original_title = NULL, alternative_title = NULL, year = NULL, runtime_minutes = NULL,
                tagline = NULL, overview = NULL, overview_long = NULL, certification = NULL,
                countries = NULL, spoken_languages = NULL, studio = NULL, web_link = NULL, trailer_url = NULL,
                tmdb_id = NULL, imdb_id = NULL, tmdb_rating = NULL, tmdb_votes = NULL,
                imdb_rating = NULL, imdb_votes = NULL, metascore = NULL, tomatometer = NULL, popcornmeter = NULL,
                imdb_top250 = NULL, awards = NULL, series_status = NULL, collection_id = NULL,
                is_adult = 0, metadata_fetched_at = NULL,
                updated_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
            WHERE id = @titleId;
            """,
            new { titleId },
            transaction);
        connection.Execute("DELETE FROM title_genre WHERE title_id = @titleId;", new { titleId }, transaction);
        connection.Execute("DELETE FROM credit WHERE title_id = @titleId;", new { titleId }, transaction);
        connection.Execute("DELETE FROM title_image WHERE title_id = @titleId;", new { titleId }, transaction);
        connection.Execute("DELETE FROM match_review WHERE title_id = @titleId;", new { titleId }, transaction);
        transaction.Commit();
    }

    /// <summary>Enregistre les champs éditables d'une fiche et de son exemplaire principal.</summary>
    public void Update(TitleDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail.Title);
        if (detail.UserRating is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(detail), "La note personnelle doit être comprise entre 0 et 10.");
        }

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var updated = connection.Execute(
            """
            UPDATE title SET
                kind = @Kind, title = @Title, original_title = @OriginalTitle, alternative_title = @AlternativeTitle,
                sort_title = @SortTitle, year = @Year, runtime_minutes = @RuntimeMinutes, tagline = @Tagline,
                overview = @Overview, certification = @Certification, countries = @Countries,
                spoken_languages = @SpokenLanguages, studio = @Studio, web_link = @WebLink, trailer_url = @TrailerUrl,
                tmdb_id = @TmdbId, imdb_id = @ImdbId, user_rating = @UserRating, comments = @Comments,
                tmdb_rating = @TmdbRating, imdb_rating = @ImdbRating, imdb_votes = @ImdbVotes,
                tomatometer = @Tomatometer, metascore = @Metascore,
                is_owned = @IsOwned, is_favorite = @IsFavorite, is_shortlisted = @IsShortlisted,
                is_locked = @IsLocked, is_adult = @IsAdult, collection_id = @CollectionId,
                updated_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
            WHERE id = @Id;
            """,
            new
            {
                detail.Id,
                Kind = ToDb(detail.Kind),
                Title = detail.Title.Trim(),
                OriginalTitle = Blank(detail.OriginalTitle),
                AlternativeTitle = Blank(detail.AlternativeTitle),
                SortTitle = SortTitle.Compute(detail.Title),
                detail.Year,
                detail.RuntimeMinutes,
                Tagline = Blank(detail.Tagline),
                Overview = Blank(detail.Overview),
                Certification = Blank(detail.Certification),
                Countries = Blank(detail.Countries),
                SpokenLanguages = Blank(detail.SpokenLanguages),
                Studio = Blank(detail.Studio),
                WebLink = Blank(detail.WebLink),
                TrailerUrl = Blank(detail.TrailerUrl),
                detail.TmdbId,
                ImdbId = Blank(detail.ImdbId),
                detail.UserRating,
                detail.TmdbRating,
                detail.ImdbRating,
                detail.ImdbVotes,
                detail.Tomatometer,
                detail.Metascore,
                Comments = Blank(detail.Comments),
                detail.IsOwned,
                detail.IsFavorite,
                detail.IsShortlisted,
                detail.IsLocked,
                detail.IsAdult,
                detail.CollectionId,
            },
            transaction);
        if (updated == 0)
        {
            throw new KeyNotFoundException(Tr.F("Titre {0} introuvable.", detail.Id));
        }

        connection.Execute("DELETE FROM title_genre WHERE title_id = @Id;", new { detail.Id }, transaction);
        connection.Execute(
            "INSERT INTO title_genre (title_id, genre_id) SELECT @Id, id FROM genre WHERE code = @Code;",
            detail.GenreCodes.Distinct(StringComparer.Ordinal).Select(code => new { detail.Id, Code = code }),
            transaction);

        ReplaceLabels(connection, transaction, Label.Tag, detail.Id, detail.Tags);
        ReplaceLabels(connection, transaction, Label.Group, detail.Id, detail.Groups);

        if (detail.Copy is { } copy)
        {
            connection.Execute(
                """
                UPDATE copy SET
                    number = @Number, custom_number = @CustomNumber, location = @Location, disc_count = @DiscCount,
                    ean = @Ean, price_cents = @PriceCents, is_3d = @Is3D,
                    medium_id = (SELECT id FROM ref_list WHERE kind = 'medium' AND code = @MediumCode),
                    case_id = (SELECT id FROM ref_list WHERE kind = 'case' AND code = @CaseCode),
                    version_id = (SELECT id FROM ref_list WHERE kind = 'version' AND code = @VersionCode)
                WHERE id = @Id AND title_id = @TitleId;
                """,
                new
                {
                    copy.Id,
                    TitleId = detail.Id,
                    copy.Number,
                    CustomNumber = Blank(copy.CustomNumber),
                    Location = Blank(copy.Location),
                    copy.DiscCount,
                    Ean = Blank(copy.Ean),
                    PriceCents = copy.Price is null ? (long?)null : (long)decimal.Round(copy.Price.Value * 100m),
                    copy.Is3D,
                    copy.MediumCode,
                    copy.CaseCode,
                    copy.VersionCode,
                },
                transaction);
        }

        transaction.Commit();
    }

    /// <summary>Supprime des fiches. Les fichiers vidéo sur disque ne sont jamais touchés ici.</summary>
    public int Delete(IEnumerable<long> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var count = connection.Execute("DELETE FROM title WHERE id = @id;", ids.Select(id => new { id }), transaction);
        transaction.Commit();
        return count;
    }

    /// <summary>Marque comme vu (ajoute un visionnage) ou non vu (efface l'historique de visionnage).</summary>
    public void SetSeen(IEnumerable<long> ids, bool seen, DateTimeOffset? when = null)
    {
        ArgumentNullException.ThrowIfNull(ids);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var id in ids)
        {
            if (seen)
            {
                connection.Execute(
                    """
                    INSERT INTO viewing (title_id, viewed_at, source)
                    SELECT @id, @When, 'manual' WHERE NOT EXISTS (SELECT 1 FROM viewing WHERE title_id = @id);
                    """,
                    new { id, When = when is null ? null : FormatTimestamp(when.Value) },
                    transaction);
            }
            else
            {
                connection.Execute("DELETE FROM viewing WHERE title_id = @id;", new { id }, transaction);
            }
        }

        transaction.Commit();
    }

    public void SetFavorite(IEnumerable<long> ids, bool favorite) => SetFlag(ids, "is_favorite", favorite);

    public void SetOwned(IEnumerable<long> ids, bool owned) => SetFlag(ids, "is_owned", owned);

    public void SetShortlisted(IEnumerable<long> ids, bool shortlisted) => SetFlag(ids, "is_shortlisted", shortlisted);

    /// <summary>Prête l'exemplaire principal. Un exemplaire déjà prêté ne peut pas l'être une seconde fois.</summary>
    public void Lend(long titleId, string borrower, DateTimeOffset when)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(borrower);
        using var connection = database.Open();
        var copyId = MainCopyId(connection, titleId);
        try
        {
            connection.Execute(
                "INSERT INTO loan (copy_id, borrower, loaned_at) VALUES (@copyId, @Borrower, @When);",
                new { copyId, Borrower = borrower.Trim(), When = FormatTimestamp(when) });
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("Cet exemplaire est déjà prêté.", ex);
        }
    }

    public void Return(long titleId, DateTimeOffset when)
    {
        using var connection = database.Open();
        var copyId = MainCopyId(connection, titleId);
        connection.Execute(
            "UPDATE loan SET returned_at = @When WHERE copy_id = @copyId AND returned_at IS NULL;",
            new { copyId, When = FormatTimestamp(when) });
    }

    public IReadOnlyList<Loan> LoanHistory(long titleId)
    {
        using var connection = database.Open();
        return connection.Query<LoanRow>(
                """
                SELECT l.id, l.borrower, l.loaned_at, l.returned_at
                FROM loan l JOIN copy c ON c.id = l.copy_id
                WHERE c.title_id = @titleId ORDER BY l.loaned_at DESC;
                """,
                new { titleId })
            .Select(ToLoan)
            .ToList();
    }

    /// <summary>Emprunteurs déjà connus, du plus récent au plus ancien (suggestions de saisie).</summary>
    public IReadOnlyList<string> Borrowers()
    {
        using var connection = database.Open();
        return connection.Query<string>("SELECT borrower FROM loan GROUP BY borrower COLLATE NOCASE ORDER BY max(id) DESC LIMIT 200;").ToList();
    }

    /// <summary>Efface une ligne de l'historique des prêts (saisie erronée).</summary>
    public void DeleteLoan(long loanId)
    {
        using var connection = database.Open();
        connection.Execute("DELETE FROM loan WHERE id = @loanId;", new { loanId });
    }

    /// <summary>Dernier emprunteur saisi, pour pré-remplir le prochain prêt.</summary>
    public string? LastBorrower()
    {
        using var connection = database.Open();
        return connection.ExecuteScalar<string?>("SELECT borrower FROM loan ORDER BY id DESC LIMIT 1;");
    }

    public IReadOnlyList<Genre> Genres()
    {
        using var connection = database.Open();
        return connection.Query<(string Code, string Label)>("SELECT code, label FROM genre ORDER BY label;")
            .Select(g => new Genre(g.Code, g.Label))
            .ToList();
    }

    public IReadOnlyList<RefItem> RefItems(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        using var connection = database.Open();
        return connection.Query<(string Kind, string Code, string Label)>(
                "SELECT kind, code, label FROM ref_list WHERE kind = @kind ORDER BY sort_order, label;",
                new { kind })
            .Select(r => new RefItem(r.Kind, r.Code, r.Label))
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

    private static string? FilterClause(QuickFilter filter) => filter switch
    {
        QuickFilter.All => null,
        QuickFilter.Owned => "t.is_owned = 1",
        QuickFilter.Wishlist => "t.is_owned = 0",
        QuickFilter.Seen => "EXISTS (SELECT 1 FROM viewing v WHERE v.title_id = t.id)",
        QuickFilter.Unseen => "NOT EXISTS (SELECT 1 FROM viewing v WHERE v.title_id = t.id)",
        QuickFilter.Favorites => "t.is_favorite = 1",
        QuickFilter.Shortlist => "t.is_shortlisted = 1",
        QuickFilter.Movies => "t.kind = 'movie'",
        QuickFilter.Series => "t.kind = 'series'",
        QuickFilter.Loaned => "EXISTS (SELECT 1 FROM loan l JOIN copy cl ON cl.id = l.copy_id WHERE cl.title_id = t.id AND l.returned_at IS NULL)",
        QuickFilter.StandardDefinition => $"({Definition}) = 1",
        QuickFilter.HighDefinition => $"({Definition}) = 2",
        QuickFilter.UltraHighDefinition => $"({Definition}) = 3",
        QuickFilter.ThreeD => "(c.is_3d = 1 OR m.code = 'bluray_3d')",
        QuickFilter.PhysicalMedia => "m.code IS NOT NULL AND m.code NOT IN ('media_file', 'digital', 'streaming')",
        QuickFilter.MediaFiles => "EXISTS (SELECT 1 FROM media_file mf JOIN copy cf ON cf.id = mf.copy_id WHERE cf.title_id = t.id)",
        QuickFilter.MissingFiles => "EXISTS (SELECT 1 FROM media_file mf JOIN copy cf ON cf.id = mf.copy_id WHERE cf.title_id = t.id AND mf.is_missing = 1)",
        QuickFilter.NoMetadata => WithoutMetadata,
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null),
    };

    // Colonnes de tri issues d'une liste fermée : aucune saisie n'arrive dans le SQL.
    private static string OrderBy(SortField sort, bool descending)
    {
        var direction = descending ? "DESC" : "ASC";
        var column = sort switch
        {
            SortField.Title => null,
            SortField.Year => "t.year",
            SortField.Number => "c.number",
            SortField.AddedAt => "t.added_at",
            SortField.UserRating => "t.user_rating",
            SortField.Runtime => "t.runtime_minutes",
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, null),
        };
        return column is null
            ? Tr.F("t.sort_title {0}, t.year {1}", direction, direction)
            : Tr.F("{0} IS NULL, {1} {2}, t.sort_title ASC", column, column, direction);
    }

    private void SetFlag(IEnumerable<long> ids, string column, bool value)
    {
        ArgumentNullException.ThrowIfNull(ids);
        // Le nom de colonne vient d'une liste fermée, jamais d'une saisie.
        if (column is not ("is_favorite" or "is_owned" or "is_shortlisted"))
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, null);
        }

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute(
            $"UPDATE title SET {column} = @value, updated_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now') WHERE id = @id;",
            ids.Select(id => new { id, value }),
            transaction);
        transaction.Commit();
    }

    private static long MainCopyId(SqliteConnection connection, long titleId, SqliteTransaction? transaction = null) =>
        connection.ExecuteScalar<long?>("SELECT min(id) FROM copy WHERE title_id = @titleId;", new { titleId }, transaction)
        ?? connection.ExecuteScalar<long>("INSERT INTO copy (title_id) VALUES (@titleId) RETURNING id;", new { titleId }, transaction);

    private static string ToDb(TitleKind kind) => kind == TitleKind.Series ? "series" : "movie";

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    internal static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static Loan ToLoan(LoanRow row) => new(
        row.Id,
        row.Borrower,
        ParseTimestamp(row.Loaned_At),
        row.Returned_At is null ? null : ParseTimestamp(row.Returned_At));

    private static TitleSummary ToSummary(Row row) => new(
        row.Id,
        row.Kind == "series" ? TitleKind.Series : TitleKind.Movie,
        row.Title,
        (int?)row.Year,
        (int?)row.Runtime_Minutes,
        row.User_Rating,
        row.Is_Owned != 0,
        row.Is_Favorite != 0,
        row.Is_Seen != 0,
        (int?)row.Number,
        row.Medium,
        row.Borrower,
        row.Poster_Rel)
    {
        Genres = row.Genres,
        ImdbRating = row.Imdb_Rating,
        Overview = row.Overview,
        OriginalTitle = row.Original_Title,
        Studio = row.Studio,
        Certification = row.Certification,
        MediaId = row.Media_Id,
        CollectionName = row.Collection_Name,
        AddedOn = row.Added_On,
    };

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

        public long? Number { get; set; }

        public string? Medium { get; set; }

        public string? Original_Title { get; set; }

        public string? Studio { get; set; }

        public string? Certification { get; set; }

        public string? Media_Id { get; set; }

        public string? Collection_Name { get; set; }

        public string? Added_On { get; set; }

        public string? Borrower { get; set; }

        public string? Poster_Rel { get; set; }

        public double? Imdb_Rating { get; set; }

        public string? Overview { get; set; }

        public string? Genres { get; set; }
    }

    private sealed class DetailRow
    {
        public long Id { get; set; }

        public string Kind { get; set; } = "movie";

        public string Title { get; set; } = string.Empty;

        public string? Original_Title { get; set; }

        public string? Alternative_Title { get; set; }

        public long? Year { get; set; }

        public long? Runtime_Minutes { get; set; }

        public string? Tagline { get; set; }

        public string? Overview { get; set; }

        public string? Certification { get; set; }

        public string? Countries { get; set; }

        public string? Spoken_Languages { get; set; }

        public string? Studio { get; set; }

        public string? Web_Link { get; set; }

        public string? Trailer_Url { get; set; }

        public long? Tmdb_Id { get; set; }

        public string? Imdb_Id { get; set; }

        public double? Tmdb_Rating { get; set; }

        public double? Imdb_Rating { get; set; }

        public long? Imdb_Votes { get; set; }

        public long? Tomatometer { get; set; }

        public long? Metascore { get; set; }

        public double? User_Rating { get; set; }

        public string? Comments { get; set; }

        public long Is_Owned { get; set; }

        public long Is_Favorite { get; set; }

        public long Is_Shortlisted { get; set; }

        public long Is_Locked { get; set; }

        public long Is_Adult { get; set; }

        public string Added_At { get; set; } = string.Empty;

        public long? Collection_Id { get; set; }

        public long View_Count { get; set; }

        public string? Last_Seen_At { get; set; }
    }

    private sealed class CopyRow
    {
        public long Id { get; set; }

        public long? Number { get; set; }

        public string? Custom_Number { get; set; }

        public string? Location { get; set; }

        public long? Disc_Count { get; set; }

        public string? Ean { get; set; }

        public long? Price_Cents { get; set; }

        public long Is_3d { get; set; }

        public string? Medium_Code { get; set; }

        public string? Case_Code { get; set; }

        public string? Version_Code { get; set; }
    }

    private sealed class LoanRow
    {
        public long Id { get; set; }

        public string Borrower { get; set; } = string.Empty;

        public string Loaned_At { get; set; } = string.Empty;

        public string? Returned_At { get; set; }
    }
#pragma warning restore CA1707, CA1812
}
