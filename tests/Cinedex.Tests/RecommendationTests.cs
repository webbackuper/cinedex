using Cinedex.Core.Titles;
using Cinedex.Data;
using Dapper;

namespace Cinedex.Tests;

/// <summary>
/// « Si vous aimez ce film, vous aimerez aussi ». L'exigence : proposer des films du catalogue vraiment
/// proches, les plus proches d'abord, avec une raison juste — et ne rien proposer quand rien ne rapproche.
/// </summary>
public sealed class RecommendationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _repo;
    private readonly RecommendationRepository _reco;

    public RecommendationTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _repo = new TitleRepository(_db);
        _reco = new RecommendationRepository(_db);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_same_director_outranks_a_shared_genre_alone()
    {
        var heat = Film("Heat", 1995, ["crime", "drama"]);
        var insider = Film("Révélations", 1999, ["drama"]);
        var other = Film("Un autre drame", 1998, ["drama"]);
        Direct(heat, "Michael Mann");
        Direct(insider, "Michael Mann");
        Direct(other, "Quelqu'un d'autre");

        var found = _reco.For(heat);

        Assert.Equal(insider, found[0].TitleId);
        Assert.Contains("Michael Mann", found[0].Reason, StringComparison.Ordinal);
        // Un seul genre partagé ne suffit pas à être proposé.
        Assert.DoesNotContain(found, r => r.TitleId == other);
    }

    [Fact]
    public void Keywords_bring_together_films_that_genres_alone_would_not()
    {
        var heat = Film("Heat", 1995, ["crime"]);
        var town = Film("The Town", 2010, ["crime"]);
        Keyword(heat, "braquage");
        Keyword(heat, "los angeles");
        Keyword(town, "braquage");
        Keyword(town, "boston");

        var found = _reco.For(heat);

        var match = Assert.Single(found);
        Assert.Equal(town, match.TitleId);
        Assert.Contains("braquage", match.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_saga_is_the_most_obvious_suggestion()
    {
        var first = Film("Le Parrain", 1972, ["crime", "drama"]);
        var second = Film("Le Parrain 2", 1974, ["crime", "drama"]);
        var unrelated = Film("Scarface", 1983, ["crime", "drama"]);
        using (var connection = _db.Open())
        {
            var saga = connection.ExecuteScalar<long>("INSERT INTO collection (name) VALUES ('Le Parrain') RETURNING id;");
            connection.Execute("UPDATE title SET collection_id = @saga WHERE id IN (@first, @second);", new { saga, first, second });
        }

        var found = _reco.For(first);

        Assert.Equal(second, found[0].TitleId);
        Assert.Contains("saga", found[0].Reason, StringComparison.Ordinal);
        Assert.Contains(found, r => r.TitleId == unrelated);
    }

    [Fact]
    public void A_series_is_never_suggested_after_a_film_and_a_wishlist_entry_never_is()
    {
        var film = Film("Heat", 1995, ["crime", "drama"]);
        var series = _repo.Add(new NewTitle(TitleKind.Series, "Une série policière", 1995));
        _repo.Update(_repo.Get(series)! with { GenreCodes = ["crime", "drama"] });
        var wished = Film("Un film convoité", 1996, ["crime", "drama"]);
        _repo.SetOwned([wished], false);

        var found = _reco.For(film);

        Assert.DoesNotContain(found, r => r.TitleId == series);
        Assert.DoesNotContain(found, r => r.TitleId == wished);
    }

    [Fact]
    public void Nothing_in_common_means_nothing_proposed()
    {
        var film = Film("Heat", 1995, ["crime"]);
        Film("Un documentaire animalier", 1960, ["documentary"]);

        Assert.Empty(_reco.For(film));
    }

    private long Film(string title, int year, string[] genres)
    {
        var id = _repo.Add(new NewTitle(TitleKind.Movie, title, year));
        _repo.Update(_repo.Get(id)! with { GenreCodes = genres });
        return id;
    }

    private void Direct(long titleId, string name)
    {
        using var connection = _db.Open();
        // person.name n'est pas unique : c'est au rapprochement de réutiliser la personne, comme à l'import.
        var personId = connection.ExecuteScalar<long?>("SELECT id FROM person WHERE name = @name;", new { name })
            ?? connection.ExecuteScalar<long>("INSERT INTO person (name) VALUES (@name) RETURNING id;", new { name });
        connection.Execute(
            "INSERT INTO credit (title_id, person_id, role, position) VALUES (@titleId, @personId, 'director', 0);",
            new { titleId, personId });
    }

    private void Keyword(long titleId, string name)
    {
        using var connection = _db.Open();
        var keywordId = connection.ExecuteScalar<long>(
            "INSERT INTO keyword (name) VALUES (@name) ON CONFLICT (name) DO UPDATE SET name = keyword.name RETURNING id;",
            new { name });
        connection.Execute("INSERT OR IGNORE INTO title_keyword (title_id, keyword_id) VALUES (@titleId, @keywordId);", new { titleId, keywordId });
    }
}
