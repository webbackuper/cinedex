using Cinedex.Core.Titles;
using Dapper;
using Microsoft.Data.Sqlite;
using Cinedex.Core.Localization;

namespace Cinedex.Data;

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

    /// <summary>
    /// Titres qui ont une affiche, pour le diaporama de l'économiseur d'écran : chemins relatifs de l'affiche et,
    /// s'il existe, du fond.
    /// </summary>
    public IReadOnlyList<PosterSlide> PosterSlides(bool hideAdult)
    {
        using var connection = database.Open();
        return connection.Query<(long Id, string Title, long? Year, string Poster, string? Backdrop)>(
                """
                SELECT t.id, t.title, t.year, i.rel_path,
                       (SELECT b.rel_path FROM title_image tb JOIN image b ON b.id = tb.image_id WHERE tb.title_id = t.id AND tb.role = 'backdrop')
                FROM title t
                JOIN title_image ti ON ti.title_id = t.id AND ti.role = 'poster'
                JOIN image i ON i.id = ti.image_id
                WHERE @hideAdult = 0 OR t.is_adult = 0;
                """,
                new { hideAdult })
            .Select(r => new PosterSlide(r.Id, r.Title, (int?)r.Year, r.Poster, r.Backdrop))
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
                GenreMatch.All => Tr.F("{0} = @GenreCount", matches),
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

    /// <summary>Titres d'une liste personnelle, dans l'ordre que l'utilisateur leur a donné.</summary>
    public IReadOnlyList<long> GroupOrder(string group)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        using var connection = database.Open();
        return connection.Query<long>(
                """
                SELECT tg.title_id FROM title_group tg JOIN user_group ug ON ug.id = tg.group_id
                WHERE ug.name = @group COLLATE NOCASE ORDER BY tg.position, tg.title_id;
                """,
                new { group })
            .ToList();
    }

    /// <summary>
    /// Déplace des titres d'un cran dans une liste personnelle (-1 vers le haut, +1 vers le bas). Un titre
    /// bloqué par un bord, ou par un voisin lui aussi déplacé, ne bouge pas : une sélection garde sa forme.
    /// </summary>
    public void MoveInGroup(string group, IReadOnlyCollection<long> ids, int delta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0 || delta == 0)
        {
            return;
        }

        var order = GroupOrder(group).ToList();
        var moving = order.Select((id, index) => (id, index)).Where(x => ids.Contains(x.id)).ToList();
        if (moving.Count == 0 || moving.Count == order.Count)
        {
            return;
        }

        // Vers le haut on part du premier, vers le bas du dernier : sinon un déplacé écraserait le suivant.
        foreach (var (id, _) in delta < 0 ? moving : Enumerable.Reverse(moving))
        {
            var index = order.IndexOf(id);
            var target = index + delta;
            if (target < 0 || target >= order.Count || ids.Contains(order[target]))
            {
                continue;
            }

            (order[index], order[target]) = (order[target], order[index]);
        }

        SetGroupOrder(group, order);
    }

    /// <summary>Écrit l'ordre d'une liste personnelle ; les titres absents de la liste sont ignorés.</summary>
    public void SetGroupOrder(string group, IReadOnlyList<long> orderedIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentNullException.ThrowIfNull(orderedIds);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var groupId = connection.ExecuteScalar<long?>("SELECT id FROM user_group WHERE name = @group COLLATE NOCASE;", new { group }, transaction);
        if (groupId is not { } id)
        {
            return;
        }

        var position = 0;
        foreach (var titleId in orderedIds)
        {
            connection.Execute(
                "UPDATE title_group SET position = @position WHERE group_id = @id AND title_id = @titleId;",
                new { id, titleId, position = position++ },
                transaction);
        }

        transaction.Commit();
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
            if (label == Label.Group)
            {
                // Une liste est ordonnée : un titre qui y entre se range à la fin, pas à la place zéro.
                connection.Execute(
                    """
                    INSERT OR IGNORE INTO title_group (title_id, group_id, position)
                    VALUES (@titleId, @labelId, coalesce((SELECT max(position) + 1 FROM title_group WHERE group_id = @labelId), 0));
                    """,
                    new { titleId, labelId },
                    transaction);
                continue;
            }

            connection.Execute(
                $"INSERT OR IGNORE INTO {label.Link} (title_id, {label.Column}) VALUES (@titleId, @labelId);",
                new { titleId, labelId },
                transaction);
        }
    }
}
