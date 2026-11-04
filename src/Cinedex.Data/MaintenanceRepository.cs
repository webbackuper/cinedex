using System.Globalization;
using System.Text;
using Cinedex.Core.Titles;
using Dapper;

namespace Cinedex.Data;

/// <summary>État de la base ouverte, affiché en tête de l'onglet Maintenance.</summary>
public sealed record DatabaseInfo(string Path, long SizeBytes, int SchemaVersion, int Titles, int People, int Images, int Copies);

/// <summary>Un titre d'un groupe de doublons possibles.</summary>
public sealed record DuplicateTitle(long Id, string Title, int? Year, string Kind, long? TmdbId, int Files);

/// <summary>Titres qui semblent désigner le même film, et pourquoi.</summary>
public sealed record DuplicateGroup(string Reason, IReadOnlyList<DuplicateTitle> Titles);

/// <summary>Bilan d'un nettoyage d'images : lignes retirées de la base, fichiers effacés du disque.</summary>
public sealed record ImageCleanup(int Rows, int Files, long Bytes);

/// <summary>
/// Bilan d'une fusion de films en double : fiches retirées, fichiers regroupés, et parmi eux ceux dont MediaInfo
/// donne exactement les mêmes caractéristiques (même encodage, donc redondants plutôt qu'une autre version).
/// </summary>
public sealed record TitleMerge(int Merged, int Files, int SameEncoding);

/// <summary>Bilan d'une purge du catalogue : ce qu'il contenait, et les fichiers d'images effacés.</summary>
public sealed record CatalogPurge(int Titles, int Copies, int People, int ImageFiles, long ImageBytes);

/// <summary>
/// Opérations de maintenance du catalogue ouvert : nettoyages, doublons, renumérotation, contrôle et compactage.
/// Les opérations qui modifient la base sont chacune faites dans une transaction.
/// </summary>
public sealed class MaintenanceRepository(CatalogDatabase database)
{
    public CatalogDatabase Database => database;

    public DatabaseInfo Info()
    {
        using var connection = database.Open();
        var size = File.Exists(database.DatabasePath) ? new FileInfo(database.DatabasePath).Length : 0;
        var wal = database.DatabasePath + "-wal";
        if (File.Exists(wal))
        {
            size += new FileInfo(wal).Length;
        }

        return new DatabaseInfo(
            database.DatabasePath,
            size,
            connection.ExecuteScalar<int>("PRAGMA user_version;"),
            connection.ExecuteScalar<int>("SELECT count(*) FROM title;"),
            connection.ExecuteScalar<int>("SELECT count(*) FROM person;"),
            connection.ExecuteScalar<int>("SELECT count(*) FROM image;"),
            connection.ExecuteScalar<int>("SELECT count(*) FROM copy;"));
    }

    /// <summary>Personnes qui ne figurent plus dans aucun générique (acteurs de films supprimés…).</summary>
    public int CountUnusedPeople()
    {
        using var connection = database.Open();
        return connection.ExecuteScalar<int>("SELECT count(*) FROM person WHERE id NOT IN (SELECT person_id FROM credit);");
    }

    public int RemoveUnusedPeople()
    {
        using var connection = database.Open();
        return connection.Execute("DELETE FROM person WHERE id NOT IN (SELECT person_id FROM credit);");
    }

    /// <summary>
    /// Doublons possibles : même identifiant IMDb, ou même titre (sans accents, casse ni ponctuation) et même genre
    /// de titre, avec la même année ou une année inconnue. Rien n'est supprimé : la liste sert à vérifier.
    /// </summary>
    public IReadOnlyList<DuplicateGroup> FindDuplicateTitles()
    {
        using var connection = database.Open();
        var rows = connection.Query<(long Id, string Title, long? Year, string Kind, long? TmdbId, string? ImdbId, long Files)>(
                """
                SELECT t.id, t.title, t.year, t.kind, t.tmdb_id, t.imdb_id,
                       (SELECT count(*) FROM media_file m JOIN copy c ON c.id = m.copy_id WHERE c.title_id = t.id)
                FROM title t ORDER BY t.sort_title, t.year;
                """)
            .ToList();
        DuplicateTitle Row((long Id, string Title, long? Year, string Kind, long? TmdbId, string? ImdbId, long Files) r) =>
            new(r.Id, r.Title, (int?)r.Year, r.Kind, r.TmdbId, (int)r.Files);

        var groups = new List<DuplicateGroup>();
        var grouped = new HashSet<long>();
        foreach (var same in rows.Where(r => r.ImdbId is not null).GroupBy(r => (r.Kind, r.ImdbId)).Where(g => g.Count() > 1))
        {
            groups.Add(new DuplicateGroup($"IMDb {same.Key.ImdbId}", [.. same.Select(Row)]));
            grouped.UnionWith(same.Select(r => r.Id));
        }

        foreach (var same in rows.Where(r => !grouped.Contains(r.Id)).GroupBy(r => (r.Kind, Key: NormalizeTitle(r.Title))).Where(g => g.Count() > 1))
        {
            // Même titre : on sépare les années connues différentes (remakes) ; une année inconnue rejoint chaque groupe.
            var known = same.Where(r => r.Year is not null).GroupBy(r => r.Year).ToList();
            var unknown = same.Where(r => r.Year is null).ToList();
            var sets = known.Count == 0 ? [unknown] : known.Select(g => g.Concat(unknown).ToList()).ToList();
            foreach (var set in sets.Where(s => s.Count > 1))
            {
                groups.Add(new DuplicateGroup(Cinedex.Core.Localization.Tr.T("Même titre et même année"), [.. set.Select(Row)]));
            }
        }

        return groups;
    }

    /// <summary>
    /// Fusionne les personnes en double (même nom, sans accents ni casse) : génériques regroupés sur une seule fiche,
    /// identifiants et photo repris. Deux homonymes aux identifiants TMDb ou IMDb différents restent distincts.
    /// Renvoie le nombre de fiches retirées.
    /// </summary>
    public int MergeDuplicatePeople()
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var people = connection.Query<(long Id, string Name, long? TmdbId, string? ImdbId, long? PhotoId, long Credits)>(
                "SELECT p.id, p.name, p.tmdb_id, p.imdb_id, p.photo_image_id, (SELECT count(*) FROM credit c WHERE c.person_id = p.id) FROM person p;",
                transaction: transaction)
            .ToList();

        var removed = 0;
        foreach (var group in people.GroupBy(p => NormalizeName(p.Name)).Where(g => g.Key.Length > 0 && g.Count() > 1))
        {
            if (group.Select(p => p.TmdbId).OfType<long>().Distinct().Count() > 1 ||
                group.Select(p => p.ImdbId).OfType<string>().Distinct(StringComparer.Ordinal).Count() > 1)
            {
                continue; // Homonymes : des fiches TMDb ou IMDb différentes désignent des personnes différentes.
            }

            var keep = group.OrderByDescending(p => p.TmdbId is not null).ThenByDescending(p => p.Credits).ThenBy(p => p.Id).First();
            var others = group.Where(p => p.Id != keep.Id).Select(p => p.Id).ToList();
            var tmdbId = keep.TmdbId ?? group.Select(p => p.TmdbId).OfType<long>().FirstOrDefault() as long?;
            var imdbId = keep.ImdbId ?? group.Select(p => p.ImdbId).OfType<string>().FirstOrDefault();
            var photoId = keep.PhotoId ?? group.Select(p => p.PhotoId).OfType<long>().FirstOrDefault() as long?;

            connection.Execute("UPDATE credit SET person_id = @keep WHERE person_id IN @others;", new { keep = keep.Id, others }, transaction);
            connection.Execute("DELETE FROM person WHERE id IN @others;", new { others }, transaction);
            connection.Execute(
                """
                UPDATE person SET tmdb_id = coalesce(tmdb_id, @tmdbId), imdb_id = coalesce(imdb_id, @imdbId),
                                  photo_image_id = coalesce(photo_image_id, @photoId)
                WHERE id = @id;
                """,
                new { id = keep.Id, tmdbId = tmdbId == 0 ? null : tmdbId, imdbId, photoId = photoId == 0 ? null : photoId },
                transaction);
            removed += others.Count;
        }

        // Une même personne citée deux fois au même rôle d'un titre (après fusion) : une seule ligne est gardée.
        connection.Execute(
            "DELETE FROM credit WHERE id NOT IN (SELECT min(id) FROM credit GROUP BY title_id, person_id, role);",
            transaction: transaction);
        transaction.Commit();
        return removed;
    }

    /// <summary>
    /// Fusionne les films en double (mêmes groupes que <see cref="FindDuplicateTitles"/>) : une fiche est gardée,
    /// les exemplaires et fichiers des autres lui sont rattachés, et les fiches vidées sont supprimées. Aucun fichier
    /// du disque n'est touché : le film garde toutes ses versions, numérotées à la suite.
    /// </summary>
    public TitleMerge MergeDuplicateTitles()
    {
        var groups = FindDuplicateTitles();
        if (groups.Count == 0)
        {
            return new TitleMerge(0, 0, 0);
        }

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        int merged = 0, gathered = 0;
        foreach (var group in groups)
        {
            // On garde la fiche la mieux remplie : liée à TMDb, puis la plus fournie en fichiers, puis la plus ancienne.
            var keep = group.Titles
                .OrderByDescending(t => t.TmdbId is not null)
                .ThenByDescending(t => t.Files)
                .ThenBy(t => t.Id)
                .First();
            var others = group.Titles.Where(t => t.Id != keep.Id).Select(t => t.Id).ToList();
            if (others.Count == 0)
            {
                continue;
            }

            // Les exemplaires des doublons passent à la fiche gardée, avec leurs fichiers, prêts et pistes.
            gathered += connection.Execute("UPDATE copy SET title_id = @keep WHERE title_id IN @others;", new { keep = keep.Id, others }, transaction);
            connection.Execute(
                """
                UPDATE title SET
                    tmdb_id = coalesce(tmdb_id, (SELECT max(tmdb_id) FROM title WHERE id IN @others)),
                    imdb_id = coalesce(imdb_id, (SELECT max(imdb_id) FROM title WHERE id IN @others)),
                    overview = coalesce(nullif(overview, ''), (SELECT max(overview) FROM title WHERE id IN @others)),
                    year = coalesce(year, (SELECT max(year) FROM title WHERE id IN @others))
                WHERE id = @keep;
                """,
                new { keep = keep.Id, others },
                transaction);
            connection.Execute("DELETE FROM title WHERE id IN @others;", new { others }, transaction);
            merged += others.Count;
        }

        // Les fichiers du film se suivent : une version par numéro, dans l'ordre où ils ont été rattachés.
        foreach (var titleId in connection.Query<long>(
                     "SELECT DISTINCT c.title_id FROM copy c JOIN media_file m ON m.copy_id = c.id;", transaction: transaction).ToList())
        {
            var part = 0;
            foreach (var id in connection.Query<long>(
                         "SELECT m.id FROM media_file m JOIN copy c ON c.id = m.copy_id WHERE c.title_id = @titleId ORDER BY m.part_number, m.id;",
                         new { titleId },
                         transaction).ToList())
            {
                connection.Execute("UPDATE media_file SET part_number = @part WHERE id = @id;", new { id, part = ++part }, transaction);
            }
        }

        // Même encodage : MediaInfo renvoie exactement les mêmes caractéristiques pour deux fichiers d'un même film.
        var sameEncoding = connection.ExecuteScalar<int>(
            """
            SELECT coalesce(sum(n - 1), 0) FROM (
                SELECT count(*) AS n FROM media_file m JOIN copy c ON c.id = m.copy_id
                WHERE m.video_codec IS NOT NULL
                GROUP BY c.title_id, m.video_codec, m.width, m.height, m.duration_seconds, m.bit_depth,
                         coalesce(m.hdr_formats, ''), coalesce(m.container, ''), coalesce(m.size_bytes, -1)
                HAVING count(*) > 1);
            """,
            transaction: transaction);

        transaction.Commit();
        return new TitleMerge(merged, gathered, sameEncoding);
    }

    /// <summary>
    /// Renumérote les exemplaires (colonne N°) de 1 à n dans l'ordre des titres donné ; les exemplaires d'un même
    /// titre se suivent. Renvoie le nombre d'exemplaires numérotés.
    /// </summary>
    public int Renumber(IReadOnlyList<long> orderedTitleIds)
    {
        ArgumentNullException.ThrowIfNull(orderedTitleIds);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var copies = connection.Query<(long Id, long TitleId)>("SELECT id, title_id FROM copy ORDER BY title_id, id;", transaction: transaction)
            .ToLookup(c => c.TitleId, c => c.Id);
        var rank = orderedTitleIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);

        // Titres absents de l'ordre donné (masqués par un filtre) : placés à la fin, dans leur ordre actuel.
        var order = copies.Select(g => g.Key)
            .OrderBy(id => rank.TryGetValue(id, out var r) ? r : int.MaxValue)
            .ThenBy(id => id)
            .SelectMany(id => copies[id])
            .ToList();

        connection.Execute("UPDATE copy SET number = NULL;", transaction: transaction);
        var number = 0;
        foreach (var copyId in order)
        {
            connection.Execute("UPDATE copy SET number = @number WHERE id = @copyId;", new { number = ++number, copyId }, transaction);
        }

        transaction.Commit();
        return number;
    }

    /// <summary>Supprime les collections, tags et groupes qui ne contiennent plus aucun titre.</summary>
    public (int Collections, int Tags, int Groups) RemoveEmptyLabels()
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var collections = connection.Execute("DELETE FROM collection WHERE id NOT IN (SELECT collection_id FROM title WHERE collection_id IS NOT NULL);", transaction: transaction);
        var tags = connection.Execute("DELETE FROM tag WHERE id NOT IN (SELECT tag_id FROM title_tag);", transaction: transaction);
        var groups = connection.Execute("DELETE FROM user_group WHERE id NOT IN (SELECT group_id FROM title_group);", transaction: transaction);
        transaction.Commit();
        return (collections, tags, groups);
    }

    /// <summary>
    /// Retire les images que plus rien n'utilise (titre, collection, photo de personne…), puis efface du dossier
    /// des images les fichiers qui ne sont plus référencés. Seuls les fichiers rangés par empreinte sont concernés.
    /// </summary>
    public ImageCleanup RemoveUnusedImages(string imagesDirectory)
    {
        using var connection = database.Open();
        // Toutes les colonnes qui pointent vers image(id), lues dans le schéma pour ne rien oublier.
        var references = connection.Query<(string Table, string Column)>(
                """
                SELECT m.name, f."from" FROM sqlite_master m, pragma_foreign_key_list(m.name) f
                WHERE m.type = 'table' AND f."table" = 'image';
                """)
            .ToList();
        var used = string.Join(" UNION ", references.Select(r => $"SELECT \"{r.Column}\" FROM \"{r.Table}\" WHERE \"{r.Column}\" IS NOT NULL"));
        var rows = connection.Execute($"DELETE FROM image WHERE id NOT IN ({(used.Length == 0 ? "SELECT NULL" : used)});");

        var kept = connection.Query<string>("SELECT rel_path FROM image;")
            .Select(p => Path.GetFullPath(Path.Combine(imagesDirectory, p)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int files = 0;
        long bytes = 0;
        if (Directory.Exists(imagesDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(imagesDirectory, "*.*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Length != 64 || !name.All(char.IsAsciiHexDigitLower) || kept.Contains(Path.GetFullPath(file)))
                {
                    continue; // Pas une image rangée par empreinte, ou encore utilisée.
                }

                var length = new FileInfo(file).Length;
                File.Delete(file);
                files++;
                bytes += length;
            }
        }

        return new ImageCleanup(rows, files, bytes);
    }

    /// <summary>
    /// Tables gardées par la purge : réglages du catalogue, listes de référence, genres, champs personnalisés,
    /// filtres enregistrés et dossiers à analyser. Tout le reste (titres, personnes, images, prêts…) est vidé.
    /// </summary>
    public static IReadOnlySet<string> KeptByPurge { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "setting", "ref_list", "genre", "custom_field", "saved_filter", "scan_root",
    };

    /// <summary>
    /// Vide complètement le catalogue ouvert (titres, exemplaires, fichiers, personnes, collections, images, prêts,
    /// séries…) et efface son dossier d'images ; ses réglages et listes de référence restent. Renvoie ce qui a été retiré.
    /// </summary>
    public CatalogPurge Purge(string imagesDirectory)
    {
        var before = Info();
        using (var connection = database.Open())
        {
            // Les tables sont vidées d'un bloc : l'ordre des clés étrangères n'a pas à être suivi.
            connection.Execute("PRAGMA foreign_keys = OFF;");
            try
            {
                using var transaction = connection.BeginTransaction();
                var tables = connection.Query<string>(
                        """
                        SELECT name FROM sqlite_master
                        WHERE type = 'table' AND name NOT LIKE 'sqlite!_%' ESCAPE '!' AND name NOT LIKE 'title!_fts%' ESCAPE '!';
                        """,
                        transaction: transaction)
                    .Where(t => !KeptByPurge.Contains(t))
                    .ToList();
                foreach (var table in tables)
                {
                    connection.Execute($"DELETE FROM \"{table}\";", transaction: transaction);
                }

                connection.Execute("INSERT INTO title_fts (title_fts) VALUES ('delete-all');", transaction: transaction);
                if (connection.ExecuteScalar<int>("SELECT count(*) FROM sqlite_master WHERE name = 'sqlite_sequence';", transaction: transaction) > 0)
                {
                    connection.Execute("DELETE FROM sqlite_sequence;", transaction: transaction);
                }

                transaction.Commit();
            }
            finally
            {
                connection.Execute("PRAGMA foreign_keys = ON;");
            }
        }

        int files = 0;
        long bytes = 0;
        if (Directory.Exists(imagesDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(imagesDirectory, "*", SearchOption.AllDirectories))
            {
                bytes += new FileInfo(file).Length;
                File.Delete(file);
                files++;
            }

            foreach (var folder in Directory.EnumerateDirectories(imagesDirectory))
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        return new CatalogPurge(before.Titles, before.Copies, before.People, files, bytes);
    }

    /// <summary>Contrôle complet de la base : intégrité des pages et des clés étrangères. Renvoie les anomalies.</summary>
    public IReadOnlyList<string> CheckIntegrity()
    {
        using var connection = database.Open();
        var problems = connection.Query<string>("PRAGMA integrity_check;").Where(r => r != "ok").ToList();
        problems.AddRange(connection.Query<(string Table, long? RowId, string Parent)>("SELECT \"table\", rowid, parent FROM pragma_foreign_key_check;")
            .Select(f => Cinedex.Core.Localization.Tr.F("Lien rompu : {0} (ligne {1}) vers {2}", f.Table, f.RowId?.ToString(CultureInfo.InvariantCulture) ?? "?", f.Parent)));
        return problems;
    }

    /// <summary>Compacte la base (VACUUM) et met à jour les statistiques de l'optimiseur ; renvoie la taille avant et après.</summary>
    public (long Before, long After) Optimize()
    {
        var before = Info().SizeBytes;
        using (var connection = database.Open())
        {
            connection.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
            connection.Execute("VACUUM;");
            connection.Execute("ANALYZE; PRAGMA optimize;");
            connection.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
        }

        return (before, Info().SizeBytes);
    }

    /// <summary>Copie de sauvegarde de la base ouverte dans le dossier des sauvegardes ; renvoie son chemin.</summary>
    public string Backup(string backupDirectory, string label)
    {
        using var connection = database.Open();
        return database.BackupTo(connection, backupDirectory, label);
    }

    internal static string NormalizeTitle(string title) => Normalize(SortTitle.Compute(title ?? string.Empty));

    internal static string NormalizeName(string name) => Normalize(name);

    private static string Normalize(string text)
    {
        var result = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            result.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return string.Join(' ', result.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
