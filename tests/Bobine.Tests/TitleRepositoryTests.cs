using Bobine.Core.Titles;
using Bobine.Data;
using Dapper;

namespace Bobine.Tests;

public sealed class TitleRepositoryTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _repo;

    public TitleRepositoryTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "bobine.db"));
        _db.Initialize(_temp.Path);
        _repo = new TitleRepository(_db);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Add_creates_a_numbered_copy()
    {
        var a = _repo.Add(new NewTitle(TitleKind.Movie, "Alien", 1979));
        var b = _repo.Add(new NewTitle(TitleKind.Movie, "Aliens", 1986));

        Assert.Equal(1, _repo.Get(a)!.Copy!.Number);
        Assert.Equal(2, _repo.Get(b)!.Copy!.Number);
    }

    [Fact]
    public void Update_round_trips_fields_genres_and_copy()
    {
        var id = _repo.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var detail = _repo.Get(id)!;

        _repo.Update(detail with
        {
            Title = "Heat",
            OriginalTitle = "Heat",
            RuntimeMinutes = 170,
            UserRating = 9.5,
            Overview = "Un braqueur et un flic.",
            ImdbId = "tt0113277",
            GenreCodes = ["crime", "drama", "crime"],
            Copy = detail.Copy! with { MediumCode = "bluray", Location = "Étagère B", Price = 12.99m },
        });

        var reloaded = _repo.Get(id)!;
        Assert.Equal(170, reloaded.RuntimeMinutes);
        Assert.Equal(9.5, reloaded.UserRating);
        Assert.Equal(["crime", "drama"], reloaded.GenreCodes.Order());
        Assert.Equal("bluray", reloaded.Copy!.MediumCode);
        Assert.Equal(12.99m, reloaded.Copy.Price);
        Assert.Equal("Étagère B", reloaded.Copy.Location);
        Assert.Single(_repo.Search("braqueur"));
    }

    [Fact]
    public void Invalid_imdb_id_is_rejected_by_the_schema()
    {
        var id = _repo.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));

        Assert.ThrowsAny<Exception>(() => _repo.Update(_repo.Get(id)! with { ImdbId = "javascript:alert(1)" }));
    }

    [Fact]
    public void Quick_filters_select_the_right_titles()
    {
        var owned = _repo.Add(new NewTitle(TitleKind.Movie, "Owned", 2000));
        var wish = _repo.Add(new NewTitle(TitleKind.Movie, "Wish", 2001));
        var series = _repo.Add(new NewTitle(TitleKind.Series, "Show", 2002));
        _repo.SetOwned([wish], false);
        _repo.SetSeen([owned], true, DateTimeOffset.UtcNow);
        _repo.SetFavorite([series], true);
        var uhd = _repo.Get(series)!;
        _repo.Update(uhd with { Copy = uhd.Copy! with { MediumCode = "uhd_bluray" } });

        Assert.Equal([owned, series], Ids(QuickFilter.Owned));
        Assert.Equal([wish], Ids(QuickFilter.Wishlist));
        Assert.Equal([owned], Ids(QuickFilter.Seen));
        Assert.Equal([series, wish], Ids(QuickFilter.Unseen));
        Assert.Equal([series], Ids(QuickFilter.Favorites));
        Assert.Equal([series], Ids(QuickFilter.Series));
        Assert.Equal([series], Ids(QuickFilter.UltraHighDefinition));
        Assert.Equal([series], Ids(QuickFilter.PhysicalMedia));
        Assert.Empty(Ids(QuickFilter.HighDefinition));
    }

    [Fact]
    public void Definition_filters_use_media_file_height()
    {
        var hd = _repo.Add(new NewTitle(TitleKind.Movie, "HD file", 2010));
        using (var connection = _db.Open())
        {
            connection.Execute(
                "INSERT INTO media_file (copy_id, path, height) SELECT id, 'x.mkv', 1080 FROM copy WHERE title_id = @hd;",
                new { hd });
        }

        Assert.Equal([hd], Ids(QuickFilter.HighDefinition));
        Assert.Equal([hd], Ids(QuickFilter.MediaFiles));
    }

    [Fact]
    public void Sorting_puts_missing_values_last()
    {
        var a = _repo.Add(new NewTitle(TitleKind.Movie, "A", 2005));
        var b = _repo.Add(new NewTitle(TitleKind.Movie, "B"));
        var c = _repo.Add(new NewTitle(TitleKind.Movie, "C", 1990));

        Assert.Equal([c, a, b], _repo.Query(new TitleQuery(Sort: SortField.Year)).Select(t => t.Id));
        Assert.Equal([a, c, b], _repo.Query(new TitleQuery(Sort: SortField.Year, Descending: true)).Select(t => t.Id));
        Assert.Equal([c, b, a], _repo.Query(new TitleQuery(Sort: SortField.Number, Descending: true)).Select(t => t.Id));
    }

    [Fact]
    public void Seen_toggle_keeps_a_single_viewing()
    {
        var id = _repo.Add(new NewTitle(TitleKind.Movie, "Vertigo", 1958));

        _repo.SetSeen([id], true);
        _repo.SetSeen([id], true);
        Assert.Equal(1, _repo.Get(id)!.ViewCount);

        _repo.SetSeen([id], false);
        Assert.Equal(0, _repo.Get(id)!.ViewCount);
    }

    [Fact]
    public void Loans_track_history_and_refuse_double_lending()
    {
        var id = _repo.Add(new NewTitle(TitleKind.Movie, "Le Samouraï", 1967));
        var t0 = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        _repo.Lend(id, "Paul", t0);
        Assert.Throws<InvalidOperationException>(() => _repo.Lend(id, "Marie", t0));
        Assert.Equal("Paul", _repo.Get(id)!.CurrentLoan!.Borrower);
        Assert.Equal([id], Ids(QuickFilter.Loaned));
        Assert.Equal("Paul", Assert.Single(_repo.ListAll()).Borrower);

        _repo.Return(id, t0.AddDays(10));
        _repo.Lend(id, "Marie", t0.AddDays(20));

        var history = _repo.LoanHistory(id);
        Assert.Equal(["Marie", "Paul"], history.Select(l => l.Borrower));
        Assert.Equal(t0.AddDays(10), history[1].ReturnedAt);
        Assert.Equal("Marie", _repo.LastBorrower());
    }

    [Fact]
    public void Delete_cascades_to_copies_and_loans()
    {
        var id = _repo.Add(new NewTitle(TitleKind.Movie, "Ran", 1985));
        _repo.Lend(id, "Paul", DateTimeOffset.UtcNow);

        Assert.Equal(1, _repo.Delete([id]));

        using var connection = _db.Open();
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT count(*) FROM copy;"));
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT count(*) FROM loan;"));
        Assert.Empty(_repo.Search("Ran"));
    }

    [Fact]
    public void Random_pick_respects_unseen_option()
    {
        var seen = _repo.Add(new NewTitle(TitleKind.Movie, "Seen", 2000));
        var unseen = _repo.Add(new NewTitle(TitleKind.Movie, "Unseen", 2000));
        _repo.SetSeen([seen], true);

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(unseen, _repo.PickRandom(TitleQuery.Default, unseenOnly: true)!.Id);
        }

        _repo.SetSeen([unseen], true);
        Assert.Null(_repo.PickRandom(TitleQuery.Default, unseenOnly: true));
    }

    [Fact]
    public void Reference_data_is_seeded()
    {
        Assert.Contains(_repo.Genres(), g => g.Code == "science_fiction");
        Assert.Contains(_repo.RefItems("medium"), r => r.Code == "uhd_bluray");
        Assert.Contains(_repo.RefItems("audio_format"), r => r.Code == "atmos");
    }

    private long[] Ids(QuickFilter filter) =>
        _repo.Query(new TitleQuery(Filter: filter)).Select(t => t.Id).ToArray();
}
