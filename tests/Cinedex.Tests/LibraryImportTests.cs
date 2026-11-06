using System.Net;
using System.Text;
using Cinedex.App.Services;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Media;
using Cinedex.Metadata.Tmdb;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cinedex.Tests;

public sealed class LibraryImportTests
{
    [Fact]
    public async Task Folder_import_without_tmdb_creates_titles_files_and_covers()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        var files = new MediaFileRepository(db);
        var images = new ImageStore(db, Path.Combine(temp.Path, "images"));
        var service = new LibraryImportService(titles, files, images, null, () => new MediaProbe(Path.Combine(temp.Path, "absent.exe")), NullLogger<LibraryImportService>.Instance);

        var library = Directory.CreateDirectory(Path.Combine(temp.Path, "Films")).FullName;
        var heatDir = Directory.CreateDirectory(Path.Combine(library, "Heat (1995)")).FullName;
        File.WriteAllBytes(Path.Combine(heatDir, "Heat.1995.1080p.mkv"), new byte[2048]);
        File.WriteAllBytes(Path.Combine(heatDir, "folder.jpg"), [0xFF, 0xD8, 0xFF, 0xE0, 0, 0]);
        File.WriteAllBytes(Path.Combine(library, "Breaking.Bad.S01E01.mkv"), new byte[2048]);

        service.RememberFolder(library);
        var candidates = await service.ScanAsync(library, new ScanOptions { MinimumBytes = 1024 }, TestContext.Current.CancellationToken);
        var result = await service.ImportAsync(candidates, new LibraryImportOptions(), null, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.WithoutMetadata);
        Assert.Equal(1, result.Episodes);
        // Sans outil d'analyse sur le poste, l'import le signale. (Les machines Linux de la CI ont ffprobe dans le PATH.)
        if (!new MediaProbe(Path.Combine(temp.Path, "absent.exe")).IsAvailable)
        {
            Assert.Contains(result.Messages, m => m.Contains("ffprobe", StringComparison.Ordinal));
        }
        var heat = Assert.Single(titles.ListAll());
        Assert.Equal(("Heat", 1995), (heat.Title, heat.Year));
        Assert.NotNull(images.PathFor(heat.Id, "poster"));
        Assert.Single(files.FilesOf(heat.Id));

        // Un second passage ne propose plus le fichier déjà importé.
        Assert.Single(await service.ScanAsync(library, new ScanOptions { MinimumBytes = 1024 }, TestContext.Current.CancellationToken));

        File.Delete(Path.Combine(heatDir, "Heat.1995.1080p.mkv"));
        Assert.Equal((1, 0), await service.CheckFilesAsync(TestContext.Current.CancellationToken));
        Assert.Single(titles.Query(new TitleQuery(Filter: QuickFilter.MissingFiles)));
    }

    [Fact]
    public async Task A_second_video_file_of_the_same_film_gets_its_own_entry()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        var files = new MediaFileRepository(db);
        var service = new LibraryImportService(titles, files, new ImageStore(db, Path.Combine(temp.Path, "images")), null,
            () => new MediaProbe(Path.Combine(temp.Path, "absent.exe")), NullLogger<LibraryImportService>.Instance);

        var library = Directory.CreateDirectory(Path.Combine(temp.Path, "Films")).FullName;
        // Le même film en deux versions, plus un film en deux parties (CD1/CD2) et un homonyme d'une autre année.
        File.WriteAllBytes(Path.Combine(library, "Heat (1995) 1080p.mkv"), new byte[2048]);
        File.WriteAllBytes(Path.Combine(library, "Heat (1995) 2160p.mkv"), new byte[2048]);
        File.WriteAllBytes(Path.Combine(library, "Ran (1985) cd1.avi"), new byte[2048]);
        File.WriteAllBytes(Path.Combine(library, "Ran (1985) cd2.avi"), new byte[2048]);
        File.WriteAllBytes(Path.Combine(library, "Heat (1986).mkv"), new byte[2048]);

        var options = new ScanOptions { MinimumBytes = 1024 };
        var candidates = await service.ScanAsync(library, options, TestContext.Current.CancellationToken);
        var result = await service.ImportAsync(candidates, new LibraryImportOptions { UseTmdb = false }, null, TestContext.Current.CancellationToken);

        // Cinedex catalogue des fichiers : les deux versions de « Heat » (1995) ont chacune leur entrée.
        Assert.Equal(["Heat", "Heat", "Heat", "Ran"], titles.ListAll().Select(t => t.Title).Order(StringComparer.Ordinal));
        var heat95 = titles.ListAll().Where(t => t is { Title: "Heat", Year: 1995 }).ToList();
        Assert.Equal(2, heat95.Count);
        Assert.All(heat95, t => Assert.Single(files.FilesOf(t.Id)));

        // Elles se retrouvent dans les doublons, prêtes à être regroupées en un film à plusieurs sources.
        var maintenance = new MaintenanceRepository(db);
        var group = Assert.Single(maintenance.FindDuplicateTitles());
        Assert.Equal([.. heat95.Select(t => t.Id).Order()], [.. group.Titles.Select(t => t.Id).Order()]);
        maintenance.MergeDuplicateTitles();
        var merged = titles.ListAll().Single(t => t is { Title: "Heat", Year: 1995 });
        Assert.Equal([1, 2], files.FilesOf(merged.Id).Select(f => f.Part).Order());

        // Le remake d'une autre année n'a jamais été confondu, et les parties explicites restent une seule entrée.
        Assert.Equal([1985, 1986, 1995], titles.ListAll().Select(t => t.Year).Order());
        var ran = titles.ListAll().Single(t => t.Title == "Ran");
        Assert.Equal([1, 2], files.FilesOf(ran.Id).Select(f => f.Part).Order());
        Assert.Equal(4, result.Added + result.WithoutMetadata);

        // Réimporter le même dossier n'ajoute rien : les fichiers sont déjà connus.
        var again = await service.ScanAsync(library, options, TestContext.Current.CancellationToken);
        Assert.Empty(again);
    }


    [Fact]
    public async Task Two_files_of_one_tmdb_film_give_two_entries_that_merge_into_one_film_with_two_sources()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        var files = new MediaFileRepository(db);
        var images = new ImageStore(db, Path.Combine(temp.Path, "img"));
        var tmdb = new TmdbClient(new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath.Contains("/search/", StringComparison.Ordinal)
            ? Json("""{"results":[{"id":949,"title":"Heat","release_date":"1995-12-15"}]}""")
            : Json("""{"id":949,"title":"Heat","release_date":"1995-12-15","runtime":170,"imdb_id":"tt0113277","overview":"Un braqueur et un flic."}"""))),
            _ => ValueTask.FromResult<string?>("token"));
        var metadata = new MetadataService(tmdb, new MetadataRepository(db), images, NullLogger<MetadataService>.Instance);
        var service = new LibraryImportService(titles, files, images, metadata, () => new MediaProbe("absent", "absent"), NullLogger<LibraryImportService>.Instance);

        // Le même film, deux fichiers à des emplacements différents.
        var library = Directory.CreateDirectory(Path.Combine(temp.Path, "Films")).FullName;
        var hd = Directory.CreateDirectory(Path.Combine(library, "HD")).FullName;
        var uhd = Directory.CreateDirectory(Path.Combine(library, "UHD")).FullName;
        File.WriteAllBytes(Path.Combine(hd, "Heat (1995).mkv"), new byte[2048]);
        File.WriteAllBytes(Path.Combine(uhd, "Heat (1995).mkv"), new byte[2048]);

        var candidates = await service.ScanAsync(library, new ScanOptions { MinimumBytes = 1024 }, TestContext.Current.CancellationToken);
        var result = await service.ImportAsync(candidates, new LibraryImportOptions(), null, TestContext.Current.CancellationToken);

        // Deux entrées, une par fichier, toutes deux liées à la même fiche TMDb : chacune reste actualisable.
        Assert.Equal(2, result.Added);
        var both = titles.ListAll();
        Assert.Equal(2, both.Count);
        Assert.All(both, t => Assert.Equal("Heat", t.Title));
        var detailed = both.Select(t => titles.Get(t.Id)!).ToList();
        Assert.All(detailed, t => Assert.Equal(949, t.TmdbId));
        Assert.All(detailed, t => Assert.Equal("tt0113277", t.ImdbId));
        Assert.All(detailed, t => Assert.Equal("Un braqueur et un flic.", t.Overview));
        Assert.All(both, t => Assert.Single(files.FilesOf(t.Id)));

        // La fusion en fait un seul film à deux sources, la fiche liée à TMDb étant gardée.
        var maintenance = new MaintenanceRepository(db);
        Assert.Single(maintenance.FindDuplicateTitles());
        Assert.Equal(1, maintenance.MergeDuplicateTitles().Merged);
        var film = Assert.Single(titles.ListAll());
        Assert.Equal(949, titles.Get(film.Id)!.TmdbId);
        Assert.Equal([1, 2], files.FilesOf(film.Id).Select(f => f.Part).Order());
    }

    /// <summary>
    /// Un film que TMDb ne sait pas rendre (fiche retirée, numéro périmé, erreur) ne doit pas faire perdre le
    /// fichier : il entre au catalogue sans fiche, et le compte rendu dit lequel et pourquoi.
    /// </summary>
    [Fact]
    public async Task A_film_tmdb_cannot_serve_is_still_imported_without_a_record()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        var files = new MediaFileRepository(db);
        var images = new ImageStore(db, Path.Combine(temp.Path, "img"));

        // TMDb trouve les deux films à la recherche, mais répond 404 sur la fiche du second.
        var tmdb = new TmdbClient(new HttpClient(new Handler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.Contains("/search/", StringComparison.Ordinal))
                {
                    return request.RequestUri.Query.Contains("Heat", StringComparison.OrdinalIgnoreCase)
                        ? Json("""{"results":[{"id":949,"title":"Heat","release_date":"1995-12-15"}]}""")
                        : Json("""{"results":[{"id":6660,"title":"Meurtres à Collioure","release_date":"2015-01-01"}]}""");
                }

                return path.Contains("/6660", StringComparison.Ordinal)
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : Json("""{"id":949,"title":"Heat","release_date":"1995-12-15","runtime":170,"overview":"Un braqueur et un flic."}""");
            })),
            _ => ValueTask.FromResult<string?>("token"));
        var metadata = new MetadataService(tmdb, new MetadataRepository(db), images, NullLogger<MetadataService>.Instance);
        var service = new LibraryImportService(titles, files, images, metadata, () => new MediaProbe("absent", "absent"), NullLogger<LibraryImportService>.Instance);

        var library = Directory.CreateDirectory(Path.Combine(temp.Path, "Films")).FullName;
        File.WriteAllBytes(Path.Combine(library, "Heat (1995).mkv"), new byte[2048]);
        File.WriteAllBytes(Path.Combine(library, "Meurtres A Collioure (2015).mp4"), new byte[2048]);

        var candidates = await service.ScanAsync(library, new ScanOptions { MinimumBytes = 1024 }, TestContext.Current.CancellationToken);
        var result = await service.ImportAsync(candidates, new LibraryImportOptions(), null, TestContext.Current.CancellationToken);

        // Aucun échec : un fichier entre toujours, avec ou sans fiche.
        Assert.Equal(0, result.Failed);
        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.WithoutMetadata);
        Assert.Equal(2, titles.ListAll().Count);

        var orphan = titles.ListAll().Select(t => titles.Get(t.Id)!).Single(t => t.TmdbId is null);
        Assert.Contains("Collioure", orphan.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Single(files.FilesOf(orphan.Id));

        // Le compte rendu nomme le fichier et dit pourquoi, pour qu'on puisse le reprendre à la main.
        Assert.Contains(result.Messages, m =>
            m.Contains("Meurtres", StringComparison.OrdinalIgnoreCase) && m.Contains("sans fiche TMDb", StringComparison.Ordinal));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

}
