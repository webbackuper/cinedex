using Bobine.App.ViewModels;
using Bobine.Core;
using Bobine.Core.Titles;
using Bobine.Data;

namespace Bobine.Tests;

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

        Assert.True(File.Exists(Path.Combine(copy, "bobine.db")));
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
}
