using Cinedex.App.Services;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Media;
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
    public async Task A_second_video_file_of_the_same_film_joins_it_instead_of_creating_a_double()
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

        // Un seul « Heat » de 1995, avec ses deux fichiers ; le remake de 1986 reste à part.
        Assert.Equal(["Heat", "Heat", "Ran"], titles.ListAll().Select(t => t.Title).Order(StringComparer.Ordinal));
        Assert.Equal([1985, 1986, 1995], titles.ListAll().Select(t => t.Year).Order());
        var heat = titles.ListAll().Single(t => t is { Title: "Heat", Year: 1995 });
        Assert.Equal([1, 2], files.FilesOf(heat.Id).Select(f => f.Part).Order());

        // Les parties explicites gardent leur numéro.
        var ran = titles.ListAll().Single(t => t.Title == "Ran");
        Assert.Equal([1, 2], files.FilesOf(ran.Id).Select(f => f.Part).Order());
        Assert.Equal(3, result.Added + result.WithoutMetadata);

        // Réimporter le même dossier n'ajoute rien : les fichiers sont déjà connus.
        var again = await service.ScanAsync(library, options, TestContext.Current.CancellationToken);
        Assert.Empty(again);
    }

}
