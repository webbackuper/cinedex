using Bobine.App.Services;
using Bobine.Core.Titles;
using Bobine.Data;
using Bobine.Media;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bobine.Tests;

public sealed class LibraryImportTests
{
    [Fact]
    public async Task Folder_import_without_tmdb_creates_titles_files_and_covers()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "bobine.db"));
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
        Assert.Equal(1, result.SkippedEpisodes);
        Assert.Contains(result.Messages, m => m.Contains("ffprobe", StringComparison.Ordinal));
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
}
