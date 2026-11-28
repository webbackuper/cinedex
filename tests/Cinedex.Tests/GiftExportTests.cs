using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Media;
using Microsoft.Data.Sqlite;

namespace Cinedex.Tests;

[CollectionDefinition(nameof(GiftExportTests), DisableParallelization = true)]
public sealed class GiftExportSerialGroup;

/// <summary>
/// Coffret USB : le disque porte déjà les films ; l'export y ajoute Cinedex.exe, une copie nettoyée du
/// catalogue aux chemins relatifs, VLC, Catalogue.html et LISEZ-MOI.txt. Hors parallélisme : la base des
/// chemins relatifs est globale à l'application (un seul coffret à la fois).
/// </summary>
[Collection(nameof(GiftExportTests))]
public sealed class GiftExportTests : IDisposable
{
    private const long Cluster = 128 * 1024;

    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _titles;
    private readonly MediaFileRepository _files;
    private readonly ImageStore _images;
    private readonly string _disk;
    private readonly string _exe;
    private readonly string _vlc;
    private readonly long _heat;
    private readonly long _alien;
    private readonly long _elsewhere;

    public GiftExportTests()
    {
        var source = Directory.CreateDirectory(Path.Combine(_temp.Path, "source")).FullName;
        _db = new CatalogDatabase(Path.Combine(source, "cinedex.db"));
        _db.Initialize(Path.Combine(source, "backups"));
        _titles = new TitleRepository(_db);
        _files = new MediaFileRepository(_db) { VolumeProvider = () => new Volumes([]) };
        _images = new ImageStore(_db, Path.Combine(source, "images"));

        _disk = Directory.CreateDirectory(Path.Combine(_temp.Path, "NOEL2026")).FullName;
        var films = Directory.CreateDirectory(Path.Combine(_disk, "Films")).FullName;
        _heat = Film("Heat", 1995, Path.Combine(films, "Heat (1995).mkv"));
        _alien = Film("Alien", 1979, Path.Combine(films, "Science-fiction", "Alien #1 (1979).mkv"));
        _elsewhere = Film("Casino", 1995, Path.Combine(_temp.Path, "PC", "Casino.mkv"));

        // Ce qui est personnel, et ne doit pas partir sur le disque offert.
        _titles.Lend(_heat, "Paul", DateTimeOffset.Now);
        _titles.AddViewing(_heat, DateTimeOffset.Now);
        _titles.SetFavorite([_heat], true);
        _titles.Update(_titles.Get(_heat)! with { UserRating = 9, Comments = "Vu au cinéma avec Marie", Overview = "Un braqueur et un flic." });
        new SettingsStore(_db).Set("tmdb.language", "fr-FR");
        _files.AddScanRoot(films, "movies");

        var poster = Path.Combine(_temp.Path, "poster.jpg");
        File.WriteAllBytes(poster, [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]);
        _images.SetFromFile(_heat, poster, "poster");

        var bin = Directory.CreateDirectory(Path.Combine(_temp.Path, "bin")).FullName;
        _exe = Path.Combine(bin, "Cinedex.exe");
        File.WriteAllBytes(_exe, new byte[3000]);
        _vlc = Directory.CreateDirectory(Path.Combine(bin, "tools", "VLC")).FullName;
        File.WriteAllBytes(Path.Combine(_vlc, "vlc.exe"), new byte[100]);
        File.WriteAllText(Path.Combine(_vlc, "COPYING.txt"), "GNU GPL");
    }

    public void Dispose()
    {
        MediaPaths.RelativeBase = null;
        SqliteConnection.ClearAllPools();
        _temp.Dispose();
    }

    private long Film(string title, int year, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[64]);
        var id = _titles.Add(new NewTitle(TitleKind.Movie, title, year));
        _files.Attach(id, path, 64);
        return id;
    }

    private GiftExportService Service(long free = 1L << 40) => new(_titles, _files, _db, _images.ImagesDirectory, new PeopleRepository(_db))
    {
        ExecutablePath = _exe,
        PlayerDirectory = _vlc,
        DiskInfo = _ => (free, Cluster),
    };

    private static List<T> Query<T>(CatalogDatabase db, string sql, Func<SqliteDataReader, T> read)
    {
        using var connection = db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
        {
            rows.Add(read(reader));
        }

        return rows;
    }

    [Fact]
    public void The_plan_lists_titles_off_the_disk_and_counts_space_by_allocation_unit()
    {
        var plan = Service().Plan(_disk, "NOEL2026");

        var off = Assert.Single(plan.OffDisk);
        Assert.Equal(_elsewhere, off.TitleId);
        Assert.Equal(2, plan.TitleCount);
        Assert.False(plan.Replaces);
        Assert.True(plan.HasPlayer);
        Assert.Null(plan.Problem);

        // Chaque fichier occupe au moins une unité d'allocation : 3 000 octets d'exécutable comptent 128 Ko.
        Assert.Equal(0, plan.RequiredBytes % Cluster);
        Assert.True(plan.RequiredBytes >= 6 * Cluster);
        Assert.True(plan.CanExport);
    }

    [Fact]
    public void Not_enough_room_or_a_foreign_folder_stops_the_export_before_writing_anything()
    {
        var tight = Service(free: Cluster).Plan(_disk, "NOEL2026");
        Assert.False(tight.HasRoom);
        Assert.False(tight.CanExport);
        Assert.Throws<InvalidOperationException>(() => Service(free: Cluster).Export(tight, [_elsewhere]));
        Assert.False(File.Exists(Path.Combine(_disk, "Cinedex.exe")));

        // Un dossier « Cinedex » qui n'est pas un coffret n'est jamais effacé.
        var foreign = Directory.CreateDirectory(Path.Combine(_disk, AppPaths.GiftFolderName)).FullName;
        File.WriteAllText(Path.Combine(foreign, "mes-notes.txt"), "à garder");
        var plan = Service().Plan(_disk, "NOEL2026");
        Assert.NotNull(plan.Problem);
        Assert.False(plan.CanExport);
        Assert.True(File.Exists(Path.Combine(foreign, "mes-notes.txt")));
    }

    [Fact]
    public void Titles_off_the_disk_must_be_excluded()
    {
        var plan = Service().Plan(_disk, "NOEL2026");
        Assert.Throws<InvalidOperationException>(() => Service().Export(plan, []));
    }

    [Fact]
    public void The_export_writes_the_gift_without_personal_data_and_with_relative_paths()
    {
        var service = Service();
        var plan = service.Plan(_disk, "NOEL2026");

        var result = service.Export(plan, [_elsewhere]);

        Assert.Equal(2, result.Titles);
        Assert.Equal(2, result.Files);
        Assert.True(File.Exists(Path.Combine(_disk, "Cinedex.exe")));
        Assert.True(File.Exists(Path.Combine(_disk, "Catalogue.html")));
        Assert.True(File.Exists(Path.Combine(_disk, "LISEZ-MOI.txt")));
        Assert.Equal("NOEL2026", File.ReadAllText(Path.Combine(_disk, "Cinedex", "coffret.flag")));
        Assert.True(File.Exists(Path.Combine(_disk, "Cinedex", "VLC", "vlc.exe")));
        Assert.True(File.Exists(Path.Combine(_disk, "Cinedex", "VLC", "COPYING.txt")));
        Assert.Contains("Exécuter quand même", File.ReadAllText(Path.Combine(_disk, "LISEZ-MOI.txt")), StringComparison.Ordinal);

        // Les films ne sont pas touchés, et l'original non plus.
        Assert.Equal(64, new FileInfo(Path.Combine(_disk, "Films", "Heat (1995).mkv")).Length);
        Assert.Equal(3, _titles.Query(new TitleQuery()).Count);
        Assert.NotNull(_titles.Get(_heat)!.CurrentLoan);

        var copy = new CatalogDatabase(Path.Combine(_disk, "Cinedex", "catalogs", AppPaths.DefaultCatalogName, AppPaths.DatabaseFileName));
        Assert.Equal(0L, Query(copy, "SELECT count(*) FROM loan", r => r.GetInt64(0))[0]);
        Assert.Equal(0L, Query(copy, "SELECT count(*) FROM viewing", r => r.GetInt64(0))[0]);
        Assert.Equal(0L, Query(copy, "SELECT count(*) FROM scan_root", r => r.GetInt64(0))[0]);
        Assert.Equal(0L, Query(copy, "SELECT count(*) FROM title WHERE is_favorite = 1 OR user_rating IS NOT NULL OR comments IS NOT NULL", r => r.GetInt64(0))[0]);
        Assert.Empty(Query(copy, "SELECT id FROM title WHERE title = 'Casino'", r => r.GetInt64(0)));
        Assert.Equal(
            ["media.player", "media.playerArgs"],
            Query(copy, "SELECT key FROM setting ORDER BY key", r => r.GetString(0)));
        Assert.Equal(
            [Path.Combine("Films", "Heat (1995).mkv"), Path.Combine("Films", "Science-fiction", "Alien #1 (1979).mkv")],
            Query(copy, "SELECT path FROM media_file ORDER BY path", r => r.GetString(0)));
        Assert.Equal("Un braqueur et un flic.", new TitleRepository(copy).Get(_heat)!.Overview);

        // L'affiche suit, au même chemin relatif dans le dossier des images du coffret.
        var poster = new ImageStore(copy, Path.Combine(_disk, "Cinedex", "catalogs", AppPaths.DefaultCatalogName, "images")).PathFor(_heat, "poster");
        Assert.True(File.Exists(poster));
    }

    [Fact]
    public void The_gift_finds_its_films_whatever_the_drive_letter()
    {
        var service = Service();
        service.Export(service.Plan(_disk, "NOEL2026"), [_elsewhere]);

        // Le disque branché ailleurs (autre lettre, autre point de montage) : le même contenu, une autre racine.
        var moved = Path.Combine(_temp.Path, "F_drive");
        Copy(_disk, moved);
        SqliteConnection.ClearAllPools();
        var paths = AppPaths.Resolve(baseDirectory: moved);
        Assert.True(paths.IsGift);
        Assert.Equal("NOEL2026", paths.GiftName);
        Assert.Equal(Path.Combine(moved, "Cinedex"), paths.DataRoot);

        MediaPaths.RelativeBase = moved;
        var copy = new CatalogDatabase(paths.CatalogDatabasePath(AppPaths.DefaultCatalogName));
        var files = new MediaFileRepository(copy) { VolumeProvider = () => new Volumes([]) };
        var heat = Assert.Single(files.FilesOf(_heat));
        Assert.Equal(Path.Combine(moved, "Films", "Heat (1995).mkv"), heat.FullPath);
        Assert.True(File.Exists(heat.FullPath));

        // Le lecteur aussi est relatif à l'exécutable.
        var player = new SettingsStore(copy).Get(MediaContext.PlayerKey)!;
        Assert.False(Path.IsPathRooted(player));
        Assert.Equal(Path.Combine(moved, "Cinedex", "VLC", "vlc.exe"), Player.ResolveAppRelative(player, moved));
        Assert.True(File.Exists(Player.ResolveAppRelative(player, moved)));
    }

    [Fact]
    public void Exporting_again_replaces_the_previous_gift()
    {
        var service = Service();
        service.Export(service.Plan(_disk, "NOEL2026"), [_elsewhere]);
        var again = service.Plan(_disk, "NOEL2026");

        Assert.True(again.Replaces);
        Assert.Null(again.Problem);
        service.Export(again, [_elsewhere]);
        Assert.True(File.Exists(Path.Combine(_disk, "Cinedex", "coffret.flag")));
    }

    [Fact]
    public async Task A_gift_window_hides_what_it_does_not_offer_and_never_reaches_the_network()
    {
        var paths = new AppPaths(Path.Combine(_temp.Path, "gift-data"), isPortable: true, giftName: "NOEL2026");
        var vm = new MainWindowViewModel(_titles, paths, new MainWindowViewModelTests.FakeDialogs()) { GiftExport = Service() };

        Assert.True(vm.IsGift);
        Assert.False(vm.IsFull);
        Assert.False(vm.CanExportGift);

        using var client = new HttpClient(new OfflineHandler());
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri("https://api.themoviedb.org/3/movie/949"), TestContext.Current.CancellationToken));

        var full = new MainWindowViewModel(_titles, new AppPaths(_temp.Path, isPortable: false), new MainWindowViewModelTests.FakeDialogs()) { GiftExport = Service() };
        Assert.True(full.IsFull);
        Assert.True(full.CanExportGift);
    }

    [Fact]
    public void The_page_is_self_contained_and_escapes_every_field()
    {
        var page = GiftCatalogPage.Render("Noël <b>2026</b>",
        [
            new GiftCatalogPage.Entry(
                "</script><img src=x onerror=alert(1)>", null, 1995, "2 h 50", "Policier, Thriller",
                "Synopsis avec <script>alert('x')</script> & \"guillemets\"", null, 8.3, 7.9, "Michael Mann", "Al Pacino",
                "Cinedex/catalogs/catalogue/images/a.jpg", [GiftCatalogPage.ToUrl(Path.Combine("Films", "Heat #1 (1995).mkv"))]),
        ]);

        Assert.DoesNotContain("http://", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script>alert", page, StringComparison.Ordinal);
        Assert.DoesNotContain("</script><img", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>2026</b>", page, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;2026&lt;/b&gt;", page, StringComparison.Ordinal);
        Assert.Equal(2, page.Split("<script").Length - 1);
        Assert.Contains("Films/Heat%20%231%20%281995%29.mkv", page, StringComparison.Ordinal);
        Assert.Contains("prefers-color-scheme:dark", page, StringComparison.Ordinal);
    }

    private static void Copy(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
