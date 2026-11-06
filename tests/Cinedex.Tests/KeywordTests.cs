using System.Net;
using System.Net.Http;
using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Dapper;

namespace Cinedex.Tests;

/// <summary>
/// Les mots-clés TMDb ont leur propre table. Ils rapprochent les films sans se mélanger aux tags, qui sont
/// le classement personnel de l'utilisateur ; les verser aussi dans les tags reste une option.
/// </summary>
public sealed class KeywordTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly MetadataRepository _metadata;
    private readonly TitleRepository _titles;

    public KeywordTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _metadata = new MetadataRepository(_db);
        _titles = new TitleRepository(_db);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Keywords_are_kept_apart_from_the_personal_tags()
    {
        var id = _metadata.Create(Record("Heat", ["braquage", "los angeles"]), ImportOptions.Default);

        using var connection = _db.Open();
        Assert.Equal(["braquage", "los angeles"], Names(connection, id));
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT count(*) FROM title_tag;"));
    }

    [Fact]
    public void The_option_also_copies_them_into_the_tags()
    {
        var id = _metadata.Create(Record("Heat", ["braquage"]), ImportOptions.Default with { ImportKeywordsAsTags = true });

        using var connection = _db.Open();
        Assert.Equal(["braquage"], Names(connection, id));
        Assert.Equal(1, connection.ExecuteScalar<int>("SELECT count(*) FROM title_tag WHERE title_id = @id;", new { id }));
    }

    [Fact]
    public void A_later_update_replaces_the_keywords_instead_of_piling_them_up()
    {
        var id = _metadata.Create(Record("Heat", ["braquage", "erreur"]), ImportOptions.Default);

        Assert.True(_metadata.Apply(id, Record("Heat", ["braquage", "los angeles"]), ImportOptions.Default));

        using var connection = _db.Open();
        Assert.Equal(["braquage", "los angeles"], Names(connection, id));
    }

    [Fact]
    public void The_catch_up_pass_lists_the_tmdb_titles_that_have_none()
    {
        var withKeywords = _metadata.Create(Record("Heat", ["braquage"]), ImportOptions.Default);
        var without = _metadata.Create(Record("Matrix", []), ImportOptions.Default);

        // Jamais rattaché à TMDb : cette passe n'a rien à y faire.
        _titles.Add(new NewTitle(TitleKind.Movie, "Film scanné", 2001));

        Assert.Equal([without], _titles.WithoutKeywordIds());
        Assert.DoesNotContain(withKeywords, _titles.WithoutKeywordIds());
    }

    private static List<string> Names(Microsoft.Data.Sqlite.SqliteConnection connection, long titleId) =>
        connection.Query<string>(
            "SELECT k.name FROM title_keyword tk JOIN keyword k ON k.id = tk.keyword_id WHERE tk.title_id = @titleId ORDER BY k.name;",
            new { titleId }).ToList();

    private static MetadataRecord Record(string title, string[] keywords) => new()
    {
        Kind = TitleKind.Movie,
        SourceId = title.Length + keywords.Length + 900,
        Title = title,
        Year = 1995,
        Keywords = keywords,
    };
}
