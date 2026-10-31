using Bobine.Core.Titles;
using Bobine.Data;
using Dapper;

namespace Bobine.Tests;

public sealed class CatalogDatabaseTests
{
    [Fact]
    public void Initialize_creates_schema_at_latest_version()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "c", "bobine.db"));

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
        var db = new CatalogDatabase(Path.Combine(temp.Path, "bobine.db"));
        db.Initialize(temp.Path);

        var second = db.Initialize(temp.Path);

        Assert.Empty(second.AppliedVersions);
        Assert.Null(second.BackupPath);
    }

    [Fact]
    public void Newer_schema_is_refused()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "bobine.db"));
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
        var db = new CatalogDatabase(Path.Combine(temp.Path, "bobine.db"));
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
        var db = new CatalogDatabase(Path.Combine(temp.Path, "bobine.db"));
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
        var db = new CatalogDatabase(Path.Combine(temp.Path, "bobine.db"));
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
        var db = new CatalogDatabase(Path.Combine(temp.Path, "bobine.db"));
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
}
