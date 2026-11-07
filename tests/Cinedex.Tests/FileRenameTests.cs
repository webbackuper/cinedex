using Cinedex.App.Services;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

/// <summary>
/// Renommage des fichiers d'après la fiche. C'est la seule action qui touche au disque et elle ne se défait
/// pas : le plan doit donc être juste avant d'être appliqué, et refuser tout ce qui est douteux.
/// </summary>
public sealed class FileRenameTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CatalogDatabase _db;
    private readonly TitleRepository _titles;
    private readonly MediaFileRepository _files;
    private readonly FileRenameService _renames;

    public FileRenameTests()
    {
        _db = new CatalogDatabase(Path.Combine(_temp.Path, "cinedex.db"));
        _db.Initialize(_temp.Path);
        _titles = new TitleRepository(_db);
        _files = new MediaFileRepository(_db);
        _renames = new FileRenameService(_titles, _files);
    }

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("Heat", 1995, "Heat (1995)")]
    [InlineData("Sans année", null, "Sans année")]
    [InlineData("Alien : le huitième passager", 1979, "Alien le huitième passager (1979)")]
    [InlineData("C'est arrivé près de chez vous ?", 1992, "C'est arrivé près de chez vous (1992)")]
    public void The_name_comes_from_the_record_and_survives_the_file_system(string title, int? year, string expected)
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, title, year));

        Assert.Equal(expected, FileRenameService.NameOf(_titles.Get(id)!));
    }

    [Fact]
    public void The_file_and_its_nfo_are_renamed_together_and_the_catalogue_follows()
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var video = Path.Combine(_temp.Path, "heat.1995.1080p.BluRay.mkv");
        File.WriteAllBytes(video, new byte[1024]);
        File.WriteAllText(Path.ChangeExtension(video, ".nfo"), "<movie />");
        _files.Attach(id, video, 1024);

        var plan = Assert.Single(_renames.Plan([id]));
        Assert.True(plan.CanRename);
        Assert.Equal("Heat (1995).mkv", plan.NewName);

        Assert.Equal(1, _renames.Apply([plan], out var problems));
        Assert.Empty(problems);

        var target = Path.Combine(_temp.Path, "Heat (1995).mkv");
        Assert.True(File.Exists(target));
        Assert.False(File.Exists(video));
        Assert.True(File.Exists(Path.Combine(_temp.Path, "Heat (1995).nfo")));
        Assert.False(File.Exists(Path.ChangeExtension(video, ".nfo")));

        // La base suit : le fichier reste rattaché au titre, à son nouveau nom.
        var kept = Assert.Single(_files.FilesOf(id));
        Assert.Equal(target, kept.FullPath);
    }

    [Fact]
    public void Several_files_of_one_film_keep_distinct_names()
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        foreach (var name in new[] { "a.mkv", "b.mkv" })
        {
            File.WriteAllBytes(Path.Combine(_temp.Path, name), new byte[1024]);
        }

        _files.Attach(id, Path.Combine(_temp.Path, "a.mkv"), 1024, part: 1);
        _files.Attach(id, Path.Combine(_temp.Path, "b.mkv"), 1024, part: 2);

        var plan = _renames.Plan([id]);

        Assert.Equal(["Heat (1995) - 1.mkv", "Heat (1995) - 2.mkv"], plan.Select(p => p.NewName));
        Assert.All(plan, p => Assert.True(p.CanRename));
    }

    [Fact]
    public void What_is_doubtful_is_refused_rather_than_risked()
    {
        var id = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var already = Path.Combine(_temp.Path, "Heat (1995).mkv");
        File.WriteAllBytes(already, new byte[1024]);
        _files.Attach(id, already, 1024);

        // Déjà au bon nom.
        Assert.Equal("déjà au bon nom", Assert.Single(_renames.Plan([id])).Problem);

        // Un autre film dont le nom cible est déjà pris par le premier.
        var twin = _titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var other = Path.Combine(_temp.Path, "heat-autre.mkv");
        File.WriteAllBytes(other, new byte[1024]);
        _files.Attach(twin, other, 1024);
        var blocked = _renames.Plan([twin]).Single();
        Assert.False(blocked.CanRename);
        Assert.Equal("un fichier porte déjà ce nom", blocked.Problem);

        // Un fichier absent du disque n'est pas renommé non plus.
        var ghost = _titles.Add(new NewTitle(TitleKind.Movie, "Fantôme", 2000));
        _files.Attach(ghost, Path.Combine(_temp.Path, "absent.mkv"), 1024);
        Assert.Equal("fichier introuvable", _renames.Plan([ghost]).Single().Problem);

        // Rien n'a bougé sur le disque.
        Assert.True(File.Exists(already));
        Assert.True(File.Exists(other));
    }
}
