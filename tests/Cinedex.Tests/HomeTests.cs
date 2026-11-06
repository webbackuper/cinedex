using Cinedex.App.ViewModels;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Dapper;

namespace Cinedex.Tests;

/// <summary>
/// Page d'accueil. L'exigence : ne proposer ce soir que ce qui est réellement regardable ce soir, reprendre
/// les sagas commencées, et garder le même tirage au long de la journée sans le figer pour toujours.
/// </summary>
public sealed class HomeTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _repo;
    private readonly HomeRepository _home;

    public HomeTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _repo = new TitleRepository(_db);
        _home = new HomeRepository(_db);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Tonight_only_offers_films_whose_disk_is_plugged_in()
    {
        var here = Film("Heat", 1995, rating: 8.0, label: "FILMS-1");
        var away = Film("Collateral", 2004, rating: 8.0, label: "FILMS-2");

        var tonight = _home.Tonight(["FILMS-1"], new DateOnly(2026, 11, 6));

        Assert.Equal([here], tonight);
        Assert.DoesNotContain(away, tonight);
    }

    [Fact]
    public void Tonight_leaves_out_what_was_already_seen_and_what_is_poorly_rated()
    {
        var unseen = Film("Heat", 1995, rating: 8.0, label: "FILMS-1");
        var seen = Film("Collateral", 2004, rating: 8.0, label: "FILMS-1");
        var poor = Film("Un navet", 2010, rating: 4.0, label: "FILMS-1");
        _repo.AddViewing(seen, DateTimeOffset.Now);

        var tonight = _home.Tonight(["FILMS-1"], new DateOnly(2026, 11, 6));

        Assert.Equal([unseen], tonight);
        Assert.DoesNotContain(poor, tonight);
    }

    [Fact]
    public void The_order_holds_for_the_day_and_changes_with_it()
    {
        for (var i = 0; i < 12; i++)
        {
            Film($"Film {i}", 2000 + i, rating: 8.0, label: "FILMS-1");
        }

        var morning = _home.Tonight(["FILMS-1"], new DateOnly(2026, 11, 6));
        var evening = _home.Tonight(["FILMS-1"], new DateOnly(2026, 11, 6));
        var tomorrow = _home.Tonight(["FILMS-1"], new DateOnly(2026, 11, 7));

        Assert.Equal(morning, evening);
        Assert.NotEqual(morning, tomorrow);
    }

    [Fact]
    public void A_saga_is_resumed_where_it_was_left()
    {
        var first = Film("Le Parrain", 1972, rating: 9.0, label: "FILMS-1");
        var second = Film("Le Parrain 2", 1974, rating: 9.0, label: "FILMS-1");
        var third = Film("Le Parrain 3", 1990, rating: 7.0, label: "FILMS-1");
        var alone = Film("Heat", 1995, rating: 8.0, label: "FILMS-1");
        using (var connection = _db.Open())
        {
            var saga = connection.ExecuteScalar<long>("INSERT INTO collection (name) VALUES ('Le Parrain') RETURNING id;");
            connection.Execute("UPDATE title SET collection_id = @saga WHERE id IN (@first, @second, @third);", new { saga, first, second, third });
        }

        _repo.AddViewing(first, DateTimeOffset.Now);

        var resume = _home.ResumeSaga();

        Assert.Equal([second, third], resume);
        Assert.DoesNotContain(alone, resume);
    }

    [Fact]
    public void Forgotten_films_are_the_old_ones_that_were_never_watched()
    {
        var today = new DateOnly(2026, 11, 6);
        var old = Film("Un vieux film", 1980, rating: 7.0, label: "FILMS-1");
        var recent = Film("Un film récent", 2026, rating: 7.0, label: "FILMS-1");
        Added(old, "2024-01-15");
        Added(recent, "2026-11-01");

        Assert.Equal([old], _home.Forgotten(today));
    }

    [Fact]
    public void The_page_leaves_out_the_bands_that_have_nothing_to_show()
    {
        Film("Heat", 1995, rating: 8.0, label: "FILMS-1");
        var page = new HomeViewModel();

        page.Load(_home, Ordered, ["FILMS-1"], null);

        Assert.True(page.HasBands);
        Assert.Contains(page.Bands, b => b.Title == "À voir ce soir");
        // Aucune saga commencée, aucun film oublié : ces bandes ne s'affichent pas vides.
        Assert.DoesNotContain(page.Bands, b => b.Title == "Reprendre une saga");
        Assert.DoesNotContain(page.Bands, b => b.Title == "Oubliés");
    }

    private IReadOnlyList<TitleSummary> Ordered(IReadOnlyList<long> ids)
    {
        var rows = _repo.Query(new TitleQuery(Ids: ids)).ToDictionary(t => t.Id);
        return ids.Where(rows.ContainsKey).Select(id => rows[id]).ToList();
    }

    private long Film(string title, int year, double rating, string label)
    {
        var id = _repo.Add(new NewTitle(TitleKind.Movie, title, year));
        using var connection = _db.Open();
        connection.Execute("UPDATE title SET tmdb_rating = @rating WHERE id = @id;", new { id, rating });
        var copy = connection.ExecuteScalar<long>("SELECT id FROM copy WHERE title_id = @id;", new { id });
        connection.Execute(
            "INSERT INTO media_file (copy_id, path, volume_label, size_bytes) VALUES (@copy, @path, @label, 700);",
            new { copy, path = $"D:/{title}.mkv", label });
        return id;
    }

    private void Added(long id, string day)
    {
        using var connection = _db.Open();
        connection.Execute("UPDATE title SET added_at = @day WHERE id = @id;", new { id, day });
    }
}
