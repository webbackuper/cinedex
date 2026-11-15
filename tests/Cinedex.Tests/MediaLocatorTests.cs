using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Media;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cinedex.Tests;

/// <summary>
/// Avant de lire un fichier, on vérifie qu'il est là. Absent, on demande son disque, et l'on sait chercher
/// le même chemin sur les autres lecteurs : la lettre d'un disque USB change d'un branchement à l'autre.
/// </summary>
public sealed class MediaLocatorTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(@"E:\Films\Heat (1995)\Heat.mkv", "Films/Heat (1995)/Heat.mkv")]
    [InlineData("e:/Films/Heat.mkv", "Films/Heat.mkv")]
    [InlineData("/Volumes/FILMS1/Films/Heat.mkv", "Films/Heat.mkv")]
    [InlineData("/media/julien/FILMS1/Films/Heat.mkv", "Films/Heat.mkv")]
    [InlineData("/run/media/julien/FILMS1/Heat.mkv", "Heat.mkv")]
    [InlineData("/mnt/films/Heat.mkv", "Heat.mkv")]
    [InlineData(@"\\nas\video\Films\Heat.mkv", "Films/Heat.mkv")]
    public void The_part_of_a_path_that_does_not_depend_on_where_the_disc_is_plugged(string path, string expected)
    {
        Assert.Equal(expected.Replace('/', Path.DirectorySeparatorChar), MediaLocator.RelativeToVolume(path));
    }

    [Fact]
    public void The_same_path_is_found_under_another_root()
    {
        var other = Directory.CreateDirectory(Path.Combine(_temp.Path, "lecteur-G"));
        var empty = Directory.CreateDirectory(Path.Combine(_temp.Path, "lecteur-F"));
        Directory.CreateDirectory(Path.Combine(other.FullName, "Films"));
        var real = Path.Combine(other.FullName, "Films", "Heat.mkv");
        File.WriteAllBytes(real, new byte[8]);

        Assert.Equal(real, MediaLocator.FindElsewhere(@"E:\Films\Heat.mkv", [empty.FullName, other.FullName]));
        Assert.Null(MediaLocator.FindElsewhere(@"E:\Films\Absent.mkv", [empty.FullName, other.FullName]));
        Assert.Null(MediaLocator.FindElsewhere(@"E:\Films\Heat.mkv", []));
    }

    [Fact]
    public async Task A_present_file_is_used_without_a_question()
    {
        var file = Path.Combine(_temp.Path, "Heat.mkv");
        File.WriteAllBytes(file, new byte[8]);
        var asked = 0;

        var located = await MediaLocator.LocateAsync(
            () => [new MediaCandidate(file, "FILMS1")],
            (_, _, _) =>
            {
                asked++;
                return Task.FromResult(-1);
            });

        Assert.Equal(file, located!.Path);
        Assert.False(located.FoundElsewhere);
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task A_missing_file_asks_for_its_disc_and_can_be_retried()
    {
        var file = Path.Combine(_temp.Path, "Heat.mkv");
        var messages = new List<string>();
        var answers = new List<string[]>();

        var located = await MediaLocator.LocateAsync(
            () => [new MediaCandidate(file, "FILMS1")],
            (_, message, choices) =>
            {
                messages.Add(message);
                answers.Add(choices);

                // Le disque est « branché » pendant que la question est à l'écran, puis on réessaie.
                File.WriteAllBytes(file, new byte[8]);
                return Task.FromResult(0);
            });

        Assert.Equal(file, located!.Path);
        var message = Assert.Single(messages);
        Assert.Contains("FILMS1", message, StringComparison.Ordinal);
        Assert.Contains(file, message, StringComparison.Ordinal);
        Assert.Equal(3, answers[0].Length);
        Assert.Equal(MediaLocator.SearchLabel, answers[0][1]);
    }

    [Fact]
    public async Task Searching_the_other_drives_finds_the_file_or_says_it_did_not()
    {
        var other = Directory.CreateDirectory(Path.Combine(_temp.Path, "G"));
        var real = Path.Combine(other.FullName, "Heat.mkv");
        var messages = new List<string>();
        var turn = 0;

        var located = await MediaLocator.LocateAsync(
            () => [new MediaCandidate(@"E:\Heat.mkv", "FILMS1", 7)],
            (_, message, _) =>
            {
                messages.Add(message);
                if (++turn == 2)
                {
                    File.WriteAllBytes(real, new byte[8]);
                }

                return Task.FromResult(1);
            },
            () => [other.FullName]);

        // Première recherche : rien, et la question revient en le disant. Seconde : trouvé.
        Assert.Equal(2, messages.Count);
        Assert.Contains("Introuvable sur les 1 disque(s)", messages[1], StringComparison.Ordinal);
        Assert.Equal(real, located!.Path);
        Assert.True(located.FoundElsewhere);
        Assert.Equal(7, located.File.FileId);
    }

    [Fact]
    public async Task Giving_up_returns_nothing()
    {
        var located = await MediaLocator.LocateAsync(
            () => [new MediaCandidate(Path.Combine(_temp.Path, "absent.mkv"), null)],
            (_, _, _) => Task.FromResult(2));

        Assert.Null(located);
    }

    /// <summary>
    /// Par la fenêtre : lire un film dont le disque n'est pas là pose la question avec son MediaID, et renoncer
    /// ne lit rien et ne touche pas à l'emplacement enregistré.
    /// </summary>
    [Fact]
    public async Task Playing_a_film_whose_disc_is_away_asks_for_it_and_keeps_the_recorded_location()
    {
        var db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        db.Initialize(_temp.Path);
        var titles = new TitleRepository(db);
        var files = new MediaFileRepository(db);
        var images = new ImageStore(db, Path.Combine(_temp.Path, "img"));
        var import = new LibraryImportService(titles, files, images, null, () => new MediaProbe("absent", "absent"), NullLogger<LibraryImportService>.Instance);
        var id = titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var recorded = Path.Combine(_temp.Path, "disque-absent", "Heat.mkv");
        var fileId = files.Attach(id, recorded, 1024);
        files.SetVolumeLabel(fileId, "FILMS1");

        var dialogs = new MainWindowViewModelTests.FakeDialogs();
        var vm = new MainWindowViewModel(titles, new AppPaths(_temp.Path, isPortable: false), dialogs,
            media: new MediaContext(files, import, new SettingsStore(db)))
        {
            MediaRoots = () => [],
        };
        vm.Load();
        vm.SelectedTitles.Add(vm.Titles[0]);

        // Chercher ailleurs (rien), puis renoncer.
        dialogs.Choices.Enqueue(1);
        dialogs.Choices.Enqueue(2);
        await vm.PlayCommand.ExecuteAsync(null);

        Assert.Equal(2, dialogs.Asked.Count);
        Assert.Contains("FILMS1", dialogs.Asked[0], StringComparison.Ordinal);
        Assert.Contains("FILMS1", vm.Status, StringComparison.Ordinal);
        Assert.Equal(recorded, Assert.Single(files.FilesOf(id)).FullPath);
        Assert.False(titles.Get(id)!.ViewCount > 0);
    }
}
