using Cinedex.Core;
using Cinedex.Core.Titles;

namespace Cinedex.Tests;

public sealed class CoreTests
{
    [Theory]
    [InlineData("The Matrix", "MATRIX")]
    [InlineData("Le Fabuleux Destin d'Amélie Poulain", "FABULEUX DESTIN D'AMELIE POULAIN")]
    [InlineData("L'Arnaque", "ARNAQUE")]
    [InlineData("A", "A")]
    [InlineData("  Été meurtrier ", "ETE MEURTRIER")]
    public void Sort_title_drops_articles_and_accents(string title, string expected)
    {
        Assert.Equal(expected, SortTitle.Compute(title));
    }

    [Fact]
    public void Portable_marker_puts_data_next_to_executable()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, AppPaths.PortableMarker), string.Empty);

        var paths = AppPaths.Resolve(temp.Path);

        Assert.True(paths.IsPortable);
        Assert.Equal(Path.Combine(temp.Path, "data"), paths.DataRoot);
    }

    [Fact]
    public void Without_marker_data_goes_to_user_profile()
    {
        using var temp = new TempDirectory();

        var paths = AppPaths.Resolve(temp.Path, Path.Combine(temp.Path, "profile"));

        Assert.False(paths.IsPortable);
        Assert.Equal(Path.Combine(temp.Path, "profile"), paths.DataRoot);
    }

    [Theory]
    [InlineData("../../etc", "_.._etc")]
    [InlineData("Films:HD", "Films_HD")]
    [InlineData("Mes films", "Mes films")]
    public void Catalog_names_cannot_escape_the_catalogs_folder(string name, string expected)
    {
        Assert.Equal(expected, AppPaths.SanitizeName(name));
        var paths = new AppPaths(Path.GetTempPath(), isPortable: false);
        var dir = Path.GetFullPath(paths.CatalogDirectory(name));
        Assert.StartsWith(Path.GetFullPath(paths.CatalogsDirectory), dir, StringComparison.Ordinal);
    }
}
