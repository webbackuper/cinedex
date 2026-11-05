using Cinedex.Core.Titles;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Cinedex.Data;
using Dapper;

namespace Cinedex.Tests;

public sealed class CatalogDatabaseTests
{
    [Fact]
    public void Initialize_creates_schema_at_latest_version()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "c", "cinedex.db"));

        var result = db.Initialize(Path.Combine(temp.Path, "backups"));

        Assert.True(result.SchemaVersion >= 1);
        Assert.Equal(result.SchemaVersion, result.AppliedVersions[^1]);
        Assert.Null(result.BackupPath);
        using var connection = db.Open();
        Assert.Equal(1, connection.ExecuteScalar<int>("PRAGMA foreign_keys;"));
        Assert.Equal("wal", connection.ExecuteScalar<string>("PRAGMA journal_mode;"));
    }

    [Fact]
    public void Initialize_is_idempotent()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);

        var second = db.Initialize(temp.Path);

        Assert.Empty(second.AppliedVersions);
        Assert.Null(second.BackupPath);
    }

    [Fact]
    public void Newer_schema_is_refused()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        using (var connection = db.Open())
        {
            connection.Execute("PRAGMA user_version = 9999;");
        }

        Assert.Throws<InvalidOperationException>(() => db.Initialize(temp.Path));
    }

    [Fact]
    public void Add_list_and_search_titles()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var repo = new TitleRepository(db);

        repo.Add(new NewTitle(TitleKind.Movie, "The Matrix", 1999));
        repo.Add(new NewTitle(TitleKind.Movie, "Le Fabuleux Destin d'Amélie Poulain", 2001));
        repo.Add(new NewTitle(TitleKind.Series, "Breaking Bad", 2008));

        var all = repo.ListAll();
        Assert.Equal(["Breaking Bad", "Le Fabuleux Destin d'Amélie Poulain", "The Matrix"], all.Select(t => t.Title));
        Assert.Equal(TitleKind.Series, all[0].Kind);

        var hit = Assert.Single(repo.Search("amelie"));
        Assert.Equal(2001, hit.Year);
        Assert.Single(repo.Search("matr"));
    }

    [Theory]
    [InlineData("title:x OR 1")]
    [InlineData("\"unbalanced")]
    [InlineData("NEAR(a b)")]
    [InlineData("*")]
    public void Search_neutralises_fts_syntax(string input)
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var repo = new TitleRepository(db);
        repo.Add(new NewTitle(TitleKind.Movie, "Alien", 1979));

        var exception = Record.Exception(() => repo.Search(input));

        Assert.Null(exception);
    }

    [Fact]
    public void Settings_round_trip()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var settings = new SettingsStore(db);

        settings.Set("ui.view", "grid");
        settings.Set("ui.view", "list");

        Assert.Equal("list", settings.Get("ui.view"));
        Assert.Null(settings.Get("absent"));
    }

    [Fact]
    public void Backup_produces_a_readable_copy()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        new TitleRepository(db).Add(new NewTitle(TitleKind.Movie, "Heat", 1995));

        string backup;
        using (var connection = db.Open())
        {
            backup = db.BackupTo(connection, Path.Combine(temp.Path, "backups"), "test");
        }

        var copy = new CatalogDatabase(backup);
        Assert.Equal(1, new TitleRepository(copy).Count());
    }

    [Fact]
    public void Migrating_to_shared_tmdb_ids_keeps_everything_attached_to_the_titles()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "cinedex.db");
        var migrator = new Migrator(NullLogger<CatalogDatabase>.Instance);

        // Un catalogue tel qu'il était avant la migration : schéma v7, avec un film et tout ce qui s'y rattache.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
        }.ToString()))
        {
            connection.Open();
            migrator.Apply(connection, [.. Migrator.All.Where(m => m.Version <= 7)]);
            Assert.Equal(7, Migrator.CurrentVersion(connection));

            var id = connection.ExecuteScalar<long>(
                "INSERT INTO title (kind, title, sort_title, year, tmdb_id) VALUES ('movie', 'Heat', 'heat', 1995, 949) RETURNING id;");
            var copy = connection.ExecuteScalar<long>("INSERT INTO copy (title_id, number) VALUES (@id, 1) RETURNING id;", new { id });
            connection.Execute("INSERT INTO media_file (copy_id, path, size_bytes) VALUES (@copy, 'D:/Heat.mkv', 700);", new { copy });
            connection.Execute("INSERT INTO title_genre (title_id, genre_id) SELECT @id, id FROM genre LIMIT 1;", new { id });
            var person = connection.ExecuteScalar<long>("INSERT INTO person (name) VALUES ('Al Pacino') RETURNING id;");
            connection.Execute("INSERT INTO credit (title_id, person_id, role) VALUES (@id, @person, 'actor');", new { id, person });
            connection.Execute("INSERT INTO viewing (title_id, viewed_at) VALUES (@id, '2026-01-02');", new { id });

            // Avant la migration, deux entrées ne pouvaient pas partager un numéro TMDb.
            Assert.Throws<SqliteException>(() => connection.Execute(
                "INSERT INTO title (kind, title, sort_title, tmdb_id) VALUES ('movie', 'Heat', 'heat', 949);"));
        }

        // La migration reconstruit la table : rien de ce qui s'y rattachait ne doit disparaître.
        var db = new CatalogDatabase(path);
        db.Initialize(temp.Path);
        using (var connection = db.Open())
        {
            Assert.Equal(Migrator.All[^1].Version, Migrator.CurrentVersion(connection));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT count(*) FROM title;"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT count(*) FROM copy;"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT count(*) FROM media_file;"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT count(*) FROM title_genre;"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT count(*) FROM credit;"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT count(*) FROM viewing;"));
            Assert.Equal(949, connection.ExecuteScalar<long>("SELECT tmdb_id FROM title;"));
            Assert.Empty(connection.Query("PRAGMA foreign_key_check;"));

            // Et deux entrées peuvent désormais désigner le même film.
            connection.Execute("INSERT INTO title (kind, title, sort_title, tmdb_id) VALUES ('movie', 'Heat', 'heat', 949);");
            Assert.Equal(2, connection.ExecuteScalar<int>("SELECT count(*) FROM title WHERE tmdb_id = 949;"));

            // L'index plein texte suit la table reconstruite, déclencheurs compris.
            Assert.Equal(2, connection.ExecuteScalar<int>("SELECT count(*) FROM title_fts WHERE title_fts MATCH 'heat';"));
        }

        // Les clés étrangères sont bien rétablies après les migrations.
        using (var connection = db.Open())
        {
            Assert.Equal(1L, connection.ExecuteScalar<long>("PRAGMA foreign_keys;"));
        }
    }

}
