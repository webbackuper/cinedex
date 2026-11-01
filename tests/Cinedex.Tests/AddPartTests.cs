using Cinedex.App.Services;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Media;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cinedex.Tests;

public sealed class AddPartTests
{
    [Fact]
    public async Task A_second_file_becomes_part_two_and_duplicates_are_refused()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        var files = new MediaFileRepository(db);
        var service = new LibraryImportService(titles, files, new ImageStore(db, Path.Combine(temp.Path, "img")), null,
            () => new MediaProbe("absent", "absent"), NullLogger<LibraryImportService>.Instance);
        var cd1 = Path.Combine(temp.Path, "Ran CD1.avi");
        var cd2 = Path.Combine(temp.Path, "Ran CD2.avi");
        var notes = Path.Combine(temp.Path, "notes.txt");
        File.WriteAllBytes(cd1, new byte[10]);
        File.WriteAllBytes(cd2, new byte[20]);
        File.WriteAllText(notes, "x");
        var id = titles.Add(new NewTitle(TitleKind.Movie, "Ran", 1985));
        files.Attach(id, cd1, 10);

        Assert.Equal(2, await service.AddPartAsync(id, cd2, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal([1, 2], files.FilesOf(id).Select(f => f.Part).Order());
        Assert.Contains("Partie 2 : " + cd2, string.Join('\n', files.TechnicalDetails(id).Select(f => f.Describe())), StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddPartAsync(id, cd2, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddPartAsync(id, notes, cancellationToken: TestContext.Current.CancellationToken));
    }
}
