using Bobine.Core.Titles;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Bobine.Data;

/// <summary>Recherche par champ, collections, tags, groupes et modification groupée.</summary>
public sealed partial class TitleRepository
{
    /// <summary>Tags (colorés) et groupes personnels partagent le même schéma de liaison.</summary>
    private sealed record Label(string Table, string Link, string Column)
    {
        public static readonly Label Tag = new("tag", "title_tag", "tag_id");
        public static readonly Label Group = new("user_group", "title_group", "group_id");
    }

    /// <summary>Générique d'un titre, dans l'ordre : (rôle, nom, personnage).</summary>
    public IReadOnlyList<(string Role, string Name, string? Character)> Credits(long titleId)
    {
        using var connection = database.Open();
        return connection.Query<(string, string, string?)>(
                "SELECT c.role, p.name, c.character FROM credit c JOIN person p ON p.id = c.person_id WHERE c.title_id = @titleId ORDER BY c.role, c.position;",
                new { titleId })
            .ToList();
    }

    /// <summary>Libellés des genres d'un titre.</summary>
    public IReadOnlyList<string> GenreLabels(long titleId)
    {
        using var connection = database.Open();
        return connection.Query<string>(
                "SELECT g.label FROM title_genre tg JOIN genre g ON g.id = tg.genre_id WHERE tg.title_id = @titleId ORDER BY g.label;",
                new { titleId })
            .ToList();
    }

    public IReadOnlyList<CollectionInfo> Collections()
    {
        using var connection = database.Open();
        return connection.Query<(long Id, string Name, long Count)>(
                """
                SELECT c.id, c.name, count(t.id) FROM collection c LEFT JOIN title t ON t.collection_id = c.id
                GROUP BY c.id ORDER BY coalesce(c.sort_key, c.name);
                """)
            .Select(c => new CollectionInfo(c.Id, c.Name, (int)c.Count))
            .ToList();
    }

    public long CreateCollection(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using var connection = database.Open();
        return connection.ExecuteScalar<long>(
            "INSERT INTO collection (name, sort_key) VALUES (@Name, @SortKey) RETURNING id;",
            new { Name = name.Trim(), SortKey = SortTitle.Compute(name) });
    }

    public IReadOnlyList<LabelInfo> Tags()
    {
        using var connection = database.Open();
        return connection.Query<(string Name, string? Color, long Count)>(
                "SELECT l.name, l.color, count(x.title_id) FROM tag l LEFT JOIN title_tag x ON x.tag_id = l.id GROUP BY l.id ORDER BY l.name;")
            .Select(l => new LabelInfo(l.Name, l.Color, (int)l.Count))
            .ToList();
    }

    public IReadOnlyList<LabelInfo> Groups()
    {
        using var connection = database.Open();
        return connection.Query<(string Name, long Count)>(
                "SELECT l.name, count(x.title_id) FROM user_group l LEFT JOIN title_group x ON x.group_id = l.id GROUP BY l.id ORDER BY l.name;")
            .Select(l => new LabelInfo(l.Name, null, (int)l.Count))
            .ToList();
    }

    /// <summary>Applique une modification groupée aux titres donnés, dans une seule transaction.</summary>
    public void ApplyBulk(IReadOnlyCollection<long> ids, BulkEdit edit)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(edit);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var id in ids)
        {
            connection.Execute(
                """
                UPDATE title SET
                    is_owned = coalesce(@IsOwned, is_owned), is_favorite = coalesce(@IsFavorite, is_favorite),
                    is_shortlisted = coalesce(@IsShortlisted, is_shortlisted), is_locked = coalesce(@IsLocked, is_locked),
                    updated_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
                WHERE id = @id;
                """,
                new { id, edit.IsOwned, edit.IsFavorite, edit.IsShortlisted, edit.IsLocked },
                transaction);

            if (edit.CollectionId is { } collection)
            {
                connection.Execute(
                    "UPDATE title SET collection_id = @Collection WHERE id = @id;",
                    new { id, Collection = collection == 0 ? (long?)null : collection },
                    transaction);
            }

            if (edit.Location is not null || edit.MediumCode is not null)
            {
                var copyId = MainCopyId(connection, id, transaction);
                connection.Execute(
                    """
                    UPDATE copy SET
                        location = coalesce(@Location, location),
                        medium_id = coalesce((SELECT r.id FROM ref_list r WHERE r.kind = 'medium' AND r.code = @MediumCode), medium_id)
                    WHERE id = @copyId;
                    """,
                    new { edit.Location, edit.MediumCode, copyId },
                    transaction);
            }

            AddLabels(connection, transaction, Label.Tag, id, edit.AddTags);
            AddLabels(connection, transaction, Label.Group, id, edit.AddGroups);
            foreach (var tag in edit.RemoveTags)
            {
                connection.Execute(
                    "DELETE FROM title_tag WHERE title_id = @id AND tag_id = (SELECT id FROM tag WHERE name = @tag);",
                    new { id, tag },
                    transaction);
            }

            if (edit.IsSeen == true)
            {
                connection.Execute(
                    "INSERT INTO viewing (title_id, viewed_at) SELECT @id, @now WHERE NOT EXISTS (SELECT 1 FROM viewing WHERE title_id = @id);",
                    new { id, now = FormatTimestamp(DateTimeOffset.UtcNow) },
                    transaction);
            }
            else if (edit.IsSeen == false)
            {
                connection.Execute("DELETE FROM viewing WHERE title_id = @id;", new { id }, transaction);
            }
        }

        transaction.Commit();
    }

    /// <summary>Motif LIKE « contient » ; les jokers % et _ saisis par l'utilisateur sont neutralisés.</summary>
    internal static string Like(string text) =>
        "%" + text.Trim()
            .Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal) + "%";

    private static void AddCriteria(SearchCriteria c, List<string> where, DynamicParameters parameters)
    {
        if (!string.IsNullOrWhiteSpace(c.PersonName))
        {
            var role = c.PersonRole is null ? string.Empty : " AND cr.role = @PersonRole";
            where.Add($"EXISTS (SELECT 1 FROM credit cr JOIN person p ON p.id = cr.person_id WHERE cr.title_id = t.id AND p.name LIKE @PersonName ESCAPE '!'{role})");
            parameters.Add("PersonName", Like(c.PersonName));
            parameters.Add("PersonRole", c.PersonRole?.ToString().ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(c.Character))
        {
            where.Add("EXISTS (SELECT 1 FROM credit cr WHERE cr.title_id = t.id AND cr.character LIKE @Character ESCAPE '!')");
            parameters.Add("Character", Like(c.Character));
        }

        if (c.YearFrom is { } from)
        {
            where.Add("t.year >= @YearFrom");
            parameters.Add("YearFrom", from);
        }

        if (c.YearTo is { } to)
        {
            where.Add("t.year <= @YearTo");
            parameters.Add("YearTo", to);
        }

        if (c.UserRatingMin is { } userMin)
        {
            where.Add("t.user_rating >= @UserRatingMin");
            parameters.Add("UserRatingMin", userMin);
        }

        if (c.SourceRatingMin is { } sourceMin)
        {
            where.Add("t.tmdb_rating >= @SourceRatingMin");
            parameters.Add("SourceRatingMin", sourceMin);
        }

        if (c.GenreCodes.Count > 0)
        {
            const string matches = "(SELECT count(DISTINCT g.code) FROM title_genre tg JOIN genre g ON g.id = tg.genre_id WHERE tg.title_id = t.id AND g.code IN @GenreCodes)";
            var codes = c.GenreCodes.Distinct(StringComparer.Ordinal).ToList();
            where.Add(c.GenreMatch switch
            {
                GenreMatch.All => $"{matches} = @GenreCount",
                GenreMatch.None => $"{matches} = 0",
                _ => $"{matches} > 0",
            });
            parameters.Add("GenreCodes", codes);
            parameters.Add("GenreCount", codes.Count);
        }

        if (!string.IsNullOrWhiteSpace(c.Tag))
        {
            where.Add("EXISTS (SELECT 1 FROM title_tag tt JOIN tag g ON g.id = tt.tag_id WHERE tt.title_id = t.id AND g.name = @Tag)");
            parameters.Add("Tag", c.Tag.Trim());
        }

        if (!string.IsNullOrWhiteSpace(c.Group))
        {
            where.Add("EXISTS (SELECT 1 FROM title_group tg JOIN user_group g ON g.id = tg.group_id WHERE tg.title_id = t.id AND g.name = @Group)");
            parameters.Add("Group", c.Group.Trim());
        }

        if (c.CollectionId is { } collection)
        {
            where.Add("t.collection_id = @CollectionId");
            parameters.Add("CollectionId", collection);
        }

        if (!string.IsNullOrWhiteSpace(c.Location))
        {
            where.Add("EXISTS (SELECT 1 FROM copy cl WHERE cl.title_id = t.id AND cl.location LIKE @Location ESCAPE '!')");
            parameters.Add("Location", Like(c.Location));
        }

        if (!string.IsNullOrWhiteSpace(c.Borrower))
        {
            where.Add("EXISTS (SELECT 1 FROM loan l JOIN copy cb ON cb.id = l.copy_id WHERE cb.title_id = t.id AND l.returned_at IS NULL AND l.borrower LIKE @Borrower ESCAPE '!')");
            parameters.Add("Borrower", Like(c.Borrower));
        }
    }

    private static void ReplaceLabels(SqliteConnection connection, SqliteTransaction transaction, Label label, long titleId, IReadOnlyList<string> names)
    {
        connection.Execute($"DELETE FROM {label.Link} WHERE title_id = @titleId;", new { titleId }, transaction);
        AddLabels(connection, transaction, label, titleId, names);
    }

    // Les noms de table viennent des instances statiques de Label, jamais d'une saisie.
    private static void AddLabels(SqliteConnection connection, SqliteTransaction transaction, Label label, long titleId, IReadOnlyList<string> names)
    {
        foreach (var name in names.Select(n => n.Trim()).Where(n => n.Length is > 0 and <= 80).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var labelId = connection.ExecuteScalar<long>(
                $"INSERT INTO {label.Table} (name) VALUES (@name) ON CONFLICT (name) DO UPDATE SET name = {label.Table}.name RETURNING id;",
                new { name },
                transaction);
            connection.Execute(
                $"INSERT OR IGNORE INTO {label.Link} (title_id, {label.Column}) VALUES (@titleId, @labelId);",
                new { titleId, labelId },
                transaction);
        }
    }
}
