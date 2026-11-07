using Cinedex.Media;

namespace Cinedex.Tests;

/// <summary>
/// Le nom du dossier complète celui du fichier. Il ne doit le remplacer que lorsqu'il en dit davantage :
/// un dossier daté comme « Films 2024 » rebaptisait sinon tous les films qu'il contient.
/// </summary>
public sealed class FolderTitleTests
{
    [Theory]
    // Le dossier porte l'année que le fichier n'a pas, et le même titre : on la prend.
    [InlineData("Heat (1995)", "Heat.mkv", "Heat", 1995)]
    [InlineData("Heat.1995.1080p.BluRay", "Heat.mkv", "Heat", 1995)]
    // Le fichier ne nomme pas le film : le dossier le nomme pour lui.
    [InlineData("Heat (1995)", "movie.mkv", "Heat", 1995)]
    [InlineData("Heat (1995)", "VTS_01_1.vob", "Heat", 1995)]
    [InlineData("Heat (1995)", "01.mkv", "Heat", 1995)]
    // Un dossier daté qui n'est pas un film : le titre du fichier reste.
    [InlineData("Films 2024", "Heat.mkv", "Heat", null)]
    [InlineData("Sauvegarde 2019", "Casino.mkv", "Casino", null)]
    // Le fichier porte déjà son année : le dossier ne s'en mêle pas.
    [InlineData("Films 2024", "Heat (1995).mkv", "Heat", 1995)]
    public void The_folder_completes_the_file_name_without_overriding_it(string folder, string file, string title, int? year)
    {
        using var temp = new TempDirectory();
        var directory = Directory.CreateDirectory(Path.Combine(temp.Path, folder));
        File.WriteAllBytes(Path.Combine(directory.FullName, file), new byte[10]);

        var found = Assert.Single(LibraryScanner.Scan(
            temp.Path,
            new ScanOptions { MinimumBytes = 0, ReadNfo = false },
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal((title, year), (found.BestTitle, found.BestYear));
    }
}
