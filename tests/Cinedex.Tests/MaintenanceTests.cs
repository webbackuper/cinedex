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

    /// <summary>
    /// Tous les rapprochements proposés ne se valent pas : on doit pouvoir n'accepter qu'un groupe, et laisser
    /// les autres tels quels plutôt que de tout prendre ou tout refuser.
    /// </summary>
    [Fact]
    public void One_group_of_duplicates_can_be_merged_alone()
    {
        var files = new MediaFileRepository(_db);
        var heat = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var heatBis = _titles.Add(new NewTitle(TitleKind.Movie, "heat", 1995));
        var ran = _titles.Add(new NewTitle(TitleKind.Movie, "Ran", 1985));
        var ranBis = _titles.Add(new NewTitle(TitleKind.Movie, "ran", 1985));
        files.Attach(heat, Path.Combine(_temp.Path, "Heat.1080p.mkv"), 700);
        files.Attach(heatBis, Path.Combine(_temp.Path, "Heat.2160p.mkv"), 900);
        files.Attach(ran, Path.Combine(_temp.Path, "Ran.1080p.mkv"), 700);
        files.Attach(ranBis, Path.Combine(_temp.Path, "Ran.2160p.mkv"), 900);
        Assert.Equal(2, _maintenance.FindDuplicateTitles().Count);

        var result = _maintenance.MergeDuplicateTitles(heatBis);

        // Le groupe de Heat est fusionné et garde ses deux fichiers ; celui de Ran n'a pas bougé.
        Assert.Equal(1, result.Merged);
        var merged = _titles.ListAll().Single(t => t.Year == 1995);
        Assert.Equal([1, 2], files.FilesOf(merged.Id).Select(f => f.Part).Order());
        Assert.Equal(2, _titles.ListAll().Count(t => t.Year == 1985));
        var left = Assert.Single(_maintenance.FindDuplicateTitles());
        Assert.All(left.Titles, t => Assert.Contains("an", t.Title, StringComparison.OrdinalIgnoreCase));

        // Un titre qui n'est plus en double ne fusionne rien, sans erreur.
        Assert.Equal(0, _maintenance.MergeDuplicateTitles(merged.Id).Merged);
    }

    [Fact]
    public void Duplicate_films_merge_into_one_entry_that_keeps_every_encoding()
    {
        var images = Path.Combine(_temp.Path, "images");
        var files = new MediaFileRepository(_db);
        MediaFileProperties Encoding(string codec, int width, int height, int seconds) =>
            new("mkv", codec, width, height, 24, 8, "16:9", [], seconds, [], []);

        // Le même film trois fois : deux encodages différents, et une copie du premier encodage.
        var hd = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var uhd = _titles.Add(new NewTitle(TitleKind.Movie, "heat", 1995));
        var copy = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        files.SaveProperties(files.Attach(hd, Path.Combine(_temp.Path, "Heat.1080p.mkv"), 700), Encoding("h264", 1920, 1080, 10_000));
        files.SaveProperties(files.Attach(uhd, Path.Combine(_temp.Path, "Heat.2160p.mkv"), 900), Encoding("hevc", 3840, 2160, 10_000));
        files.SaveProperties(files.Attach(copy, Path.Combine(_temp.Path, "sauvegarde", "Heat.1080p.mkv"), 700), Encoding("h264", 1920, 1080, 10_000));

        // Un remake d'une autre année n'est pas un doublon.
        var remake = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1986));
        files.Attach(remake, Path.Combine(_temp.Path, "Heat.1986.mkv"), 500);

        var result = _maintenance.MergeDuplicateTitles();

        Assert.Equal(2, result.Merged);
        Assert.Equal(2, result.Files);
        Assert.Equal(1, result.SameEncoding);   // les deux fichiers 1080p sont le même encodage
        Assert.Equal([1986, 1995], _titles.ListAll().Select(t => t.Year).Order());

        // Une seule fiche de 1995, qui garde ses trois fichiers, numérotés à la suite.
        var heat = _titles.ListAll().Single(t => t.Year == 1995);
        Assert.Equal([1, 2, 3], files.FilesOf(heat.Id).Select(f => f.Part).Order());
        Assert.Equal(["h264", "h264", "hevc"], files.TechnicalDetails(heat.Id).Select(d => d.VideoCodec!).Order(StringComparer.Ordinal));
        Assert.Single(files.FilesOf(remake));
        Assert.Empty(_maintenance.CheckIntegrity());

        // Plus rien à fusionner, et la liste des doublons est vide.
        Assert.Equal(0, _maintenance.MergeDuplicateTitles().Merged);
        Assert.Empty(_maintenance.FindDuplicateTitles());
    }


    [Fact]
    public void Duplicates_follow_the_identifiers_not_the_title()
    {
        long Add(string title, int year, long? tmdb, string? imdb)
        {
            var id = _titles.Add(new NewTitle(TitleKind.Movie, title, year));
            using var connection = _db.Open();
            connection.Execute("UPDATE title SET tmdb_id = @tmdb, imdb_id = @imdb WHERE id = @id;", new { id, tmdb, imdb });
            return id;
        }

        // Deux films distincts qui portent le même titre et la même année : leurs identifiants le disent.
        Add("Heat", 1995, 949, "tt0113277");
        Add("Heat", 1995, 11423, "tt0112911");
        Assert.Empty(_maintenance.FindDuplicateTitles());

        // Deux entrées de la même fiche TMDb : c'est un doublon, quels que soient leurs fichiers.
        var first = Add("Heat", 1995, 42, "tt0000042");
        var second = Add("Heat", 1995, 42, null);
        var group = Assert.Single(_maintenance.FindDuplicateTitles());
        Assert.Equal("TMDb 42", group.Reason);
        Assert.Equal([first, second], group.Titles.Select(t => t.Id).Order());

        // Le lien se propage : la troisième rejoint par son identifiant IMDb, pas par son titre.
        var third = Add("Autre montage", 1995, null, "tt0000042");
        var wider = Assert.Single(_maintenance.FindDuplicateTitles());
        Assert.Equal([first, second, third], wider.Titles.Select(t => t.Id).Order());

        // Un titre sans aucun identifiant ne rejoint personne par son seul nom.
        Add("Heat", 1995, null, null);
        Assert.Single(_maintenance.FindDuplicateTitles());
    }

    [Fact]
    public void Scanned_titles_without_any_identifier_still_pair_up_by_title_and_year()
    {
        _titles.Add(new NewTitle(TitleKind.Movie, "Ran", 1985));
        _titles.Add(new NewTitle(TitleKind.Movie, "ran", 1985));
        _titles.Add(new NewTitle(TitleKind.Movie, "Ran", 2000));

        var group = Assert.Single(_maintenance.FindDuplicateTitles());
        Assert.Equal(2, group.Titles.Count);
        Assert.Contains("identifiant", group.Reason, StringComparison.Ordinal);
    }

}
