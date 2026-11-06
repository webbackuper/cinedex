using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Dapper;

namespace Cinedex.Tests;

/// <summary>
/// Listes personnelles ordonnées. L'exigence : l'ordre est celui que l'utilisateur donne, il se conserve, et
/// déplacer plusieurs titres à la fois ne les mélange pas entre eux.
/// </summary>
public sealed class PersonalListTests : IDisposable
{
    private const string List = "Soirée famille";

    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _repo;

    public PersonalListTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _repo = new TitleRepository(_db);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_title_joins_the_list_at_its_end()
    {
        var first = Add("Alpha");
        var second = Add("Zoulou");
        var third = Add("Bravo");

        // L'ordre est celui de l'ajout, pas l'ordre alphabétique.
        Assert.Equal([first, second, third], _repo.GroupOrder(List));
    }

    [Fact]
    public void Moving_up_and_down_shifts_one_place_and_stops_at_the_edges()
    {
        var a = Add("A");
        var b = Add("B");
        var c = Add("C");

        _repo.MoveInGroup(List, [c], -1);
        Assert.Equal([a, c, b], _repo.GroupOrder(List));

        _repo.MoveInGroup(List, [c], -1);
        Assert.Equal([c, a, b], _repo.GroupOrder(List));

        // En tête, il ne bouge plus.
        _repo.MoveInGroup(List, [c], -1);
        Assert.Equal([c, a, b], _repo.GroupOrder(List));

        _repo.MoveInGroup(List, [c], 1);
        Assert.Equal([a, c, b], _repo.GroupOrder(List));
    }

    [Fact]
    public void A_group_of_titles_keeps_its_shape_when_it_moves()
    {
        var a = Add("A");
        var b = Add("B");
        var c = Add("C");
        var d = Add("D");

        // B et C montent ensemble d'un cran : ils restent côte à côte, dans le même ordre.
        _repo.MoveInGroup(List, [b, c], -1);

        Assert.Equal([b, c, a, d], _repo.GroupOrder(List));
    }

    [Fact]
    public void The_list_view_shows_the_titles_in_that_order()
    {
        var a = Add("Alpha");
        var z = Add("Zoulou");
        _repo.MoveInGroup(List, [z], -1);

        var rows = _repo.Query(new TitleQuery(
            Sort: SortField.ListOrder,
            Rules: new RuleSearch([new SearchRule("group", RuleOperator.Equals, List)]),
            GroupOrder: List));

        Assert.Equal([z, a], rows.Select(r => r.Id));
    }

    [Fact]
    public void Without_a_list_the_list_order_falls_back_to_the_title()
    {
        Add("Zoulou");
        Add("Alpha");

        var rows = _repo.Query(new TitleQuery(Sort: SortField.ListOrder));

        Assert.Equal(["Alpha", "Zoulou"], rows.Select(r => r.Title));
    }

    [Fact]
    public void Opening_a_personal_list_sorts_by_its_order_and_leaving_it_releases_the_list()
    {
        var a = Add("Alpha");
        var z = Add("Zoulou");
        _repo.MoveInGroup(List, [z], -1);
        var vm = new MainWindowViewModel(_repo, new AppPaths(_temp.Path, isPortable: false), new MainWindowViewModelTests.FakeDialogs());
        vm.Load();

        var lists = vm.Facets.Single(f => f.Label == "Mes listes");
        lists.SelectedItem = lists.Items.Single(i => i.Label == List);

        Assert.Equal(List, vm.SelectedListName);
        Assert.Equal(SortField.ListOrder, vm.SelectedSort.Value);
        Assert.Equal([z, a], vm.Titles.Select(t => t.Id));

        // Déplacer depuis l'interface change l'ordre et garde la sélection.
        vm.SelectedTitles.Add(vm.Titles[1]);
        Assert.True(vm.MoveUpCommand.CanExecute(null));
        vm.MoveUpCommand.Execute(null);
        Assert.Equal([a, z], vm.Titles.Select(t => t.Id));
        Assert.Equal([a], vm.SelectedTitles.Select(t => t.Id));

        vm.LeaveSideViews();
        Assert.Null(vm.SelectedListName);
        Assert.False(vm.MoveUpCommand.CanExecute(null));
    }

    [Fact]
    public void An_existing_group_receives_the_order_it_had_on_screen()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "cinedex.db");
        var migrator = new Migrator(Microsoft.Extensions.Logging.Abstractions.NullLogger<CatalogDatabase>.Instance);

        // Un catalogue d'avant l'ordre des listes : le groupe existe, sans position.
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
        }.ToString()))
        {
            connection.Open();
            migrator.Apply(connection, [.. Migrator.All.Where(m => m.Version <= 11)]);
            var group = connection.ExecuteScalar<long>("INSERT INTO user_group (name) VALUES (@name) RETURNING id;", new { name = List });
            foreach (var title in new[] { "Zoulou", "Alpha", "Mike" })
            {
                var id = connection.ExecuteScalar<long>(
                    "INSERT INTO title (kind, title, sort_title) VALUES ('movie', @title, lower(@title)) RETURNING id;",
                    new { title });
                connection.Execute("INSERT INTO title_group (title_id, group_id) VALUES (@id, @group);", new { id, group });
            }
        }

        var db = new CatalogDatabase(path);
        db.Initialize(temp.Path);

        // L'ordre de départ est celui qu'ils avaient à l'écran, le tri par titre : rien ne bouge à l'affichage.
        Assert.Equal(["Alpha", "Mike", "Zoulou"], new TitleRepository(db).GroupOrder(List)
            .Select(id => new TitleRepository(db).Get(id)!.Title));
    }

    private long Add(string title)
    {
        var id = _repo.Add(new NewTitle(TitleKind.Movie, title, 2000));
        _repo.ApplyBulk([id], new BulkEdit { AddGroups = [List] });
        return id;
    }
}
