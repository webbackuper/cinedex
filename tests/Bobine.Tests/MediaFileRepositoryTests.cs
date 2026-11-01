using Bobine.Core.Titles;
using Bobine.Data;

namespace Bobine.Tests;

public sealed class MediaFileRepositoryTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TitleRepository _titles;
    private readonly MediaFileRepository _files;
    private readonly string _library;

    public MediaFileRepositoryTests()
    {
        var db = new CatalogDatabase(Path.Combine(_temp.Path, "bobine.db"));
        db.Initialize(_temp.Path);
        _titles = new TitleRepository(db);
        _files = new MediaFileRepository(db);
        _library = Directory.CreateDirectory(Path.Combine(_temp.Path, "Films")).FullName;
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Files_under_a_root_are_stored_relative_and_survive_a_root_move()
    {
        var rootId = _files.AddScanRoot(_library, "movies");
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var file = Touch(Path.Combine(_library, "Heat", "heat.mkv"), 100);

        _files.Attach(id, file, 100);
        Assert.Equal(file, Assert.Single(_files.FilesOf(id)).FullPath);
        Assert.Contains(file, _files.KnownPaths());
        Assert.Equal("media_file", _titles.Get(id)!.Copy!.MediumCode);

        var moved = Path.Combine(_temp.Path, "NAS", "Films");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        Directory.Move(_library, moved);
        _files.MoveScanRoot(rootId, moved);

        var entry = Assert.Single(_files.FilesOf(id));
        Assert.Equal(Path.Combine(moved, "Heat", "heat.mkv"), entry.FullPath);
        Assert.Empty(_files.CheckMissing());
    }

    [Fact]
    public void Attaching_the_same_file_twice_does_not_duplicate()
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var file = Touch(Path.Combine(_library, "heat.mkv"), 10);

        _files.Attach(id, file, 10);
        _files.Attach(id, file, 12);

        Assert.Equal(12, Assert.Single(_files.FilesOf(id)).SizeBytes);
    }

    [Fact]
    public void Missing_files_are_detected_and_relinked_by_name_and_size()
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Alien", 1979));
        var file = Touch(Path.Combine(_library, "alien.mkv"), 321);
        _files.Attach(id, file, 321);

        var elsewhere = Path.Combine(_temp.Path, "Ailleurs", "alien.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);
        File.Move(file, elsewhere);

        Assert.Single(_files.CheckMissing());
        Assert.Equal([id], _titles.Query(new TitleQuery(Filter: QuickFilter.MediaFiles)).Select(t => t.Id));

        Assert.Equal(1, _files.RelinkMoved([(elsewhere, 321L), (Path.Combine(_temp.Path, "x", "alien.mkv"), 999L)]));
        Assert.Equal(elsewhere, Assert.Single(_files.FilesOf(id)).FullPath);
        Assert.Empty(_files.CheckMissing());
    }

    [Fact]
    public void Absolute_prefixes_can_be_replaced()
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Ran", 1985));
        _files.Attach(id, Path.Combine(_temp.Path, "Old", "ran.mkv"), 1);

        Assert.Equal(1, _files.ReplacePathPrefix(Path.Combine(_temp.Path, "Old"), Path.Combine(_temp.Path, "New")));
        Assert.Equal(Path.Combine(_temp.Path, "New", "ran.mkv"), Assert.Single(_files.FilesOf(id)).FullPath);
    }

    [Fact]
    public void Properties_fill_runtime_when_missing()
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var fileId = _files.Attach(id, Touch(Path.Combine(_library, "h.mkv"), 1), 1);

        _files.SaveProperties(fileId, new MediaFileProperties("matroska", "hevc", 3840, 2160, 23.976, 10, "16:9", ["HDR10"], 10200, [("truehd", 8, "eng")], ["fre"]));

        Assert.Equal(170, _titles.Get(id)!.RuntimeMinutes);
        Assert.Equal([id], _titles.Query(new TitleQuery(Filter: QuickFilter.UltraHighDefinition)).Select(t => t.Id));
    }

    private static string Touch(string path, int bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }
}
