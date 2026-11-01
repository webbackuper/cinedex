using System.Globalization;
using Cinedex.Core.People;
using Cinedex.Core.Titles;
using Dapper;

namespace Cinedex.Data;

/// <summary>Personnes du catalogue (acteurs, réalisateurs, scénaristes, compositeurs) et leurs participations.</summary>
public sealed class PeopleRepository(CatalogDatabase database)
{
    private const string PhotoRel = "(SELECT i.rel_path FROM image i WHERE i.id = p.photo_image_id)";

    /// <summary>Distribution puis équipe d'un titre, dans l'ordre du générique.</summary>
    public IReadOnlyList<CastMember> Credits(long titleId)
    {
        using var connection = database.Open();
        return connection.Query<(long Id, string Name, string Role, string? Character, long Position, string? Photo, string? Profile)>(
                $"""
                SELECT p.id, p.name, c.role, c.character, c.position, {PhotoRel}, p.profile_path
                FROM credit c JOIN person p ON p.id = c.person_id
                WHERE c.title_id = @titleId
                ORDER BY CASE c.role WHEN 'director' THEN 0 WHEN 'actor' THEN 1 WHEN 'writer' THEN 2 ELSE 3 END, c.position;
                """,
                new { titleId })
            .Select(r => new CastMember(r.Id, r.Name, r.Role, r.Character, (int)r.Position, r.Photo, r.Profile))
            .ToList();
    }

    public PersonDetail? Get(long personId)
    {
        using var connection = database.Open();
        var r = connection.QuerySingleOrDefault<Row>(
            $"""
            SELECT p.id, p.name, p.tmdb_id, p.imdb_id, p.biography, p.birthday, p.deathday, p.place_of_birth, p.known_for,
                   p.profile_path, {PhotoRel} AS photo_rel, p.details_fetched_at
            FROM person p WHERE p.id = @personId;
            """,
            new { personId });
        return r is null ? null : new PersonDetail
        {
            Id = r.Id,
            Name = r.Name,
            TmdbId = r.Tmdb_Id,
            ImdbId = r.Imdb_Id,
            Biography = r.Biography,
            Birthday = ParseDate(r.Birthday),
            Deathday = ParseDate(r.Deathday),
            PlaceOfBirth = r.Place_Of_Birth,
            KnownFor = r.Known_For,
            ProfilePath = r.Profile_Path,
            PhotoRelativePath = r.Photo_Rel,
            HasDetails = r.Details_Fetched_At is not null,
        };
    }

    /// <summary>Titres du catalogue auxquels la personne a participé, du plus récent au plus ancien.</summary>
    public IReadOnlyList<PersonCredit> Filmography(long personId)
    {
        using var connection = database.Open();
        return connection.Query<(long Id, string Title, long? Year, string Kind, string Role, string? Character, string? Poster)>(
                """
                SELECT t.id, t.title, t.year, t.kind, c.role, c.character,
                       (SELECT i.rel_path FROM title_image ti JOIN image i ON i.id = ti.image_id WHERE ti.title_id = t.id AND ti.role = 'poster')
                FROM credit c JOIN title t ON t.id = c.title_id
                WHERE c.person_id = @personId
                ORDER BY t.year IS NULL, t.year DESC, t.sort_title, CASE c.role WHEN 'actor' THEN 0 ELSE 1 END;
                """,
                new { personId })
            .Select(r => new PersonCredit(r.Id, r.Title, (int?)r.Year, r.Kind == "series" ? TitleKind.Series : TitleKind.Movie, r.Role, r.Character, r.Poster))
            .ToList();
    }

    /// <summary>Recherche par nom (contient), personnes les plus présentes dans le catalogue d'abord.</summary>
    public IReadOnlyList<PersonSummary> Search(string? text, int limit = 200)
    {
        using var connection = database.Open();
        return connection.Query<(long Id, string Name, long Count, string? Photo)>(
                $"""
                SELECT p.id, p.name, count(DISTINCT c.title_id) AS n, {PhotoRel}
                FROM person p JOIN credit c ON c.person_id = p.id
                WHERE @pattern IS NULL OR p.name LIKE @pattern ESCAPE '!'
                GROUP BY p.id ORDER BY n DESC, p.name LIMIT @limit;
                """,
                new { pattern = string.IsNullOrWhiteSpace(text) ? null : TitleRepository.Like(text), limit })
            .Select(r => new PersonSummary(r.Id, r.Name, (int)r.Count, r.Photo))
            .ToList();
    }

    public void SaveDetails(long personId, PersonDetail details)
    {
        ArgumentNullException.ThrowIfNull(details);
        using var connection = database.Open();
        connection.Execute(
            """
            UPDATE person SET
                biography = coalesce(@Biography, biography), birthday = coalesce(@Birthday, birthday),
                deathday = coalesce(@Deathday, deathday), place_of_birth = coalesce(@PlaceOfBirth, place_of_birth),
                known_for = coalesce(@KnownFor, known_for), imdb_id = coalesce(@ImdbId, imdb_id),
                profile_path = coalesce(@ProfilePath, profile_path),
                details_fetched_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
            WHERE id = @personId;
            """,
            new
            {
                personId,
                details.Biography,
                Birthday = details.Birthday?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Deathday = details.Deathday?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                details.PlaceOfBirth,
                details.KnownFor,
                details.ImdbId,
                details.ProfilePath,
            });
    }

    public void SetPhoto(long personId, long imageId)
    {
        using var connection = database.Open();
        connection.Execute("UPDATE person SET photo_image_id = @imageId WHERE id = @personId;", new { personId, imageId });
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

#pragma warning disable CA1707, CA1812 // Noms alignés sur les colonnes SQL ; instancié par Dapper.
    private sealed class Row
    {
        public long Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public long? Tmdb_Id { get; set; }

        public string? Imdb_Id { get; set; }

        public string? Biography { get; set; }

        public string? Birthday { get; set; }

        public string? Deathday { get; set; }

        public string? Place_Of_Birth { get; set; }

        public string? Known_For { get; set; }

        public string? Profile_Path { get; set; }

        public string? Photo_Rel { get; set; }

        public string? Details_Fetched_At { get; set; }
    }
#pragma warning restore CA1707, CA1812
}
