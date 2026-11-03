using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Dapper;

namespace Cinedex.Tests;

/// <summary>Onglet Maintenance : nettoyages, doublons, renumérotation, contrôle et renommage du catalogue.</summary>
public sealed class MaintenanceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _titles;
    private readonly MaintenanceRepository _maintenance;

    public MaintenanceTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _titles = new TitleRepository(_db);
        _maintenance = new MaintenanceRepository(_db);
    }

    public void Dispose() => _temp.Dispose();

    private long Person(string name, long? tmdbId = null)
    {
        using var connection = _db.Open();
        return connection.ExecuteScalar<long>("INSERT INTO person (name, tmdb_id) VALUES (@name, @tmdbId) RETURNING id;", new { name, tmdbId });
    }

    private void Credit(long titleId, long personId, string role = "actor")
    {
        using var connection = _db.Open();
        connection.Execute("INSERT INTO credit (title_id, person_id, role) VALUES (@titleId, @personId, @role);", new { titleId, personId, role });
    }

    private T Scalar<T>(string sql)
    {
        using var connection = _db.Open();
        return connection.ExecuteScalar<T>(sql)!;
    }

    [Fact]
    public async Task Purge_empties_the_catalog_after_two_confirmations_but_keeps_its_settings()
    {
        var film = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        _titles.Add(new NewTitle(TitleKind.Series, "Twin Peaks", 1990));
        Credit(film, Person("Al Pacino"));
        new SettingsStore(_db).Set("ui.detailWidth", "500");
        var images = Path.Combine(_temp.Path, "images");
        Directory.CreateDirectory(Path.Combine(images, "ab"));
        File.WriteAllBytes(Path.Combine(images, "ab", new string('a', 64) + ".jpg"), new byte[100]);
        var backups = Path.Combine(_temp.Path, "backups");
        var vm = new MaintenanceViewModel(_maintenance, new CatalogManager(new AppPaths(_temp.Path, isPortable: true)), "tests", backups, images);

        // Deux confirmations : un refus à la seconde ne touche à rien.
        var questions = new List<string>();
        vm.Confirm = (title, _) =>
        {
            questions.Add(title);
            return Task.FromResult(questions.Count == 1);
        };
        await vm.PurgeCatalogCommand.ExecuteAsync(null);
        Assert.Equal(["Purger le catalogue", "Dernière confirmation"], questions);
        Assert.Equal(2, Scalar<int>("SELECT count(*) FROM title;"));
        Assert.False(vm.Changed);

        vm.Confirm = (_, _) => Task.FromResult(true);
        await vm.PurgeCatalogCommand.ExecuteAsync(null);

        Assert.True(vm.Changed);
        Assert.Contains("2 titre(s)", vm.Message, StringComparison.Ordinal);
        foreach (var table in new[] { "title", "copy", "person", "credit", "image", "title_fts" })
        {
            Assert.Equal(0, Scalar<int>($"SELECT count(*) FROM {table};"));
        }

        Assert.Equal("500", new SettingsStore(_db).Get("ui.detailWidth"));
        Assert.True(Scalar<int>("SELECT count(*) FROM genre;") > 0);
        Assert.True(Scalar<int>("SELECT count(*) FROM ref_list;") > 0);
        Assert.Empty(Directory.EnumerateFileSystemEntries(images));
        Assert.Contains(Directory.EnumerateFiles(backups), f => f.Contains("avant-purge", StringComparison.Ordinal));
        Assert.Empty(_maintenance.CheckIntegrity());

        // Le catalogue vidé reste utilisable.
        _titles.Add(new NewTitle(TitleKind.Movie, "Alien", 1979));
        Assert.Equal(1, Scalar<int>("SELECT count(*) FROM title;"));
    }

    [Fact]
    public void Unused_people_are_removed()
    {
        var film = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        Credit(film, Person("Al Pacino"));
        Person("Oublié");

        Assert.Equal(1, _maintenance.CountUnusedPeople());
        Assert.Equal(1, _maintenance.RemoveUnusedPeople());
        Assert.Equal(1, Scalar<int>("SELECT count(*) FROM person;"));
    }

    [Fact]
    public void Duplicate_people_are_merged_but_homonyms_with_different_tmdb_ids_are_kept()
    {
        var a = _titles.Add(new NewTitle(TitleKind.Movie, "A", 2000));
        var b = _titles.Add(new NewTitle(TitleKind.Movie, "B", 2001));
        var fox = Person("Michael J. Fox", tmdbId: 521);
        var foxCopy = Person("michael j fox");
        Credit(a, fox);
        Credit(a, foxCopy); // même film, même rôle : une seule ligne doit rester
        Credit(b, foxCopy, "director");
        Credit(a, Person("John Smith", tmdbId: 1));
        Credit(b, Person("John Smith", tmdbId: 2));

        Assert.Equal(1, _maintenance.MergeDuplicatePeople());

        Assert.Equal(3, Scalar<int>("SELECT count(*) FROM person;"));
        Assert.Equal(2, Scalar<int>($"SELECT count(*) FROM credit WHERE person_id = {fox};"));
        Assert.Equal(0, Scalar<int>($"SELECT count(*) FROM person WHERE id = {foxCopy};"));
    }

    [Fact]
    public void Duplicate_titles_are_found_by_imdb_id_or_by_title_and_year()
    {
        var heat = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var heatAgain = _titles.Add(new NewTitle(TitleKind.Movie, "HEAT", 1995));
        _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1986)); // remake : autre année
        var alien = _titles.Add(new NewTitle(TitleKind.Movie, "Alien", 1979));
        var alienCopy = _titles.Add(new NewTitle(TitleKind.Movie, "Alien, le 8e passager", null));
        _titles.Update(_titles.Get(alien)! with { ImdbId = "tt0078748" });
        _titles.Update(_titles.Get(alienCopy)! with { ImdbId = "tt0078748" });
        _titles.Add(new NewTitle(TitleKind.Movie, "Ran", 1985));

        var groups = _maintenance.FindDuplicateTitles();

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Titles.Select(t => t.Id).Order().SequenceEqual(new[] { alien, alienCopy }.Order()));
        Assert.Contains(groups, g => g.Titles.Select(t => t.Id).Order().SequenceEqual(new[] { heat, heatAgain }.Order()));
    }

    [Fact]
    public void Renumbering_follows_the_given_order_and_puts_the_others_last()
    {
        var c = _titles.Add(new NewTitle(TitleKind.Movie, "C", 2000));
        var a = _titles.Add(new NewTitle(TitleKind.Movie, "A", 2001));
        var b = _titles.Add(new NewTitle(TitleKind.Movie, "B", 2002));
        using (var connection = _db.Open())
        {
            connection.Execute("INSERT INTO copy (title_id) VALUES (@a);", new { a }); // second exemplaire de A
        }

        Assert.Equal(4, _maintenance.Renumber([a, b]));

        using var check = _db.Open();
        var numbers = check.Query<(long TitleId, long Number)>("SELECT title_id, number FROM copy ORDER BY number;").ToList();
        Assert.Equal([a, a, b, c], numbers.Select(n => n.TitleId));
        Assert.Equal([1L, 2, 3, 4], numbers.Select(n => n.Number));
    }

    [Fact]
    public void Empty_labels_and_unused_images_are_removed()
    {
        var film = _titles.Add(new NewTitle(TitleKind.Movie, "Film", 2000));
        _titles.CreateCollection("Vide");
        using (var connection = _db.Open())
        {
            connection.Execute("INSERT INTO tag (name) VALUES ('orphelin');");
        }

        var images = new ImageStore(_db, Path.Combine(_temp.Path, "images"));
        byte[] Png(byte b) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0, b, 0, 0, 0];
        var used = images.Save(Png(1));
        images.Save(Png(2));
        images.Attach(film, used, "poster");
        File.WriteAllText(Path.Combine(images.ImagesDirectory, "notes.txt"), "à garder");

        Assert.Equal((1, 1, 0), _maintenance.RemoveEmptyLabels());
        var cleanup = _maintenance.RemoveUnusedImages(images.ImagesDirectory);

        Assert.Equal((1, 1), (cleanup.Rows, cleanup.Files));
        Assert.NotNull(images.PathFor(film, "poster"));
        Assert.True(File.Exists(Path.Combine(images.ImagesDirectory, "notes.txt")));
    }

    [Fact]
    public void Integrity_check_optimize_and_backup()
    {
        _titles.Add(new NewTitle(TitleKind.Movie, "Film", 2000));

        Assert.Empty(_maintenance.CheckIntegrity());
        var (before, after) = _maintenance.Optimize();
        Assert.True(before > 0 && after > 0);
        Assert.True(File.Exists(_maintenance.Backup(Path.Combine(_temp.Path, "backups"), "test")));
        Assert.Equal(1, _maintenance.Info().Titles);
    }

    [Fact]
    public async Task Renaming_checks_the_name_then_asks_the_application_to_reopen()
    {
        var paths = new AppPaths(Path.Combine(_temp.Path, "data"), isPortable: true);
        var manager = new CatalogManager(paths);
        manager.Create("Films");
        manager.Create("Archives");
        var vm = new MaintenanceViewModel(_maintenance, manager, "Films", paths.BackupsDirectory, Path.Combine(_temp.Path, "images"))
        {
            Confirm = (_, _) => Task.FromResult(true),
        };
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        vm.NewName = "Archives";
        await vm.RenameCommand.ExecuteAsync(null);
        Assert.Null(vm.PendingRename);
        Assert.Contains("Archives", vm.Message, StringComparison.Ordinal);

        vm.NewName = "Films 2026";
        await vm.RenameCommand.ExecuteAsync(null);
        Assert.Equal("Films 2026", vm.PendingRename);
        Assert.True(closed);
    }

    [Fact]
    public async Task A_change_makes_a_backup_first()
    {
        var backups = Path.Combine(_temp.Path, "backups");
        Person("Oublié");
        var vm = new MaintenanceViewModel(_maintenance, new CatalogManager(new AppPaths(Path.Combine(_temp.Path, "data"), isPortable: true)), "Films", backups, Path.Combine(_temp.Path, "images"))
        {
            Confirm = (_, _) => Task.FromResult(true),
        };

        await vm.RemoveUnusedPeopleCommand.ExecuteAsync(null);

        Assert.True(vm.Changed);
        Assert.Single(Directory.GetFiles(backups, "*avant-acteurs-inutilises.db"));
        Assert.Equal(0, vm.UnusedPeople);
    }
}
