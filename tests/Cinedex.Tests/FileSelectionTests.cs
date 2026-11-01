using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Media;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cinedex.Tests;

public sealed class FileSelectionTests
{
    [Fact]
    public void Chosen_paths_mix_files_and_folders_and_skip_non_videos_and_known_files()
    {
        using var temp = new TempDirectory();
        var a = Touch(temp, "A", "Heat (1995).mkv", 10);
        var b = Touch(temp, "B", "Alien.1979.mp4", 10);
        var txt = Touch(temp, "B", "notes.txt", 10);
        Touch(temp, "C", "Ran (1985).avi", 10);
        var known = new HashSet<string>([b], StringComparer.OrdinalIgnoreCase);

        var found = LibraryScanner.FromPaths([a, b, txt, Path.Combine(temp.Path, "C"), a], new ScanOptions { MinimumBytes = 1024 }, known, TestContext.Current.CancellationToken);

        // Fichier choisi : pas de taille minimale ; dossier déposé : règles du scan (ici 1 Ko minimum, donc ignoré).
        Assert.Equal(["Heat"], found.Select(c => c.BestTitle));
    }

    [Fact]
    public async Task Import_window_adds_files_without_a_folder_and_imports_them()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        var service = new LibraryImportService(titles, new MediaFileRepository(db), new ImageStore(db, Path.Combine(temp.Path, "img")), null,
            () => new MediaProbe("absent", "absent"), NullLogger<LibraryImportService>.Instance);
        var vm = new ImportFolderViewModel(service) { ReadProperties = false };
        var heat = Touch(temp, "X", "Heat (1995).mkv", 10);
        var alien = Touch(temp, "Y", "Alien (1979).mkv", 10);

        await vm.AddPathsAsync([heat]);
        await vm.AddPathsAsync([alien, heat]);
        Assert.Equal(2, vm.Candidates.Count);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.True(vm.Imported);
        Assert.Equal(["Alien", "Heat"], titles.ListAll().Select(t => t.Title));
    }

    private static string Touch(TempDirectory temp, string folder, string name, int bytes)
    {
        var dir = Directory.CreateDirectory(Path.Combine(temp.Path, folder)).FullName;
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }
}
