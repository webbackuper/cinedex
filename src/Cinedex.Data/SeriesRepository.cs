using System.Globalization;
using Cinedex.Core.Metadata;
using Dapper;
using Microsoft.Data.Sqlite;
using Cinedex.Core.Localization;

namespace Cinedex.Data;

public sealed record SeasonView(long Id, int Number, string Name, DateOnly? AirDate, bool IsLocked, int EpisodeCount, int SeenCount, int FileCount)
{
    public string Label => Number == 0 ? "Épisodes spéciaux" : string.IsNullOrWhiteSpace(Name) ? Tr.F("Saison {0}", Number) : Name;

    public string Progress => Tr.F("{0}/{1} vus", SeenCount, EpisodeCount);
}

public sealed record EpisodeView(long Id, long SeasonId, int SeasonNumber, int Number, string? Name, string? Overview, DateOnly? AirDate, bool IsSeen, string? FilePath)
{
    public string Code => $"S{SeasonNumber:00}E{Number:00}";
}

/// <summary>
/// Suivi d'une série : épisodes diffusés et vus, prochain épisode à regarder, prochaine diffusion annoncée.
/// </summary>
public sealed record SeriesTracking(
    long TitleId,
    string Title,
    string? Status,
    long? TmdbId,
    string? PosterRelativePath,
    int Aired,
    int Seen,
    EpisodeView? NextToWatch,
    EpisodeView? NextAiring)
{
    public int ToWatch => Math.Max(0, Aired - Seen);

    public double Ratio => Aired == 0 ? 0 : (double)Seen / Aired;

    public bool IsRunning => Status == "continuing";
}

/// <summary>Un épisode à venir d'une série du catalogue (calendrier des diffusions).</summary>
public sealed record UpcomingEpisode(long TitleId, string Series, EpisodeView Episode);

/// <summary>Saisons, épisodes, visionnage par épisode et fichiers d'épisodes d'une série.</summary>
public sealed class SeriesRepository(CatalogDatabase database)
{
    /// <summary>
    /// Fusionne saisons et épisodes reçus de la source : ajoute ce qui manque, met à jour les textes,
    /// ne touche pas aux saisons verrouillées ni aux épisodes déjà vus ou rattachés à un fichier.
    /// Renvoie le nombre de nouveaux épisodes.
    /// </summary>
    public int Merge(long titleId, IReadOnlyList<SeasonRecord> seasons, IReadOnlyDictionary<int, IReadOnlyList<EpisodeRecord>> episodes)
    {
        ArgumentNullException.ThrowIfNull(seasons);
        ArgumentNullException.ThrowIfNull(episodes);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var added = 0;
        foreach (var season in seasons)
        {
            var seasonId = connection.ExecuteScalar<long>(
                """
                INSERT INTO season (title_id, season_number, name, overview, air_date) VALUES (@titleId, @Number, @Name, @Overview, @AirDate)
                ON CONFLICT (title_id, season_number) DO UPDATE SET
                    name = CASE WHEN season.is_locked = 1 THEN season.name ELSE excluded.name END,
                    overview = CASE WHEN season.is_locked = 1 THEN season.overview ELSE excluded.overview END,
                    air_date = CASE WHEN season.is_locked = 1 THEN season.air_date ELSE excluded.air_date END
                RETURNING id;
                """,
                new { titleId, season.Number, season.Name, season.Overview, AirDate = Format(season.AirDate) },
                transaction);
            var locked = connection.ExecuteScalar<long>("SELECT is_locked FROM season WHERE id = @seasonId;", new { seasonId }, transaction) != 0;
            if (locked || !episodes.TryGetValue(season.Number, out var list))
            {
                continue;
            }

            foreach (var episode in list)
            {
                var exists = connection.ExecuteScalar<bool>(
                    "SELECT EXISTS (SELECT 1 FROM episode WHERE season_id = @seasonId AND episode_number = @Number);",
                    new { seasonId, episode.Number },
                    transaction);
                connection.Execute(
                    """
                    INSERT INTO episode (season_id, episode_number, name, overview, air_date, runtime_minutes, tmdb_id)
                    VALUES (@seasonId, @Number, @Name, @Overview, @AirDate, @RuntimeMinutes, @SourceId)
                    ON CONFLICT (season_id, episode_number) DO UPDATE SET name = excluded.name, overview = excluded.overview,
                        air_date = excluded.air_date, runtime_minutes = excluded.runtime_minutes, tmdb_id = excluded.tmdb_id;
                    """,
                    new { seasonId, episode.Number, episode.Name, episode.Overview, AirDate = Format(episode.AirDate), episode.RuntimeMinutes, episode.SourceId },
                    transaction);
                if (!exists)
                {
                    added++;
                }
            }
        }

        transaction.Commit();
        return added;
    }

    public IReadOnlyList<SeasonView> Seasons(long titleId)
    {
        using var connection = database.Open();
        return connection.Query<(long Id, long Number, string? Name, string? AirDate, long Locked, long Episodes, long Seen, long Files)>(
                """
                SELECT s.id, s.season_number, s.name, s.air_date, s.is_locked,
                       (SELECT count(*) FROM episode e WHERE e.season_id = s.id),
                       (SELECT count(*) FROM episode e WHERE e.season_id = s.id AND EXISTS (SELECT 1 FROM viewing v WHERE v.episode_id = e.id)),
                       (SELECT count(*) FROM episode e WHERE e.season_id = s.id AND EXISTS (SELECT 1 FROM media_file m WHERE m.episode_id = e.id))
                FROM season s WHERE s.title_id = @titleId
                ORDER BY CASE WHEN s.season_number = 0 THEN 1 ELSE 0 END, s.season_number;
                """,
                new { titleId })
            .Select(s => new SeasonView(s.Id, (int)s.Number, s.Name ?? string.Empty, Parse(s.AirDate), s.Locked != 0, (int)s.Episodes, (int)s.Seen, (int)s.Files))
            .ToList();
    }

    /// <summary>
    /// Suivi de toutes les séries du catalogue à la date donnée : épisodes diffusés (saisons normales, date passée)
    /// et vus, prochain épisode diffusé non vu, prochaine diffusion. Les séries à regarder viennent en premier.
    /// </summary>
    public IReadOnlyList<SeriesTracking> Tracking(DateOnly today)
    {
        using var connection = database.Open();
        var day = Format(today);
        var series = connection.Query<(long Id, string Title, string? Status, long? TmdbId, string? Poster, long Aired, long Seen)>(
                """
                SELECT t.id, t.title, t.series_status, t.tmdb_id,
                       (SELECT i.rel_path FROM title_image ti JOIN image i ON i.id = ti.image_id WHERE ti.title_id = t.id AND ti.role = 'poster'),
                       (SELECT count(*) FROM episode e JOIN season s ON s.id = e.season_id
                        WHERE s.title_id = t.id AND s.season_number > 0 AND e.air_date IS NOT NULL AND e.air_date <= @day),
                       (SELECT count(*) FROM episode e JOIN season s ON s.id = e.season_id
                        WHERE s.title_id = t.id AND s.season_number > 0 AND e.air_date IS NOT NULL AND e.air_date <= @day
                          AND EXISTS (SELECT 1 FROM viewing v WHERE v.episode_id = e.id))
                FROM title t WHERE t.kind = 'series' ORDER BY t.sort_title;
                """,
                new { day })
            .ToList();

        var result = new List<SeriesTracking>(series.Count);
        foreach (var s in series)
        {
            var next = QueryEpisodes(
                connection,
                """
                s.title_id = @id AND s.season_number > 0 AND e.air_date IS NOT NULL AND e.air_date <= @day
                AND NOT EXISTS (SELECT 1 FROM viewing v WHERE v.episode_id = e.id)
                """,
                new { id = s.Id, day }).FirstOrDefault();
            var airing = QueryEpisodes(connection, "s.title_id = @id AND s.season_number > 0 AND e.air_date > @day", new { id = s.Id, day })
                .OrderBy(e => e.AirDate)
                .FirstOrDefault();
            result.Add(new SeriesTracking(s.Id, s.Title, s.Status, s.TmdbId, s.Poster, (int)s.Aired, (int)s.Seen, next, airing));
        }

        return [.. result.OrderByDescending(r => r.ToWatch > 0).ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Nombre de séries du catalogue, et de celles qui ont des épisodes diffusés non vus (une seule requête).</summary>
    public (int Series, int ToWatch) CountToWatch(DateOnly today)
    {
        using var connection = database.Open();
        return connection.QuerySingle<(int, int)>(
            """
            SELECT (SELECT count(*) FROM title WHERE kind = 'series'),
                   (SELECT count(DISTINCT s.title_id) FROM episode e JOIN season s ON s.id = e.season_id JOIN title t ON t.id = s.title_id
                    WHERE t.kind = 'series' AND s.season_number > 0 AND e.air_date IS NOT NULL AND e.air_date <= @day
                      AND NOT EXISTS (SELECT 1 FROM viewing v WHERE v.episode_id = e.id));
            """,
            new { day = Format(today) });
    }

    /// <summary>Épisodes des séries du catalogue diffusés dans les jours à venir, par date.</summary>
    public IReadOnlyList<UpcomingEpisode> Upcoming(DateOnly today, int days)
    {
        using var connection = database.Open();
        var titles = connection.Query<(long Id, string Title)>("SELECT id, title FROM title WHERE kind = 'series';").ToDictionary(t => t.Id, t => t.Title);
        var episodes = connection.Query<(long EpisodeId, long TitleId)>(
                """
                SELECT e.id, s.title_id FROM episode e JOIN season s ON s.id = e.season_id
                WHERE s.season_number > 0 AND e.air_date > @from AND e.air_date <= @to;
                """,
                new { from = Format(today), to = Format(today.AddDays(days)) })
            .ToList();
        var result = new List<UpcomingEpisode>();
        foreach (var group in episodes.GroupBy(e => e.TitleId))
        {
            var ids = group.Select(e => e.EpisodeId).ToList();
            result.AddRange(QueryEpisodes(connection, "e.id IN (SELECT value FROM json_each(@ids))", new { ids = System.Text.Json.JsonSerializer.Serialize(ids) })
                .Select(e => new UpcomingEpisode(group.Key, titles.GetValueOrDefault(group.Key, "?"), e)));
        }

        return [.. result.OrderBy(u => u.Episode.AirDate).ThenBy(u => u.Series, StringComparer.CurrentCultureIgnoreCase)];
    }

    public IReadOnlyList<EpisodeView> Episodes(long seasonId)
    {
        using var connection = database.Open();
        return QueryEpisodes(connection, "e.season_id = @seasonId", new { seasonId });
    }

    /// <summary>Épisodes déjà diffusés sans fichier ni visionnage, pour repérer ce qui manque.</summary>
    public IReadOnlyList<EpisodeView> MissingEpisodes(long titleId, DateOnly today)
    {
        using var connection = database.Open();
        return QueryEpisodes(
            connection,
            """
            s.title_id = @titleId AND s.season_number > 0 AND e.air_date IS NOT NULL AND e.air_date <= @today
            AND NOT EXISTS (SELECT 1 FROM media_file m WHERE m.episode_id = e.id)
            AND NOT EXISTS (SELECT 1 FROM viewing v WHERE v.episode_id = e.id)
            """,
            new { titleId, today = Format(today) });
    }

    public long? FindEpisode(long titleId, int season, int episode)
    {
        using var connection = database.Open();
        return connection.ExecuteScalar<long?>(
            """
            SELECT e.id FROM episode e JOIN season s ON s.id = e.season_id
            WHERE s.title_id = @titleId AND s.season_number = @season AND e.episode_number = @episode;
            """,
            new { titleId, season, episode });
    }

    /// <summary>Crée au besoin la saison et l'épisode (série saisie à la main, épisode absent de la source).</summary>
    public long EnsureEpisode(long titleId, int season, int episode)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var seasonId = connection.ExecuteScalar<long>(
            "INSERT INTO season (title_id, season_number) VALUES (@titleId, @season) ON CONFLICT DO UPDATE SET season_number = season_number RETURNING id;",
            new { titleId, season },
            transaction);
        var id = connection.ExecuteScalar<long>(
            "INSERT INTO episode (season_id, episode_number) VALUES (@seasonId, @episode) ON CONFLICT DO UPDATE SET episode_number = episode_number RETURNING id;",
            new { seasonId, episode },
            transaction);
        transaction.Commit();
        return id;
    }

    public void SetEpisodesSeen(IEnumerable<long> episodeIds, bool seen, DateTimeOffset? when = null)
    {
        ArgumentNullException.ThrowIfNull(episodeIds);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var id in episodeIds)
        {
            connection.Execute(
                seen
                    ? "INSERT INTO viewing (episode_id, viewed_at) SELECT @id, @when WHERE NOT EXISTS (SELECT 1 FROM viewing WHERE episode_id = @id);"
                    : "DELETE FROM viewing WHERE episode_id = @id;",
                new { id, when = when?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) },
                transaction);
        }

        transaction.Commit();
        SyncSeriesSeen(episodeIds);
    }

    public void SetSeasonSeen(long seasonId, bool seen) =>
        SetEpisodesSeen(Episodes(seasonId).Select(e => e.Id).ToList(), seen, DateTimeOffset.Now);

    public void SetSeriesSeen(long titleId, bool seen)
    {
        var ids = Seasons(titleId).Where(s => s.Number > 0).SelectMany(s => Episodes(s.Id)).Select(e => e.Id).ToList();
        SetEpisodesSeen(ids, seen, DateTimeOffset.Now);
    }

    public void SetSeasonLocked(long seasonId, bool locked)
    {
        using var connection = database.Open();
        connection.Execute("UPDATE season SET is_locked = @locked WHERE id = @seasonId;", new { seasonId, locked });
    }

    /// <summary>
    /// Une série est « vue » quand tous ses épisodes hors spéciaux le sont ; le statut du titre suit donc
    /// celui des épisodes, ce qui garde les filtres « vus / pas vus » cohérents.
    /// </summary>
    private void SyncSeriesSeen(IEnumerable<long> episodeIds)
    {
        using var connection = database.Open();
        var titleIds = connection.Query<long>(
            "SELECT DISTINCT s.title_id FROM episode e JOIN season s ON s.id = e.season_id WHERE e.id IN @ids;",
            new { ids = episodeIds.ToList() });
        foreach (var titleId in titleIds)
        {
            var remaining = connection.ExecuteScalar<long>(
                """
                SELECT count(*) FROM episode e JOIN season s ON s.id = e.season_id
                WHERE s.title_id = @titleId AND s.season_number > 0 AND NOT EXISTS (SELECT 1 FROM viewing v WHERE v.episode_id = e.id);
                """,
                new { titleId });
            connection.Execute(
                remaining == 0
                    ? "INSERT INTO viewing (title_id, viewed_at) SELECT @titleId, strftime('%Y-%m-%dT%H:%M:%fZ', 'now') WHERE NOT EXISTS (SELECT 1 FROM viewing WHERE title_id = @titleId);"
                    : "DELETE FROM viewing WHERE title_id = @titleId;",
                new { titleId });
        }
    }

    private static List<EpisodeView> QueryEpisodes(SqliteConnection connection, string where, object param) =>
        connection.Query<(long Id, long SeasonId, long SeasonNumber, long Number, string? Name, string? Overview, string? AirDate, long Seen, string? Path, string? Root)>(
                $"""
                SELECT e.id, e.season_id, s.season_number, e.episode_number, e.name, e.overview, e.air_date,
                       EXISTS (SELECT 1 FROM viewing v WHERE v.episode_id = e.id),
                       (SELECT m.path FROM media_file m WHERE m.episode_id = e.id ORDER BY m.part_number LIMIT 1),
                       (SELECT r.path FROM media_file m JOIN scan_root r ON r.id = m.scan_root_id WHERE m.episode_id = e.id ORDER BY m.part_number LIMIT 1)
                FROM episode e JOIN season s ON s.id = e.season_id
                WHERE {where}
                ORDER BY s.season_number, e.episode_number;
                """,
                param)
            .Select(e => new EpisodeView(
                e.Id,
                e.SeasonId,
                (int)e.SeasonNumber,
                (int)e.Number,
                e.Name,
                e.Overview,
                Parse(e.AirDate),
                e.Seen != 0,
                e.Path is null ? null : e.Root is null ? e.Path : System.IO.Path.GetFullPath(System.IO.Path.Combine(e.Root, e.Path))))
            .ToList();

    private static string? Format(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly? Parse(string? date) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
