using Cinedex.App;
using Cinedex.App.Services;
using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Media;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cinedex.Tests;

/// <summary>
/// Pastille de présence (bandeau droit et fiche) : vérifiée sur le disque en direct, hors du fil d'interface,
/// avec un délai par fichier ; le constat est écrit en base. Et le bouton Lire de la fiche, et l'Accueil qui
/// ouvre la fiche du film cliqué.
/// </summary>
public sealed class FilePresenceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _titles;
    private readonly MediaFileRepository _files;

    public FilePresenceTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _titles = new TitleRepository(_db);
        _files = new MediaFileRepository(_db);
    }

    public void Dispose() => _temp.Dispose();

    private (long TitleId, long FileId, string Path) TitleWithFile(string name, bool onDisk, string? mediaId = null)
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, name, 2000));
        var path = Path.Combine(_temp.Path, "films", name + ".mkv");
        if (onDisk)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[16]);
        }

        var fileId = _files.Attach(id, path, 16);
        if (mediaId is not null)
        {
            _files.SetVolumeLabel(fileId, mediaId);
        }

        return (id, fileId, path);
    }

    private FilePresenceViewModel Presence(Func<string, bool>? exists = null, TimeSpan? timeout = null) =>
        // Délai large par défaut : la suite complète charge les fils, et un disque local n'a pas à « dépasser le délai ».
        new(checkedFiles => _files.RecordPresence(checkedFiles), exists, timeout ?? TimeSpan.FromSeconds(30));

    [Fact]
    public async Task A_file_on_disk_is_present_and_its_missing_mark_is_cleared()
    {
        var (id, fileId, _) = TitleWithFile("Heat", onDisk: true);
        _files.RecordPresence([(fileId, false)]);
        Assert.True(Assert.Single(_files.FilesOf(id)).IsMissing);
        var presence = Presence();

        await presence.CheckAsync(id, _files.FilesOf(id));

        Assert.Equal(PresenceState.Present, presence.State);
        Assert.True(presence.IsVisible);
        Assert.Equal("Présent", presence.Text);
        Assert.Contains("Présent : ", presence.ToolTip, StringComparison.Ordinal);
        Assert.False(Assert.Single(_files.FilesOf(id)).IsMissing);
    }

    [Fact]
    public async Task A_missing_file_names_its_media_id_and_is_marked_missing()
    {
        var (id, _, _) = TitleWithFile("Alien", onDisk: false, mediaId: "FILMS1");
        var presence = Presence();

        await presence.CheckAsync(id, _files.FilesOf(id));

        Assert.Equal(PresenceState.Absent, presence.State);
        Assert.True(presence.IsAbsent);
        Assert.Equal("Absent – branchez « FILMS1 »", presence.Text);
        Assert.True(Assert.Single(_files.FilesOf(id)).IsMissing);

        // Le filtre rapide « Fichiers manquants » dit la même chose que la pastille.
        Assert.Contains(_titles.Query(new TitleQuery(Filter: QuickFilter.MissingFiles)), t => t.Id == id);
    }

    [Fact]
    public async Task Several_parts_are_present_when_one_is_there_and_each_is_listed()
    {
        var (id, _, _) = TitleWithFile("Kill Bill", onDisk: true);
        var part2 = Path.Combine(_temp.Path, "ailleurs", "Kill Bill CD2.mkv");
        _files.Attach(id, part2, 16, part: 2);
        var presence = Presence();

        await presence.CheckAsync(id, _files.FilesOf(id));

        Assert.Equal(PresenceState.Present, presence.State);
        Assert.Equal(2, presence.Results.Count);
        var lines = presence.ToolTip!.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("Présent : ", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("Absent : ", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_drive_that_does_not_answer_in_time_shows_absent_timed_out_and_writes_nothing()
    {
        var (id, _, _) = TitleWithFile("Ran", onDisk: false);
        using var never = new ManualResetEventSlim(false);
        var presence = Presence(exists: _ =>
        {
            // Ne répond qu'une fois le test fini : seul le délai peut conclure.
            never.Wait(TimeSpan.FromSeconds(60));
            return true;
        }, timeout: TimeSpan.FromMilliseconds(50));

        await presence.CheckAsync(id, _files.FilesOf(id));
        never.Set();

        Assert.Equal(PresenceState.TimedOut, presence.State);
        Assert.Equal("Absent (délai dépassé)", presence.Text);
        Assert.StartsWith("Délai dépassé : ", presence.ToolTip, StringComparison.Ordinal);

        // On ne sait pas si le fichier est là : la base n'est pas touchée.
        Assert.False(Assert.Single(_files.FilesOf(id)).IsMissing);
    }

    [Fact]
    public async Task A_result_arriving_after_the_title_changed_is_ignored()
    {
        var (first, _, firstPath) = TitleWithFile("Premier", onDisk: true);
        var (second, _, _) = TitleWithFile("Second", onDisk: false, mediaId: "FILMS2");
        using var slow = new ManualResetEventSlim(false);
        var presence = Presence(exists: path =>
        {
            if (path == firstPath)
            {
                slow.Wait(TimeSpan.FromSeconds(10));
            }

            return File.Exists(path);
        });

        var stale = presence.CheckAsync(first, _files.FilesOf(first));
        Assert.Equal(PresenceState.Checking, presence.State);
        Assert.Equal("Vérification…", presence.Text);

        // L'utilisateur passe au titre suivant pendant que le premier disque se fait attendre.
        await presence.CheckAsync(second, _files.FilesOf(second));
        slow.Set();
        await stale;

        Assert.Equal(second, presence.TitleId);
        Assert.Equal(PresenceState.Absent, presence.State);
        Assert.Equal("Absent – branchez « FILMS2 »", presence.Text);
        Assert.All(presence.Results, r => Assert.Equal(second, r.File.TitleId));
    }

    [Fact]
    public async Task A_title_without_files_has_no_badge()
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Souhait", 2024));
        var presence = Presence();

        await presence.CheckAsync(id, _files.FilesOf(id));

        Assert.Equal(PresenceState.None, presence.State);
        Assert.False(presence.IsVisible);
        Assert.Null(presence.ToolTip);
    }

    [Fact]
    public void Recording_presence_counts_only_real_changes()
    {
        var (id, fileId, _) = TitleWithFile("Seven", onDisk: false);

        Assert.Equal(1, _files.RecordPresence([(fileId, false)]));
        Assert.Equal(0, _files.RecordPresence([(fileId, false)]));
        Assert.True(Assert.Single(_files.FilesOf(id)).IsMissing);
        Assert.Equal(1, _files.RecordPresence([(fileId, true)]));
        Assert.False(Assert.Single(_files.FilesOf(id)).IsMissing);
        Assert.Equal(0, _files.RecordPresence([]));
    }

    private MainWindowViewModel Window(MainWindowViewModelTests.FakeDialogs dialogs)
    {
        var images = new ImageStore(_db, Path.Combine(_temp.Path, "img"));
        var import = new LibraryImportService(_titles, _files, images, null, () => new MediaProbe("absent", "absent"), NullLogger<LibraryImportService>.Instance);
        return new MainWindowViewModel(_titles, new AppPaths(_temp.Path, isPortable: false), dialogs,
            media: new MediaContext(_files, import, new SettingsStore(_db)))
        {
            MediaRoots = () => [],
        };
    }

    [Fact]
    public async Task The_right_panel_badge_follows_the_selection_and_refreshes_the_technical_summary()
    {
        var (absent, _, _) = TitleWithFile("Absent", onDisk: false, mediaId: "FILMS1");
        var (present, _, _) = TitleWithFile("Présent", onDisk: true);
        var vm = Window(new MainWindowViewModelTests.FakeDialogs());
        vm.Load();

        vm.SelectedTitles.Add(vm.Titles.Single(t => t.Id == absent));
        await vm.Record.CheckPresenceAsync();
        Assert.Equal(PresenceState.Absent, vm.Record.Presence.State);
        Assert.Contains("FILMS1", vm.Record.Presence.Text, StringComparison.Ordinal);
        Assert.Contains("(introuvable)", vm.Record.TechnicalSummary, StringComparison.Ordinal);

        vm.SelectedTitles.Clear();
        vm.SelectedTitles.Add(vm.Titles.Single(t => t.Id == present));
        await vm.Record.CheckPresenceAsync();
        Assert.Equal(present, vm.Record.Presence.TitleId);
        Assert.Equal(PresenceState.Present, vm.Record.Presence.State);
    }

    [Fact]
    public async Task The_play_button_of_the_editor_takes_the_playback_path_and_asks_in_front_of_the_editor()
    {
        var (id, _, recorded) = TitleWithFile("Heat", onDisk: false, mediaId: "FILMS1");
        var dialogs = new MainWindowViewModelTests.FakeDialogs();
        var vm = Window(dialogs);
        vm.Load();
        vm.SelectedTitles.Add(vm.Titles[0]);
        var askedByEditor = new List<string>();
        dialogs.OnEdit = editor =>
        {
            editor.Ask = (_, message, _) =>
            {
                askedByEditor.Add(message);
                return Task.FromResult(2);
            };
            Assert.True(editor.PlayCommand.CanExecute(null));
            editor.PlayCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        };

        await vm.EditCommand.ExecuteAsync(null);

        // Le même chemin que le bandeau droit : on demande le disque par son MediaID, et renoncer ne lit rien.
        Assert.Contains("FILMS1", Assert.Single(askedByEditor), StringComparison.Ordinal);
        Assert.Empty(dialogs.Asked);
        Assert.Contains("FILMS1", vm.Status, StringComparison.Ordinal);
        Assert.Equal(recorded, Assert.Single(_files.FilesOf(id)).FullPath);
        Assert.Equal(PresenceState.Absent, dialogs.Edited!.Presence.State);
    }

    [Fact]
    public async Task The_editor_play_command_runs_the_given_delegate_and_rechecks_the_badge()
    {
        var (id, _, _) = TitleWithFile("Alien", onDisk: true);
        var played = 0;
        var editor = new TitleEditorViewModel(
            _titles.Get(id)!, _titles.Genres(), _titles.RefItems("medium"), _titles.RefItems("case"), _titles.RefItems("version"), extras: new TitleEditorExtras(Files: _files, Play: () =>
            {
                played++;
                return Task.CompletedTask;
            }));

        await editor.CheckPresenceAsync();
        Assert.Equal(PresenceState.Present, editor.Presence.State);
        await editor.PlayCommand.ExecuteAsync(null);

        Assert.Equal(1, played);
        Assert.Equal(PresenceState.Present, editor.Presence.State);

        // Sans fichier (liste de souhaits), pas de lecture.
        var wish = _titles.Add(new NewTitle(TitleKind.Movie, "Souhait", 2025));
        var empty = new TitleEditorViewModel(_titles.Get(wish)!, _titles.Genres(), _titles.RefItems("medium"), _titles.RefItems("case"), _titles.RefItems("version"), extras: new TitleEditorExtras(Files: _files, Play: () => Task.CompletedTask));
        Assert.False(empty.PlayCommand.CanExecute(null));
    }

    [Fact]
    public async Task Clicking_a_film_on_the_home_page_opens_its_editor_and_keeps_it_selected()
    {
        _titles.Add(new NewTitle(TitleKind.Movie, "Alpha", 2000));
        var targetId = _titles.Add(new NewTitle(TitleKind.Movie, "Bravo", 2001));
        var dialogs = new MainWindowViewModelTests.FakeDialogs();
        var vm = Window(dialogs);
        vm.Load();

        // Une recherche cache le film : il faut relire la liste (Refresh) pour le retrouver.
        vm.SearchText = "Alpha";
        await vm.ViewRefresh;
        Assert.DoesNotContain(vm.Titles, t => t.Id == targetId);
        var clicked = _titles.Query(new TitleQuery(Ids: [targetId])).Single();

        await vm.OpenFromHomeCommand.ExecuteAsync(clicked);

        Assert.Equal(targetId, dialogs.Edited!.TitleId);
        Assert.Equal(targetId, Assert.Single(vm.SelectedTitles).Id);
        Assert.Null(vm.Home);
    }
}
