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

        Assert.False(vm.IsIndeterminate);
        await vm.ImportCommand.ExecuteAsync(null);

        Assert.True(vm.Imported);
        Assert.Equal(["Alien", "Heat"], titles.ListAll().Select(t => t.Title));

        // La barre de progression a suivi l'import fichier par fichier (les signalements arrivent en différé).
        Assert.Equal(2, vm.Total);
        for (var waited = 0; vm.Done < 2 && waited < 2000; waited += 10)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal(2, vm.Done);
    }

    [Fact]
    public async Task Rescanning_a_folder_leaves_the_files_already_imported_unchecked()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        var folder = Directory.CreateDirectory(Path.Combine(temp.Path, "Films")).FullName;
        File.WriteAllBytes(Path.Combine(folder, "Heat (1995).mkv"), new byte[10]);

        LibraryImportService Service(string label) => new(
            titles,
            new MediaFileRepository(db) { VolumeProvider = () => new Volumes([(temp.Path, label)]) },
            new ImageStore(db, Path.Combine(temp.Path, "img")), null,
            () => new MediaProbe("absent", "absent"), NullLogger<LibraryImportService>.Instance);

        // Fichiers de test minuscules : pas de taille minimale.
        var scan = new ScanSettings { MinimumMegabytes = 0 };

        var first = new ImportFolderViewModel(Service("FILMS-A"), scan) { Folder = folder, ReadProperties = false };
        await first.ScanCommand.ExecuteAsync(null);
        Assert.True(Assert.Single(first.Candidates).IsSelected);
        await first.ImportCommand.ExecuteAsync(null);
        Assert.Equal(["Heat"], titles.ListAll().Select(t => t.Title));

        // Le disque est monté sous un autre nom : l'analyse reproposera le fichier…
        var again = new ImportFolderViewModel(Service("FILMS-B"), scan) { Folder = folder, ReadProperties = false };
        await again.ScanCommand.ExecuteAsync(null);
        var row = Assert.Single(again.Candidates);

        // … mais décoché et annoncé comme tel, donc un import n'ajoute pas de doublon.
        Assert.True(row.IsKnown);
        Assert.False(row.IsSelected);
        Assert.Equal("déjà au catalogue", row.Hint);
        Assert.Contains("déjà au catalogue", again.Message, StringComparison.Ordinal);
        await again.ImportCommand.ExecuteAsync(null);
        Assert.Equal(["Heat"], titles.ListAll().Select(t => t.Title));
    }

    [Fact]
    public async Task Rescanning_proposes_the_new_files_even_when_the_film_is_already_in_the_catalogue()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        var files = new MediaFileRepository(db);
        var service = new LibraryImportService(titles, files, new ImageStore(db, Path.Combine(temp.Path, "img")), null,
            () => new MediaProbe("absent", "absent"), NullLogger<LibraryImportService>.Instance);
        var scan = new ScanSettings { MinimumMegabytes = 0 };
        var folder = Directory.CreateDirectory(Path.Combine(temp.Path, "Films")).FullName;

        ImportFolderViewModel Window() => new(service, scan) { Folder = folder, ReadProperties = false, UseTmdb = false };

        // Premier passage : un seul fichier, importé.
        File.WriteAllBytes(Path.Combine(folder, "Heat (1995).mkv"), new byte[10]);
        var first = Window();
        await first.ScanCommand.ExecuteAsync(null);
        Assert.Equal(["Heat"], first.Candidates.Select(c => c.Title));
        await first.ImportCommand.ExecuteAsync(null);
        var heat = Assert.Single(titles.ListAll());
        Assert.Single(files.FilesOf(heat.Id));

        // Le même film arrive en seconde version, dans un sous-dossier.
        var uhd = Directory.CreateDirectory(Path.Combine(folder, "4K")).FullName;
        File.WriteAllBytes(Path.Combine(uhd, "Heat (1995) 2160p.mkv"), new byte[10]);

        // Rescan : seul le nouveau fichier est proposé, et il est coché.
        var again = Window();
        await again.ScanCommand.ExecuteAsync(null);
        var proposed = Assert.Single(again.Candidates);
        Assert.Equal(Path.Combine(uhd, "Heat (1995) 2160p.mkv"), proposed.Path);
        Assert.True(proposed.IsSelected, "le nouveau fichier doit être coché");
        Assert.False(proposed.IsKnown);

        // Import : une seconde entrée, malgré le film déjà présent, avec son propre fichier.
        await again.ImportCommand.ExecuteAsync(null);
        var both = titles.ListAll();
        Assert.Equal(2, both.Count);
        Assert.All(both, t => Assert.Equal("Heat", t.Title));
        Assert.All(both, t => Assert.Single(files.FilesOf(t.Id)));
        Assert.Equal(
            new[] { Path.Combine(folder, "Heat (1995).mkv"), Path.Combine(uhd, "Heat (1995) 2160p.mkv") }.Order(StringComparer.OrdinalIgnoreCase),
            both.SelectMany(t => files.FilesOf(t.Id)).Select(f => f.FullPath).Order(StringComparer.OrdinalIgnoreCase));

        // Un troisième passage sans rien de neuf ne propose plus rien.
        var third = Window();
        await third.ScanCommand.ExecuteAsync(null);
        Assert.Empty(third.Candidates);
        Assert.Equal(2, titles.ListAll().Count);
    }

    private static string Touch(TempDirectory temp, string folder, string name, int bytes)
    {
        var dir = Directory.CreateDirectory(Path.Combine(temp.Path, folder)).FullName;
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }
}
