using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cinedex.Tests;

/// <summary>
/// Deux façons d'écrire un .nfo : compléter ce qui manque, ou remplacer ce qui s'y trouve. La seconde est
/// ce qu'il faut après avoir corrigé une fiche, sans quoi le .nfo garde son ancienne valeur.
/// </summary>
public sealed class NfoUpdateTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _titles;
    private readonly MediaFileRepository _files;
    private readonly NfoService _nfo;

    public NfoUpdateTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _titles = new TitleRepository(_db);
        _files = new MediaFileRepository(_db);
        _nfo = new NfoService(_titles, new MetadataRepository(_db), _files, new SettingsStore(_db), NullLogger<NfoService>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Writing_completes_what_is_missing_and_updating_replaces_what_is_there()
    {
        var video = Path.Combine(_temp.Path, "Heat (1995).mkv");
        File.WriteAllBytes(video, new byte[1024]);
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        _files.Attach(id, video, 1024);
        _titles.Update(_titles.Get(id)! with { Overview = "Un braqueur et un flic." });

        // Un .nfo qui existe déjà, avec un titre faux et sans synopsis.
        var nfoPath = Path.ChangeExtension(video, ".nfo");
        File.WriteAllText(nfoPath, """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <movie>
              <title>Titre erroné</title>
            </movie>
            """);

        // Compléter : le synopsis arrive, le titre faux reste.
        Assert.Equal(1, _nfo.WriteForTitle(id));
        var completed = File.ReadAllText(nfoPath);
        Assert.Contains("Titre erroné", completed, StringComparison.Ordinal);
        Assert.Contains("Un braqueur et un flic.", completed, StringComparison.Ordinal);

        // Mettre à jour : le titre de la fiche l'emporte.
        Assert.Equal(1, _nfo.WriteForTitle(id, new Cinedex.Media.NfoWriteOptions { OverwriteExisting = true, Backup = false }));
        var updated = File.ReadAllText(nfoPath);
        Assert.DoesNotContain("Titre erroné", updated, StringComparison.Ordinal);
        Assert.Contains("<title>Heat</title>", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_command_asks_first_and_reports_what_it_rewrote()
    {
        var video = Path.Combine(_temp.Path, "Heat (1995).mkv");
        File.WriteAllBytes(video, new byte[1024]);
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        _files.Attach(id, video, 1024);
        File.WriteAllText(Path.ChangeExtension(video, ".nfo"), """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <movie><title>Titre erroné</title></movie>
            """);

        var dialogs = new MainWindowViewModelTests.FakeDialogs { Answer = false };
        var media = new MediaContext(
            _files,
            new LibraryImportService(_titles, _files, new ImageStore(_db, Path.Combine(_temp.Path, "img")), null,
                () => new Cinedex.Media.MediaProbe("absent", "absent"), NullLogger<LibraryImportService>.Instance),
            new SettingsStore(_db),
            _nfo);
        var vm = new MainWindowViewModel(_titles, new AppPaths(_temp.Path, isPortable: false), dialogs, media: media);
        vm.Load();
        vm.SelectedTitles.Add(vm.Titles[0]);

        // Refus : le fichier n'est pas touché.
        await vm.UpdateNfoCommand.ExecuteAsync(null);
        Assert.Contains("Titre erroné", File.ReadAllText(Path.ChangeExtension(video, ".nfo")), StringComparison.Ordinal);

        dialogs.Answer = true;
        await vm.UpdateNfoCommand.ExecuteAsync(null);

        Assert.DoesNotContain("Titre erroné", File.ReadAllText(Path.ChangeExtension(video, ".nfo")), StringComparison.Ordinal);
        Assert.Contains("1", vm.Status, StringComparison.Ordinal);
    }
}
