using Cinedex.App.ViewModels;
using Cinedex.Core;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

public sealed class CatalogTests
{
    [Fact]
    public void Catalogs_are_independent_and_current_is_remembered()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path, isPortable: false);
        var manager = new CatalogManager(paths);

        Assert.Equal(AppPaths.DefaultCatalogName, manager.Current);
        var films = manager.Create("Films");
        var series = manager.Create("Séries TV");
        new TitleRepository(new CatalogDatabase(manager.DatabasePath(films))).Add(new NewTitle(TitleKind.Movie, "Heat", 1995));

        Assert.Equal(["Films", "Séries TV"], manager.List().Select(c => c.Name));
        Assert.Equal(0, new TitleRepository(new CatalogDatabase(manager.DatabasePath(series))).Count());
        Assert.Throws<InvalidOperationException>(() => manager.Create("Films"));

        manager.SetCurrent(series);
        Assert.Equal("Séries TV", new CatalogManager(paths).Current);
    }

    [Fact]
    public void Open_catalog_cannot_be_renamed_or_removed_and_removal_keeps_a_copy()
    {
        using var temp = new TempDirectory();
        var manager = new CatalogManager(new AppPaths(temp.Path, isPortable: false));
        manager.Create("A");
        manager.Create("B");

        Assert.Throws<InvalidOperationException>(() => manager.Remove("A", openCatalog: "A"));
        Assert.Throws<InvalidOperationException>(() => manager.Rename("A", "C", openCatalog: "A"));

        Assert.Equal("C", manager.Rename("B", "C", openCatalog: "A"));
        var copy = manager.Remove("C", openCatalog: "A");

        Assert.True(File.Exists(Path.Combine(copy, "cinedex.db")));
        Assert.Equal(["A"], manager.List().Select(c => c.Name));
    }

    [Fact]
    public void Creating_a_catalog_copies_settings_and_opens_it()
    {
        using var temp = new TempDirectory();
        var manager = new CatalogManager(new AppPaths(temp.Path, isPortable: false));
        var current = manager.Create("Principal");
        var settings = new SettingsStore(new CatalogDatabase(manager.DatabasePath(current)));
        settings.Set("tmdb.language", "fr-FR");
        string? opened = null;
        var vm = new CatalogsViewModel(manager, current, name => opened = name, settings) { NameText = "Enfants" };

        vm.CreateCommand.Execute(null);

        Assert.Equal("Enfants", opened);
        Assert.True(vm.Switched);
        Assert.Equal("fr-FR", new SettingsStore(new CatalogDatabase(manager.DatabasePath("Enfants"))).Get("tmdb.language"));
    }

    [Fact]
    public void The_editor_changes_the_tmdb_number_and_the_poster()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        var images = new ImageStore(db, Path.Combine(temp.Path, "img"));
        var id = titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var source = Path.Combine(temp.Path, "affiche.png");
        File.WriteAllBytes(source, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var editor = new TitleEditorViewModel(titles.Get(id)!, titles.Genres(), [], [], [], null, new TitleEditorExtras(
            SetPoster: path => Task.FromResult(images.SetFromFile(id, path, "poster")),
            RemovePoster: () =>
            {
                images.Remove(id, "poster");
                return Task.CompletedTask;
            }));

        // Le numéro TMDb se saisit, se vérifie et s'enregistre.
        Assert.Equal(string.Empty, editor.TmdbId);
        editor.TmdbId = "pas un nombre";
        Assert.False(editor.TryBuild(out _));
        Assert.Contains("TMDb", editor.LastError!, StringComparison.Ordinal);
        editor.TmdbId = "949";
        Assert.True(editor.TryBuild(out var linked));
        titles.Update(linked!);
        Assert.Equal(949, titles.Get(id)!.TmdbId);

        // Vidé, il détache la fiche de TMDb.
        editor.TmdbId = string.Empty;
        Assert.True(editor.TryBuild(out var detached));
        titles.Update(detached!);
        Assert.Null(titles.Get(id)!.TmdbId);
    }

    [Fact]
    public async Task The_editor_sets_and_removes_the_poster()
    {
        using var temp = new TempDirectory();
        var db = new CatalogDatabase(Path.Combine(temp.Path, "cinedex.db"));
        db.Initialize(temp.Path);
        var titles = new TitleRepository(db);
        var images = new ImageStore(db, Path.Combine(temp.Path, "img"));
        var id = titles.Add(new NewTitle(TitleKind.Movie, "Heat", 1995));
        var source = Path.Combine(temp.Path, "affiche.png");
        File.WriteAllBytes(source, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var editor = new TitleEditorViewModel(titles.Get(id)!, titles.Genres(), [], [], [], null, new TitleEditorExtras(
            SetPoster: path => Task.FromResult(images.SetFromFile(id, path, "poster")),
            RemovePoster: () =>
            {
                images.Remove(id, "poster");
                return Task.CompletedTask;
            }));

        Assert.False(editor.HasPoster);
        Assert.True(editor.CanChoosePoster);
        Assert.False(editor.RemovePosterCommand.CanExecute(null));

        await editor.SetPosterAsync(source);
        Assert.True(editor.HasPoster);
        Assert.NotNull(images.PathFor(id, "poster"));
        Assert.True(editor.RemovePosterCommand.CanExecute(null));

        await editor.RemovePosterCommand.ExecuteAsync(null);
        Assert.False(editor.HasPoster);
        Assert.Null(images.PathFor(id, "poster"));
    }

}
