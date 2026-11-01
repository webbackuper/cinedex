using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

public sealed class VolumeTests
{
    [Fact]
    public void Files_follow_their_disk_when_the_drive_letter_changes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var plugged = new Volumes([(@"C:\", "SYSTEME"), (@"F:\", "FILMS-A")]);

        Assert.Equal("FILMS-A", plugged.LabelOf(@"F:\Films\Heat.mkv"));
        Assert.Equal(@"F:\Films\Heat.mkv", plugged.Resolve(@"E:\Films\Heat.mkv", "FILMS-A"));
        Assert.Equal(@"E:\Films\Heat.mkv", plugged.Resolve(@"E:\Films\Heat.mkv", "FILMS-B"));
        Assert.Equal(@"E:\Films\Heat.mkv", plugged.Resolve(@"E:\Films\Heat.mkv", null));
    }

    [Fact]
    public void Two_disks_with_the_same_path_make_two_files()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        var current = new Volumes([(@"E:\", "FILMS-A")]);
        var files = new MediaFileRepository(db) { VolumeProvider = () => current };
        var heat = titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var ran = titles.Add(new NewTitle(TitleKind.Movie, "Ran", 1985));

        files.Attach(heat, @"E:\Films\film.mkv", 10);
        Assert.Contains(@"E:\Films\film.mkv", files.KnownPaths());

        // Autre disque branché sous la même lettre : le même chemin n'est pas « déjà connu ».
        current = new Volumes([(@"E:\", "FILMS-B")]);
        Assert.DoesNotContain(@"E:\Films\film.mkv", files.KnownPaths());
        files.Attach(ran, @"E:\Films\film.mkv", 20);

        Assert.Equal("FILMS-A", Assert.Single(files.FilesOf(heat)).VolumeLabel);
        Assert.Equal("FILMS-B", Assert.Single(files.FilesOf(ran)).VolumeLabel);
        Assert.Equal(["FILMS-A", "FILMS-B"], files.VolumeLabels());

        files.SetVolumeLabel(files.FilesOf(ran)[0].Id, "FILMS-C");
        Assert.Equal("FILMS-C", files.FilesOf(ran)[0].VolumeLabel);
    }

    [Fact]
    public void Files_imported_before_media_id_get_it_at_the_next_scan()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        var path = Path.Combine(temp.Path, "Films", "heat.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[10]);
        var id = titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        new MediaFileRepository(db) { VolumeProvider = () => new Volumes([]) }.Attach(id, path, 10);

        var files = new MediaFileRepository(db) { VolumeProvider = () => new Volumes([(temp.Path, "FILMS-A")]) };
        Assert.Contains(path, files.KnownPaths());
        Assert.Equal("FILMS-A", Assert.Single(files.FilesOf(id)).VolumeLabel);
    }
}
