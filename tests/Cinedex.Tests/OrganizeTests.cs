using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Dapper;

namespace Cinedex.Tests;

public sealed class OrganizeTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _repo;

    public OrganizeTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _repo = new TitleRepository(_db);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Tags_groups_and_collection_round_trip_through_update()
    {
        var id = _repo.Add(new NewTitle(TitleKind.Movie, "Alien", 1979));
        var saga = _repo.CreateCollection("Alien, la saga");

        _repo.Update(_repo.Get(id)! with { Tags = ["SF", "Huis clos", "sf"], Groups = ["Soirée"], CollectionId = saga });

        var detail = _repo.Get(id)!;
        Assert.Equal(["Huis clos", "SF"], detail.Tags);
        Assert.Equal(["Soirée"], detail.Groups);
        Assert.Equal("Alien, la saga", detail.CollectionName);
        Assert.Equal(1, Assert.Single(_repo.Collections()).Count);
        Assert.Equal(2, _repo.Tags().Count);

        _repo.Update(detail with { Tags = ["SF"], CollectionId = null });
        Assert.Equal(["SF"], _repo.Get(id)!.Tags);
        Assert.Equal(0, Assert.Single(_repo.Collections()).Count);
    }

    [Fact]
    public void Criteria_combine_with_and()
    {
        var alien = _repo.Add(new NewTitle(TitleKind.Movie, "Alien", 1979));
        var blade = _repo.Add(new NewTitle(TitleKind.Movie, "Blade Runner", 1982));
        var heat = _repo.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        _repo.Update(_repo.Get(alien)! with { GenreCodes = ["horror", "science_fiction"], UserRating = 9, Tags = ["culte"] });
        _repo.Update(_repo.Get(blade)! with { GenreCodes = ["science_fiction"], UserRating = 8 });
        _repo.Update(_repo.Get(heat)! with { GenreCodes = ["crime"], UserRating = 9.5, Copy = _repo.Get(heat)!.Copy! with { Location = "Étagère 100%" } });
        AddCredit(alien, "Ridley Scott", "director", null);
        AddCredit(blade, "Ridley Scott", "director", null);
        AddCredit(blade, "Harrison Ford", "actor", "Rick Deckard");

        Assert.Equal([alien, blade], Ids(new SearchCriteria { PersonName = "ridley" }));
        Assert.Empty(Ids(new SearchCriteria { PersonName = "ridley", PersonRole = CreditRole.Actor }));
        Assert.Equal([blade], Ids(new SearchCriteria { Character = "deckard" }));
        Assert.Equal([blade, heat], Ids(new SearchCriteria { YearFrom = 1980 }));
        Assert.Equal([alien], Ids(new SearchCriteria { YearFrom = 1975, YearTo = 1980 }));
        Assert.Equal([alien, heat], Ids(new SearchCriteria { UserRatingMin = 9 }));
        Assert.Equal([alien, blade], Ids(new SearchCriteria { GenreCodes = ["science_fiction"] }));
        Assert.Equal([alien], Ids(new SearchCriteria { GenreCodes = ["science_fiction", "horror"], GenreMatch = GenreMatch.All }));
        Assert.Equal([heat], Ids(new SearchCriteria { GenreCodes = ["science_fiction"], GenreMatch = GenreMatch.None }));
        Assert.Equal([alien], Ids(new SearchCriteria { Tag = "culte" }));
        Assert.Equal([heat], Ids(new SearchCriteria { Location = "100%" }));
        Assert.Empty(Ids(new SearchCriteria { Location = "_" }));
        Assert.Equal([blade], Ids(new SearchCriteria { PersonName = "Scott", YearFrom = 1980 }));
    }

    [Fact]
    public void Bulk_edit_changes_only_the_given_fields()
    {
        var a = _repo.Add(new NewTitle(TitleKind.Movie, "A", 2000));
        var b = _repo.Add(new NewTitle(TitleKind.Movie, "B", 2001));
        _repo.Update(_repo.Get(a)! with { IsFavorite = true, Tags = ["garder"], Copy = _repo.Get(a)!.Copy! with { Location = "Salon" } });
        var saga = _repo.CreateCollection("Saga");

        _repo.ApplyBulk([a, b], new BulkEdit { IsSeen = true, MediumCode = "bluray", CollectionId = saga, AddTags = ["lot"], RemoveTags = ["garder"] });

        var da = _repo.Get(a)!;
        var db = _repo.Get(b)!;
        Assert.True(da.IsFavorite);
        Assert.False(db.IsFavorite);
        Assert.Equal("Salon", da.Copy!.Location);
        Assert.Equal("bluray", db.Copy!.MediumCode);
        Assert.Equal(["lot"], da.Tags);
        Assert.Equal(1, db.ViewCount);
        Assert.Equal(saga, db.CollectionId);

        _repo.ApplyBulk([a], new BulkEdit { CollectionId = 0, IsSeen = false });
        Assert.Null(_repo.Get(a)!.CollectionId);
        Assert.Equal(0, _repo.Get(a)!.ViewCount);
    }

    private void AddCredit(long titleId, string name, string role, string? character)
    {
        using var connection = _db.Open();
        var personId = connection.ExecuteScalar<long?>("SELECT id FROM person WHERE name = @name;", new { name })
            ?? connection.ExecuteScalar<long>("INSERT INTO person (name) VALUES (@name) RETURNING id;", new { name });
        connection.Execute(
            "INSERT INTO credit (title_id, person_id, role, character) VALUES (@titleId, @personId, @role, @character);",
            new { titleId, personId, role, character });
    }

    private long[] Ids(SearchCriteria criteria) =>
        _repo.Query(new TitleQuery(Criteria: criteria)).Select(t => t.Id).ToArray();
}
